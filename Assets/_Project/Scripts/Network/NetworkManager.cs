using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NativeWebSocket;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;
using Yahtzee.Core;
using Yahtzee.Feedback;

namespace Yahtzee.Network
{
    public sealed class NetworkManager : MonoBehaviour
    {
        public static NetworkManager Instance { get; private set; }

        public static event Action<string> OnUserBanned;
        public static string LastBanReason { get; private set; } = string.Empty;
        public static string LastBanUntil { get; private set; } = string.Empty;
        public static event Action<string, string> OnRequestFailed;
        public static event Action OnConnectionLost;
        public static event Action OnConnectionRestored;
        public static event Action<GameStateResponse> OnMatchStateCaughtUp;
        public static event Action<int[]> OnDiceRolled;
        public static event Action<DiceRolledEvent> OnDiceRolledEvent;
        public static event Action<DiceHeldEvent> OnDiceHeld;
        public static event Action<MatchStartedEvent> OnMatchStarted;
        public static event Action<TurnChangedEvent> OnTurnChanged;
        public static event Action<MatchOverEvent> OnMatchOver;
        public static event Action<OpponentDisconnectedEvent> OnOpponentDisconnected;
        public static event Action<PlayerLeftEvent> OnPlayerLeft;
        public static event Action<TurnTimedOutEvent> OnTurnTimedOut;
        public static event Action<PlayerReconnectedEvent> OnPlayerReconnected;
        public static event Action<OpponentAbandonChoiceEvent> OnOpponentAbandonChoice;
        public static event Action<PlayerReplacedByBotEvent> OnPlayerReplacedByBot;
        public static event Action<MatchmakingResponse> OnQuickMatchUpdated;
        public static event Action<string> OnLobbyJoinFailed;

        private readonly SemaphoreSlim _socketGate = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();

        private SynchronizationContext _main;
        private string _apiBaseUrl;
        private string _token = string.Empty;
        private WebSocket websocket;
        private byte[] _pingBytes;
        private float _nextPingUnscaled = float.PositiveInfinity;
        private float _pingIntervalSeconds = 30f;
        private int _socketGeneration;
        private bool _socketReady;
        private bool _reconnectRunning;
        private bool _connectionLost;
        private Task _socketPump;
        private int? _activeMatchId;
        private bool _searchingQueue;
        private string _expectedChannel = string.Empty;
        private TaskCompletionSource<string> _socketIdTcs;
        private TaskCompletionSource<bool> _subscribeTcs;

        public bool IsAuthenticated => !string.IsNullOrEmpty(_token);
        public bool ConnectionDropped => _connectionLost;
        public int? ActiveMatchId => _activeMatchId;
        public GameStateResponse ResumeState { get; private set; }
        public ProfileResponse CurrentUser { get; private set; }
        public MatchmakingResponse LastQuickMatch { get; private set; }
        public MatchState LastLobby { get; private set; }
        public MatchStartedEvent LastMatchStarted { get; private set; }

        private void SetActiveMatchId(int? matchId, bool persist = true)
        {
            _activeMatchId = matchId;
            if (!persist)
                return;

            var key = YahtzeeConfig.Current.ActiveMatchPrefKey;
            if (matchId.HasValue && matchId.Value > 0)
                PlayerPrefs.SetInt(key, matchId.Value);
            else
                PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// After Play Mode / app restart: pin unfinished match id and show reconnect UI.
        /// Does NOT call /reconnect yet — that would clear the opponent's waiting banner early.
        /// </summary>
        public async Task<bool> TryRestorePersistedMatch()
        {
            if (_activeMatchId.HasValue)
            {
                _connectionLost = true;
                return true;
            }

            var key = YahtzeeConfig.Current.ActiveMatchPrefKey;
            if (!PlayerPrefs.HasKey(key))
                return false;

            var id = PlayerPrefs.GetInt(key, 0);
            if (id <= 0)
            {
                PlayerPrefs.DeleteKey(key);
                PlayerPrefs.Save();
                return false;
            }

            if (string.IsNullOrEmpty(_token))
                return false;

            try
            {
                SetActiveMatchId(id);
                // Read-only catch-up — does not mark the seat online again.
                var state = await GetMatchState(id);
                if (state == null || state.Match == null || state.Match.Status != "playing")
                {
                    SetActiveMatchId(null);
                    ResumeState = null;
                    return false;
                }

                ResumeState = state;
                OnMatchStateCaughtUp?.Invoke(state);
                _connectionLost = true;
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Yahtzee restore match: " + exception.Message);
                SetActiveMatchId(null);
                ResumeState = null;
                return false;
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            if (transform.parent != null)
                transform.SetParent(null, false);

            DontDestroyOnLoad(gameObject);
            _main = SynchronizationContext.Current;
            _pingBytes = Encoding.UTF8.GetBytes("{\"event\":\"pusher:ping\",\"data\":{}}");
            var config = YahtzeeConfig.Current;
            _apiBaseUrl = config.ApiBaseUrl;
            _pingIntervalSeconds = config.PingIntervalSeconds;
            _token = PlayerPrefs.GetString(config.TokenPrefKey, string.Empty);
        }

        private void OnDestroy()
        {
            if (Instance != this)
                return;

            Instance = null;
            _lifetime.Cancel();
            CloseSocketQuietly();
        }

        private void Update()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            if (websocket != null)
            {
                websocket.DispatchMessageQueue();
            }
#endif
            if (websocket == null || _pingBytes == null || websocket.State != WebSocketState.Open)
                return;

            if (Time.unscaledTime < _nextPingUnscaled)
                return;

            var socket = websocket;
            _nextPingUnscaled = Time.unscaledTime + _pingIntervalSeconds;
            SendPing(socket);
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
                LeaveQueueOnExit();
        }

        private void OnApplicationQuit()
        {
            LeaveQueueOnExit();

            if (!_activeMatchId.HasValue || CurrentUser == null || string.IsNullOrEmpty(_token))
                return;

            var matchId = _activeMatchId.Value;
            var userId = CurrentUser.Id;
            // Keep persisted match id so the next launch can show «Переподключиться».
            SetActiveMatchId(null, persist: false);
            var url = _apiBaseUrl + YahtzeeConfig.Current.MatchPath(
                matchId,
                "players/" + userId.ToString(CultureInfo.InvariantCulture) + "/disconnect");
            SendQuitRequest(HttpMethod.Post, url, "{}");
        }

        private void LeaveQueueOnExit()
        {
            if (!_searchingQueue || string.IsNullOrEmpty(_token))
                return;

            _searchingQueue = false;
            SendQuitRequest(HttpMethod.Delete, _apiBaseUrl + YahtzeeConfig.Current.QuickMatchPath, null);
        }

        private void SendQuitRequest(HttpMethod method, string url, string body)
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
                using var message = new HttpRequestMessage(method, url);
                message.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _token);
                message.Headers.TryAddWithoutValidation("Accept", "application/json");
                message.Headers.TryAddWithoutValidation("X-Client-Version", ClientGate.ClientVersion);
                if (body != null)
                    message.Content = new StringContent(body, Encoding.UTF8, "application/json");
                Task.Run(async () =>
                {
                    using var response = await client.SendAsync(message).ConfigureAwait(false);
                }).Wait(TimeSpan.FromSeconds(2));
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Yahtzee quit: " + exception.Message);
            }
        }

        private async void SendPing(WebSocket socket)
        {
            if (socket == null)
                return;

            try
            {
                await socket.Send(_pingBytes);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Yahtzee ping: " + exception.Message);
            }
        }

        public async Task<bool> LoginAsGuest(string deviceId, string nickname, string platform, string deviceSecret)
        {
            if (!IsDeviceId(deviceId))
            {
                OnRequestFailed?.Invoke(
                    "VALIDATION_ERROR",
                    "Идентификатор устройства должен содержать от 8 до 128 символов: буквы, цифры, точка, _, : или -.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(deviceSecret) || deviceSecret.Length < 32 || deviceSecret.Length > 128)
            {
                OnRequestFailed?.Invoke(
                    "VALIDATION_ERROR",
                    "Секрет устройства должен содержать от 32 до 128 символов.");
                return false;
            }

            if (!IsPlatform(platform))
            {
                OnRequestFailed?.Invoke(
                    "VALIDATION_ERROR",
                    "Платформа должна быть одной из: android, ios, webgl, pc.");
                return false;
            }

            var payload = new JObject
            {
                ["device_id"] = deviceId,
                ["device_secret"] = deviceSecret,
                ["platform"] = platform
            };

            if (!string.IsNullOrWhiteSpace(nickname))
                payload["nickname"] = nickname.Trim();

            HttpResponse response;
            try
            {
                response = await SendAsync(
                    UnityWebRequest.kHttpVerbPOST,
                    YahtzeeConfig.Current.GuestLoginPath,
                    payload.ToString(Formatting.None),
                    "application/json",
                    false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                OnRequestFailed?.Invoke("NETWORK", exception.Message);
                return false;
            }

            if (response.StatusCode == 403 && IsBanned(response.Body))
            {
                PublishBan(response.Body);
                return false;
            }

            if (!response.IsSuccess)
            {
                var failure = NetworkException.From(response.StatusCode, response.Body);
                OnRequestFailed?.Invoke(failure.ErrorCode, failure.Message);
                return false;
            }

            AuthResponse auth;
            try
            {
                auth = JsonConvert.DeserializeObject<AuthResponse>(response.Body, NetworkJson.Settings);
            }
            catch (JsonException exception)
            {
                OnRequestFailed?.Invoke("INVALID_RESPONSE", exception.Message);
                return false;
            }

            if (auth == null || string.IsNullOrEmpty(auth.Token))
            {
                OnRequestFailed?.Invoke("INVALID_RESPONSE", "Сервер не выдал токен.");
                return false;
            }

            _token = auth.Token;
            CurrentUser = auth.User;
            SkinManager.Remember(auth.User);
            PlayerPrefs.SetString(YahtzeeConfig.Current.TokenPrefKey, _token);
            PlayerPrefs.Save();
            return true;
        }

        public async Task<ProfileResponse> GetProfile()
        {
            var response = await SendAsync(UnityWebRequest.kHttpVerbGET, YahtzeeConfig.Current.ProfilePath, null, null, true);
            ThrowIfBanned(response);
            if (!response.IsSuccess)
                throw NetworkException.From(response.StatusCode, response.Body);

            var profile = JsonConvert.DeserializeObject<ProfileResponse>(response.Body, NetworkJson.Settings);
            if (profile == null)
                throw new NetworkException(response.StatusCode, "INVALID_RESPONSE", "Сервер вернул пустой профиль.");

            CurrentUser = profile;
            SkinManager.Remember(profile);
            SkinManager.Ensure().ApplyServerBalance(profile.SoftCurrency);
            return profile;
        }

        public async Task<ProfileResponse> UpdateProfileNickname(string nickname)
        {
            var body = "{\"nickname\":" + JsonConvert.ToString(nickname ?? string.Empty) + "}";
            var response = await SendAsync("PATCH", YahtzeeConfig.Current.ProfilePath, body, "application/json", true);
            ThrowIfBanned(response);
            if (!response.IsSuccess)
                throw NetworkException.From(response.StatusCode, response.Body);

            var profile = JsonConvert.DeserializeObject<ProfileResponse>(response.Body, NetworkJson.Settings);
            if (profile == null)
                throw new NetworkException(response.StatusCode, "INVALID_RESPONSE", "Сервер не обновил профиль.");

            CurrentUser = MergeProfile(profile);
            SkinManager.Remember(CurrentUser);
            SkinManager.Ensure().ApplyServerBalance(CurrentUser.SoftCurrency);
            return CurrentUser;
        }

        public async Task<JObject> ExportProfile()
        {
            var response = await SendAsync(
                UnityWebRequest.kHttpVerbGET,
                YahtzeeConfig.Current.ProfilePath + "/export",
                null,
                null,
                true);
            ThrowIfBanned(response);
            if (!response.IsSuccess)
                throw NetworkException.From(response.StatusCode, response.Body);

            return JObject.Parse(response.Body);
        }

        public async Task DeleteAccount()
        {
            var response = await SendAsync(
                UnityWebRequest.kHttpVerbDELETE,
                YahtzeeConfig.Current.ProfilePath,
                "{}",
                "application/json",
                true);
            if (!response.IsSuccess && response.StatusCode != 200 && response.StatusCode != 204)
                throw NetworkException.From(response.StatusCode, response.Body);

            ClearToken();
            CurrentUser = null;
            SetActiveMatchId(null);
            ResumeState = null;
        }

        public async Task<ProfileResponse> BindDevice(string deviceId, string deviceSecret)
        {
            if (!IsDeviceId(deviceId))
                throw new ArgumentException("Invalid device id.", nameof(deviceId));
            if (string.IsNullOrWhiteSpace(deviceSecret) || deviceSecret.Length < 32 || deviceSecret.Length > 128)
                throw new ArgumentException("Invalid device secret.", nameof(deviceSecret));

            var body = new JObject
            {
                ["deviceId"] = deviceId,
                ["deviceSecret"] = deviceSecret
            }.ToString(Formatting.None);
            var response = await SendAsync(
                UnityWebRequest.kHttpVerbPOST,
                YahtzeeConfig.Current.ProfilePath + "/bind-device",
                body,
                "application/json",
                true);
            ThrowIfBanned(response);
            if (!response.IsSuccess)
                throw NetworkException.From(response.StatusCode, response.Body);

            var profile = JsonConvert.DeserializeObject<ProfileResponse>(response.Body, NetworkJson.Settings);
            if (profile == null)
                throw new NetworkException(response.StatusCode, "INVALID_RESPONSE", "Сервер не привязал устройство.");

            CurrentUser = MergeProfile(profile);
            SkinManager.Remember(CurrentUser);
            return CurrentUser;
        }

        public async Task<GameStateResponse> GetMatchState(int matchId)
        {
            if (matchId <= 0)
                throw new ArgumentOutOfRangeException(nameof(matchId));

            var path = YahtzeeConfig.Current.MatchPath(matchId, null);
            var response = await SendAsync(UnityWebRequest.kHttpVerbGET, path, null, null, true);
            ThrowIfBanned(response);
            if (!response.IsSuccess)
                throw NetworkException.From(response.StatusCode, response.Body);

            var state = JsonConvert.DeserializeObject<GameStateResponse>(response.Body, NetworkJson.Settings);
            if (state == null || state.Match == null)
                throw new NetworkException(response.StatusCode, "INVALID_RESPONSE", "Сервер вернул пустое состояние матча.");

            return state;
        }

        public async Task<EconomyCatalogDto> GetEconomyCatalog()
        {
            var response = await SendAsync(
                UnityWebRequest.kHttpVerbGET,
                "/api/economy/catalog",
                null,
                null,
                true);
            ThrowIfBanned(response);
            if (!response.IsSuccess)
                throw NetworkException.From(response.StatusCode, response.Body);

            var root = JObject.Parse(response.Body);
            var catalog = new EconomyCatalogDto
            {
                RewardedAdCoins = root.Value<int?>("rewardedAdCoins") ?? 50,
                Products = root["products"] != null
                    ? root["products"].ToObject<EconomyProductDto[]>(JsonSerializer.Create(NetworkJson.Settings))
                    : Array.Empty<EconomyProductDto>()
            };
            return catalog;
        }

        public async Task RedeemIap(string productId, string transactionId, string platform, string receipt)
        {
            var body = new JObject
            {
                ["productId"] = productId,
                ["transactionId"] = transactionId,
                ["platform"] = MapEconomyPlatform(platform),
                ["receipt"] = receipt ?? string.Empty
            };
            var response = await SendAsync(
                UnityWebRequest.kHttpVerbPOST,
                "/api/economy/iap",
                body.ToString(Formatting.None),
                "application/json",
                true);
            ThrowIfBanned(response);
            if (!response.IsSuccess)
                throw NetworkException.From(response.StatusCode, response.Body);

            ApplyEconomyUser(JObject.Parse(response.Body));
        }

        public async Task RedeemRewardedAd(string impressionId, string signature)
        {
            var body = new JObject
            {
                ["impressionId"] = impressionId,
                ["signature"] = signature
            };
            var response = await SendAsync(
                UnityWebRequest.kHttpVerbPOST,
                "/api/economy/ads/reward",
                body.ToString(Formatting.None),
                "application/json",
                true);
            ThrowIfBanned(response);
            if (!response.IsSuccess)
                throw NetworkException.From(response.StatusCode, response.Body);

            ApplyEconomyUser(JObject.Parse(response.Body));
        }

        private void ApplyEconomyUser(JObject root)
        {
            if (root == null || root["user"] == null)
                return;

            var profile = root["user"].ToObject<ProfileResponse>(JsonSerializer.Create(NetworkJson.Settings));
            if (profile == null)
                return;

            CurrentUser = MergeProfile(profile);
            SkinManager.Remember(CurrentUser);
            SkinManager.Ensure().ApplyServerBalance(CurrentUser.SoftCurrency);
        }

        private static string MapEconomyPlatform(string platform)
        {
            var value = (platform ?? string.Empty).Trim().ToLowerInvariant();
            if (value.Contains("android"))
                return "android";
            if (value.Contains("iphone") || value.Contains("ipad") || value == "ios")
                return "ios";
            if (value.Contains("web") || value.Contains("webgl"))
                return "web";
            return "editor";
        }

        public async Task<ShopCatalogResponse> GetShop()
        {
            var response = await SendAsync(UnityWebRequest.kHttpVerbGET, YahtzeeConfig.Current.ShopPath, null, null, true);
            ThrowIfBanned(response);
            if (!response.IsSuccess)
                throw NetworkException.From(response.StatusCode, response.Body);

            var catalog = JsonConvert.DeserializeObject<ShopCatalogResponse>(response.Body, NetworkJson.Settings);
            if (catalog == null)
                throw new NetworkException(response.StatusCode, "INVALID_RESPONSE", "Сервер вернул пустой магазин.");

            return catalog;
        }

        public async Task<SkinActionResponse> BuySkin(int skinId)
        {
            return await PostSkin(YahtzeeConfig.Current.ShopBuyPath, skinId);
        }

        public async Task<SkinActionResponse> EquipSkin(int skinId)
        {
            return await PostSkin(YahtzeeConfig.Current.SkinEquipPath, skinId);
        }

        private async Task<SkinActionResponse> PostSkin(string path, int skinId)
        {
            var body = "{\"skinId\":" + skinId.ToString(CultureInfo.InvariantCulture) + "}";
            var response = await SendAsync(UnityWebRequest.kHttpVerbPOST, path, body, "application/json", true);
            ThrowIfBanned(response);
            if (!response.IsSuccess)
                throw NetworkException.From(response.StatusCode, response.Body);

            var parsed = JsonConvert.DeserializeObject<SkinActionResponse>(response.Body, NetworkJson.Settings);
            if (parsed == null)
                throw new NetworkException(response.StatusCode, "INVALID_RESPONSE", "Сервер не подтвердил скин.");

            if (parsed.User != null)
            {
                CurrentUser = MergeProfile(parsed.User);
                SkinManager.Remember(CurrentUser);
            }

            return parsed;
        }

        private ProfileResponse MergeProfile(ProfileResponse patch)
        {
            var current = CurrentUser ?? new ProfileResponse();
            current.Id = patch.Id > 0 ? patch.Id : current.Id;
            if (!string.IsNullOrEmpty(patch.Nickname))
                current.Nickname = patch.Nickname;
            current.SoftCurrency = patch.SoftCurrency;
            current.EquippedDiceSkinId = patch.EquippedDiceSkinId;
            current.EquippedTableBoxSkinId = patch.EquippedTableBoxSkinId;
            current.EquippedDiceSkinSlug = patch.EquippedDiceSkinSlug;
            current.EquippedTableBoxSkinSlug = patch.EquippedTableBoxSkinSlug;
            current.IsAdFree = patch.IsAdFree;
            current.IsAdFreeUntil = patch.IsAdFreeUntil ?? current.IsAdFreeUntil;
            current.Mmr = patch.Mmr > 0 ? patch.Mmr : current.Mmr;
            return current;
        }

        public async Task<bool> RestoreSession()
        {
            if (string.IsNullOrEmpty(_token))
                return false;

            try
            {
                await GetProfile();
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (NetworkException exception)
            {
                if (exception.ErrorCode != "USER_BANNED")
                    OnRequestFailed?.Invoke(exception.ErrorCode, exception.Message);
                return false;
            }
        }

        public void ClearResumeState()
        {
            ResumeState = null;
        }

        public void ForgetLocalMatch()
        {
            SetActiveMatchId(null);
            ResumeState = null;
            LastMatchStarted = null;
            // Intentional teardown after MatchOver / leave — not a network drop.
            _connectionLost = false;
            CloseSocketQuietly();
        }

        public async Task<bool> TryReconnect()
        {
            if (string.IsNullOrEmpty(_token))
                return false;

            try
            {
                await GetProfile();
                if (_activeMatchId.HasValue)
                {
                    var matchId = _activeMatchId.Value.ToString(CultureInfo.InvariantCulture);
                    try
                    {
                        await SendReconnectRequest(matchId);
                    }
                    catch (NetworkException exception) when (exception.ErrorCode == "MATCH_NOT_PLAYING"
                        || exception.ErrorCode == "RECONNECT_EXPIRED"
                        || exception.ErrorCode == "NOT_IN_MATCH")
                    {
                        SetActiveMatchId(null);
                        ResumeState = null;
                    }

                    if (ResumeState != null && ResumeState.Match != null && ResumeState.Match.Status == "finished")
                        SetActiveMatchId(null);
                }

                MarkConnectionRestored();
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Yahtzee reconnect: " + exception.Message);
                return false;
            }
        }

        public async Task<ServiceStatusDto> RefreshServiceStatusAsync()
        {
            try
            {
                var response = await SendAsync(
                    UnityWebRequest.kHttpVerbGET,
                    YahtzeeConfig.Current.ServiceStatusPath,
                    null,
                    null,
                    false);

                if (!response.IsSuccess)
                {
                    if (response.StatusCode == 0)
                    {
                        PlayerFeedback.SetServiceStatus(
                            ServiceUiStatus.Offline,
                            null,
                            null);
                        return null;
                    }

                    var failure = NetworkException.From(response.StatusCode, response.Body);
                    PlayerFeedback.FromNetwork(failure);
                    return null;
                }

                var dto = JsonConvert.DeserializeObject<ServiceStatusDto>(response.Body, NetworkJson.Settings)
                    ?? new ServiceStatusDto { Status = "ok" };
                PlayerFeedback.ApplyServiceDto(dto);
                ClientGate.Apply(dto);
                if (string.Equals(dto.Status, "ok", StringComparison.OrdinalIgnoreCase))
                    MarkConnectionRestored();
                return dto;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Yahtzee service status: " + exception.Message);
                PlayerFeedback.SetServiceStatus(ServiceUiStatus.Offline, null, null);
                return null;
            }
        }

        private void MarkConnectionLost()
        {
            if (_connectionLost)
                return;

            _connectionLost = true;
            // Tell the server immediately so the opponent gets OpponentDisconnected / reconnect timer.
            if (_activeMatchId.HasValue && CurrentUser != null && !string.IsNullOrEmpty(_token))
                _ = NotifyServerDisconnected();
            OnConnectionLost?.Invoke();
        }

        private async Task NotifyServerDisconnected()
        {
            try
            {
                if (!_activeMatchId.HasValue || CurrentUser == null)
                    return;

                var path = YahtzeeConfig.Current.MatchPath(
                    _activeMatchId.Value,
                    "players/" + CurrentUser.Id.ToString(CultureInfo.InvariantCulture) + "/disconnect");
                await PostAbsolute(path);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Yahtzee notify disconnect: " + exception.Message);
            }
        }

        private void MarkConnectionRestored()
        {
            var changed = _connectionLost;
            _connectionLost = false;
            if (changed)
                OnConnectionRestored?.Invoke();

            if (PlayerFeedback.ServiceStatus == ServiceUiStatus.Offline
                || PlayerFeedback.ServiceStatus == ServiceUiStatus.Maintenance)
            {
                // Soft clear only after a successful authenticated/status round-trip confirms OK.
            }
        }

        public Task<MatchmakingResponse> JoinQuickMatch()
        {
            return JoinQueue(YahtzeeConfig.Current.QuickMatchPath);
        }

        public Task<MatchmakingResponse> JoinRankedMatch()
        {
            return JoinQueue(YahtzeeConfig.Current.RankedMatchPath);
        }

        public Task<MatchmakingResponse> JoinTournamentQueue()
        {
            return JoinQueue(YahtzeeConfig.Current.TournamentJoinPath);
        }

        private async Task<MatchmakingResponse> JoinQueue(string path)
        {
            var response = await SendAsync(
                UnityWebRequest.kHttpVerbPOST,
                path,
                "{}",
                "application/json",
                true);
            ThrowIfBanned(response);
            if (!response.IsSuccess)
                throw NetworkException.From(response.StatusCode, response.Body);

            var parsed = JsonConvert.DeserializeObject<MatchmakingResponse>(response.Body, NetworkJson.Settings);
            if (parsed == null || string.IsNullOrEmpty(parsed.Status))
                throw new NetworkException(response.StatusCode, "INVALID_RESPONSE", "Сервер вернул пустой ответ поиска матча.");

            if (parsed.Status == "matched" && parsed.Match != null)
            {
                SetActiveMatchId(parsed.Match.Id);
                _searchingQueue = false;
            }
            else if (parsed.Status == "queued")
            {
                _searchingQueue = true;
            }

            LastQuickMatch = parsed;
            OnQuickMatchUpdated?.Invoke(parsed);
            return parsed;
        }

        public async Task LeaveQuickMatch()
        {
            var response = await SendAsync(
                UnityWebRequest.kHttpVerbDELETE,
                YahtzeeConfig.Current.QuickMatchPath,
                null,
                null,
                true);
            ThrowIfBanned(response);
            if (!response.IsSuccess)
                throw NetworkException.From(response.StatusCode, response.Body);

            _searchingQueue = false;
            LastQuickMatch = null;
        }

        public async Task LeaveTournamentQueue()
        {
            var response = await SendAsync(
                UnityWebRequest.kHttpVerbPOST,
                YahtzeeConfig.Current.TournamentLeavePath,
                "{}",
                "application/json",
                true);
            ThrowIfBanned(response);
            if (!response.IsSuccess)
                throw NetworkException.From(response.StatusCode, response.Body);

            _searchingQueue = false;
            LastQuickMatch = null;
        }

        public async Task<MmrLeaderboardDto> GetMmrLeaderboard()
        {
            var response = await SendAsync(
                UnityWebRequest.kHttpVerbGET,
                YahtzeeConfig.Current.MmrLeaderboardPath,
                null,
                null,
                true);
            ThrowIfBanned(response);
            if (!response.IsSuccess)
                throw NetworkException.From(response.StatusCode, response.Body);

            var parsed = JsonConvert.DeserializeObject<MmrLeaderboardDto>(response.Body, NetworkJson.Settings);
            if (parsed == null)
                parsed = new MmrLeaderboardDto();
            if (parsed.Entries == null)
                parsed.Entries = Array.Empty<MmrLeaderboardEntryDto>();

            return parsed;
        }

        public async Task<TournamentLeaderboardDto> GetTournamentLeaderboard()
        {
            var response = await SendAsync(
                UnityWebRequest.kHttpVerbGET,
                YahtzeeConfig.Current.TournamentLeaderboardPath,
                null,
                null,
                true);
            ThrowIfBanned(response);
            if (!response.IsSuccess)
                throw NetworkException.From(response.StatusCode, response.Body);

            var parsed = JsonConvert.DeserializeObject<TournamentLeaderboardDto>(response.Body, NetworkJson.Settings);
            if (parsed == null)
                parsed = new TournamentLeaderboardDto();
            if (parsed.Entries == null)
                parsed.Entries = Array.Empty<TournamentLeaderboardEntryDto>();

            return parsed;
        }

        public async Task<MatchState> CreateLobby(int maxPlayers, int turnDurationSeconds, string password = null, string roomName = null)
        {
            var body = new JObject
            {
                ["maxPlayers"] = maxPlayers,
                ["turnDurationSeconds"] = turnDurationSeconds
            };
            if (!string.IsNullOrWhiteSpace(roomName))
                body["roomName"] = roomName.Trim();
            if (!string.IsNullOrWhiteSpace(password))
                body["password"] = password.Trim();

            var response = await SendAsync(
                UnityWebRequest.kHttpVerbPOST,
                "/api/lobbies",
                body.ToString(Formatting.None),
                "application/json",
                true);
            ThrowIfBanned(response);
            // 201 Created is success for lobby create.
            if (!response.IsSuccess && response.StatusCode != 201)
                throw NetworkException.From(response.StatusCode, response.Body);

            var match = ReadLobby(response.Body, response.StatusCode);
            LastLobby = match;
            return match;
        }

        public async Task<LobbyListResponseDto> GetLobbies(string search, bool hideFull, bool hidePassword, int page, int perPage)
        {
            if (page < 1)
                page = 1;
            if (perPage < 1)
                perPage = 20;

            var path = "/api/lobbies?hideFull=" + (hideFull ? "true" : "false")
                + "&hidePassword=" + (hidePassword ? "true" : "false")
                + "&page=" + page.ToString(CultureInfo.InvariantCulture)
                + "&perPage=" + perPage.ToString(CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(search))
                path += "&search=" + UnityWebRequest.EscapeURL(search.Trim());

            var response = await SendAsync(UnityWebRequest.kHttpVerbGET, path, null, null, true);
            ThrowIfBanned(response);
            if (!response.IsSuccess)
                throw NetworkException.From(response.StatusCode, response.Body);

            var parsed = JsonConvert.DeserializeObject<LobbyListResponseDto>(response.Body, NetworkJson.Settings);
            if (parsed == null)
                parsed = new LobbyListResponseDto();
            if (parsed.Lobbies == null)
                parsed.Lobbies = Array.Empty<LobbyRoomDto>();

            return parsed;
        }

        public async Task<bool> JoinLobbyWithPassword(string matchId, string password)
        {
            if (!int.TryParse(matchId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) || id <= 0)
                throw new ArgumentOutOfRangeException(nameof(matchId), "Идентификатор комнаты должен быть положительным числом.");

            var body = new JObject
            {
                ["matchId"] = id
            };
            if (!string.IsNullOrWhiteSpace(password))
                body["password"] = password;

            var response = await SendAsync(
                UnityWebRequest.kHttpVerbPOST,
                "/api/lobbies/join",
                body.ToString(Formatting.None),
                "application/json",
                true);
            ThrowIfBanned(response);
            if (response.IsSuccess)
            {
                LastLobby = ReadLobby(response.Body, response.StatusCode);
                return true;
            }

            var failure = NetworkException.From(response.StatusCode, response.Body);
            if (response.StatusCode == 403 && (failure.ErrorCode == "INVALID_PASSWORD" || failure.ErrorCode == "INVALID_LOBBY_PASSWORD"))
            {
                OnLobbyJoinFailed?.Invoke(string.IsNullOrEmpty(failure.Message) ? "Неверный пароль от комнаты" : failure.Message);
                return false;
            }

            throw failure;
        }

        public async Task LeaveLobby(string matchId)
        {
            if (!int.TryParse(matchId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) || id <= 0)
                return;

            var response = await SendAsync(
                UnityWebRequest.kHttpVerbPOST,
                "/api/lobbies/" + id.ToString(CultureInfo.InvariantCulture) + "/leave",
                "{}",
                "application/json",
                true);
            ThrowIfBanned(response);
            if (!response.IsSuccess)
                throw NetworkException.From(response.StatusCode, response.Body);

            if (LastLobby != null && LastLobby.Id == id)
                LastLobby = null;
        }

        public async Task<GameStateResponse> StartLobbyMatch(string matchId)
        {
            if (!int.TryParse(matchId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) || id <= 0)
                throw new ArgumentOutOfRangeException(nameof(matchId), "Идентификатор комнаты должен быть положительным числом.");

            var response = await SendAsync(
                UnityWebRequest.kHttpVerbPOST,
                "/api/matches/" + id.ToString(CultureInfo.InvariantCulture) + "/start",
                "{}",
                "application/json",
                true);
            ThrowIfBanned(response);
            if (!response.IsSuccess)
                throw NetworkException.From(response.StatusCode, response.Body);

            var state = JsonConvert.DeserializeObject<GameStateResponse>(response.Body, NetworkJson.Settings);
            if (state == null || state.Match == null || state.Match.Id <= 0)
                throw new NetworkException(response.StatusCode, "INVALID_RESPONSE", "Сервер не начал матч.");

            SetActiveMatchId(state.Match.Id);
            ResumeState = state;
            LastLobby = state.Match;
            return state;
        }

        public async Task WatchLobby(string matchId)
        {
            await ConnectToMatchWebSocket(matchId);
            // Lobby watch only — don't pin a resumable playing match.
            SetActiveMatchId(null);
        }

        private static MatchState ReadLobby(string body, long statusCode)
        {
            var match = JsonConvert.DeserializeObject<MatchState>(body, NetworkJson.Settings);
            if (match == null || match.Id <= 0)
                throw new NetworkException(statusCode, "INVALID_RESPONSE", "Сервер не вернул лобби.");

            return match;
        }

        public async Task<GameStateResponse> ResignMatch()
        {
            var state = await PostMatchAction("resign", "{}");
            SetActiveMatchId(null);
            ResumeState = null;
            LastMatchStarted = null;
            _connectionLost = false;
            CloseSocketQuietly();
            return state;
        }

        public async Task<GameStateResponse> DisconnectSelf()
        {
            if (!_activeMatchId.HasValue || CurrentUser == null)
                throw new InvalidOperationException("Нет активного матча.");

            var path = YahtzeeConfig.Current.MatchPath(
                _activeMatchId.Value,
                "players/" + CurrentUser.Id.ToString(CultureInfo.InvariantCulture) + "/disconnect");
            return await PostAbsolute(path);
        }

        public async Task<GameStateResponse> ReconnectMatch()
        {
            return await PostMatchAction("reconnect", "{}");
        }

        public async Task<MatchReconnectResponseDto> SendReconnectRequest(string matchId)
        {
            if (!int.TryParse(matchId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) || id <= 0)
                throw new ArgumentOutOfRangeException(nameof(matchId), "Идентификатор матча должен быть положительным числом.");

            SetActiveMatchId(id);
            GameStateResponse state;
            try
            {
                state = await ReconnectMatch();
            }
            catch (NetworkException exception) when (exception.ErrorCode == "NOT_DISCONNECTED")
            {
                state = await ExpireTurn();
            }

            if (state == null || state.Match == null)
                throw new InvalidOperationException("Сервер вернул пустое состояние матча.");

            if (state.Match.Status == "finished" || state.Match.Status == "cancelled")
            {
                SetActiveMatchId(null);
                ResumeState = null;
                return MatchReconnectResponseDto.From(state);
            }

            ResumeState = state;
            return MatchReconnectResponseDto.From(state);
        }

        public async Task<GameStateResponse> ClaimVictory()
        {
            return await PostMatchAction("claim-victory", "{}");
        }

        public async Task<GameStateResponse> PlayWithBot()
        {
            return await PostMatchAction("play-with-bot", "{}");
        }

        public async Task<GameStateResponse> ExpireTurn()
        {
            return await PostMatchAction("expire-turn", "{}");
        }

        private async Task<GameStateResponse> PostAbsolute(string path)
        {
            var response = await SendAsync(
                UnityWebRequest.kHttpVerbPOST,
                path,
                "{}",
                "application/json",
                true);
            ThrowIfBanned(response);
            if (!response.IsSuccess)
                throw NetworkException.From(response.StatusCode, response.Body);

            var state = JsonConvert.DeserializeObject<GameStateResponse>(response.Body, NetworkJson.Settings);
            if (state == null || state.Match == null)
                throw new NetworkException(response.StatusCode, "INVALID_RESPONSE", "Сервер вернул пустое состояние матча.");

            return state;
        }

        public async Task<GameStateResponse> RollDice(int[] reroll)
        {
            var indexes = new JArray();
            if (reroll != null)
            {
                for (var index = 0; index < reroll.Length; index++)
                    indexes.Add(reroll[index]);
            }

            var body = new JObject { ["reroll"] = indexes };
            return await PostMatchAction("roll", body.ToString(Formatting.None));
        }

        public async Task HoldDice(bool[] held)
        {
            var mask = new JArray();
            for (var index = 0; index < DiceGameEngine.DiceCount; index++)
                mask.Add(held != null && index < held.Length && held[index]);

            var body = new JObject { ["held"] = mask };
            await PostMatchAction("hold", body.ToString(Formatting.None));
        }

        public async Task<GameStateResponse> SelectCategory(string category, int score)
        {
            if (string.IsNullOrEmpty(category))
                throw new ArgumentException("Категория обязательна.", nameof(category));

            var body = new JObject
            {
                ["category"] = category,
                ["score"] = score
            };
            return await PostMatchAction("score", body.ToString(Formatting.None));
        }

        public async Task ConnectToMatchWebSocket(string matchId)
        {
            if (!int.TryParse(matchId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) || id <= 0)
                throw new ArgumentOutOfRangeException(nameof(matchId), "Идентификатор матча должен быть положительным числом.");

            await _socketGate.WaitAsync(_lifetime.Token);
            try
            {
                await ConnectCore(id);
            }
            finally
            {
                _socketGate.Release();
            }
        }

        private async Task<GameStateResponse> PostMatchAction(string action, string json)
        {
            if (!_activeMatchId.HasValue)
                throw new InvalidOperationException("Нет активного матча.");

            var path = YahtzeeConfig.Current.MatchPath(_activeMatchId.Value, action);
            var response = await SendAsync(
                UnityWebRequest.kHttpVerbPOST,
                path,
                json,
                "application/json",
                true);
            ThrowIfBanned(response);
            if (!response.IsSuccess)
                throw NetworkException.From(response.StatusCode, response.Body);

            var state = JsonConvert.DeserializeObject<GameStateResponse>(response.Body, NetworkJson.Settings);
            if (state == null || state.Match == null)
                throw new NetworkException(response.StatusCode, "INVALID_RESPONSE", "Сервер вернул пустое состояние матча.");

            return state;
        }

        private async Task ConnectCore(int matchId)
        {
            _nextPingUnscaled = float.PositiveInfinity;
            CloseSocketQuietly();
            var generation = _socketGeneration;

            if (string.IsNullOrWhiteSpace(YahtzeeConfig.Current.ReverbAppKey))
                throw new InvalidOperationException("Укажите публичный Reverb App Key в YahtzeeConfig.");

            _expectedChannel = "presence-match." + matchId.ToString(CultureInfo.InvariantCulture);
            _socketIdTcs = new TaskCompletionSource<string>();
            _subscribeTcs = new TaskCompletionSource<bool>();

            var socket = new WebSocket(BuildReverbUrl());
            websocket = socket;
            socket.OnMessage += bytes => HandleMessage(generation, bytes);
            socket.OnError += error => HandleError(generation, error);
            socket.OnClose += _ => HandleClose(generation);
            if (_socketPump != null && _socketPump.IsFaulted && _socketPump.Exception != null)
                Debug.LogWarning("Yahtzee socket: " + _socketPump.Exception.GetBaseException().Message);
            _socketPump = socket.Connect();

            var socketId = await WaitForSocketId(generation);
            var auth = await RequestBroadcastAuth(socketId, _expectedChannel);
            if (generation != _socketGeneration)
                throw new OperationCanceledException();

            var data = new JObject
            {
                ["auth"] = auth.Auth,
                ["channel"] = _expectedChannel
            };
            if (!string.IsNullOrEmpty(auth.ChannelData))
                data["channel_data"] = auth.ChannelData;

            var subscribe = new JObject
            {
                ["event"] = "pusher:subscribe",
                ["data"] = data
            };
            await socket.SendText(subscribe.ToString(Formatting.None));
            await WaitForSubscription(generation);
            SetActiveMatchId(matchId);
            _socketReady = true;
        }

        private async Task<BroadcastAuthResponse> RequestBroadcastAuth(string socketId, string channel)
        {
            var body = "socket_id=" + Uri.EscapeDataString(socketId)
                + "&channel_name=" + Uri.EscapeDataString(channel);
            var response = await SendAsync(
                UnityWebRequest.kHttpVerbPOST,
                YahtzeeConfig.Current.BroadcastAuthPath,
                body,
                "application/x-www-form-urlencoded",
                true);
            ThrowIfBanned(response);
            if (!response.IsSuccess)
                throw NetworkException.From(response.StatusCode, response.Body);

            var auth = JsonConvert.DeserializeObject<BroadcastAuthResponse>(response.Body, NetworkJson.Settings);
            if (auth == null || string.IsNullOrEmpty(auth.Auth))
                throw new NetworkException(response.StatusCode, "BROADCAST_AUTH", "Reverb не подтвердил канал матча.");

            return auth;
        }

        private async Task CatchUp(int matchId)
        {
            var path = YahtzeeConfig.Current.MatchPath(matchId, "reconnect");
            var response = await SendAsync(
                UnityWebRequest.kHttpVerbPOST,
                path,
                "{}",
                "application/json",
                true);

            if (response.StatusCode == 403 && IsBanned(response.Body))
            {
                PublishBan(response.Body);
                return;
            }

            if (response.IsSuccess)
            {
                var state = JsonConvert.DeserializeObject<GameStateResponse>(response.Body, NetworkJson.Settings);
                if (state != null)
                {
                    ResumeState = state;
                    if (state.Match != null && (state.Match.Status == "finished" || state.Match.Status == "cancelled"))
                        SetActiveMatchId(null);
                    OnMatchStateCaughtUp?.Invoke(state);
                }
                return;
            }

            if (TryReadError(response.Body, out var error))
            {
                if (error.Error == "NOT_DISCONNECTED")
                {
                    ResumeState = await ExpireTurn();
                    if (ResumeState != null && ResumeState.Match != null && (ResumeState.Match.Status == "finished" || ResumeState.Match.Status == "cancelled"))
                        SetActiveMatchId(null);
                    if (ResumeState != null)
                        OnMatchStateCaughtUp?.Invoke(ResumeState);
                    return;
                }

                if (error.Error == "RECONNECT_EXPIRED")
                {
                    OnRequestFailed?.Invoke(error.Error, ReadReason(response.Body, "Время на возвращение в матч истекло."));
                    return;
                }
            }

            throw NetworkException.From(response.StatusCode, response.Body);
        }

        private void ScheduleReconnect()
        {
            if (_reconnectRunning || _lifetime.IsCancellationRequested || !_activeMatchId.HasValue)
                return;

            _reconnectRunning = true;
            _ = RunReconnect();
        }

        private async Task RunReconnect()
        {
            var delaySeconds = YahtzeeConfig.Current.ReconnectStartDelaySeconds;
            try
            {
                while (!_lifetime.IsCancellationRequested && _activeMatchId.HasValue)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(delaySeconds), _lifetime.Token);
                        if (string.IsNullOrEmpty(_token) || !_activeMatchId.HasValue)
                            return;

                        await _socketGate.WaitAsync(_lifetime.Token);
                        try
                        {
                            if (_socketReady || !_activeMatchId.HasValue)
                                return;

                            var matchId = _activeMatchId.Value;
                            await ConnectCore(matchId);
                            await CatchUp(matchId);
                            MarkConnectionRestored();
                            return;
                        }
                        finally
                        {
                            _socketGate.Release();
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (InvalidOperationException)
                    {
                        return;
                    }
                    catch (NetworkException exception) when (exception.StatusCode == 401 || exception.ErrorCode == "USER_BANNED")
                    {
                        return;
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning("Yahtzee reconnect: " + exception.Message);
                        delaySeconds = Math.Min(delaySeconds * 2, YahtzeeConfig.Current.ReconnectMaxDelaySeconds);
                    }
                }
            }
            finally
            {
                _reconnectRunning = false;
            }
        }

        private void HandleMessage(int generation, byte[] bytes)
        {
            if (generation != _socketGeneration || bytes == null || bytes.Length == 0)
                return;

            JObject frame;
            try
            {
                frame = JObject.Parse(Encoding.UTF8.GetString(bytes));
            }
            catch (JsonException exception)
            {
                Debug.LogWarning("Yahtzee socket JSON: " + exception.Message);
                return;
            }

            var eventName = frame.Value<string>("event") ?? string.Empty;
            var channel = frame.Value<string>("channel") ?? string.Empty;
            var dataJson = ReadDataJson(frame["data"]);

            if (eventName.StartsWith("pusher:", StringComparison.Ordinal)
                || eventName.StartsWith("pusher_internal:", StringComparison.Ordinal))
            {
                HandleProtocol(eventName, channel, dataJson);
                return;
            }

            PublishGameEvent(eventName, channel, dataJson);
        }

        private void HandleProtocol(string eventName, string channel, string dataJson)
        {
            switch (eventName)
            {
                case "pusher:connection_established":
                    var established = JsonConvert.DeserializeObject<PusherConnectionData>(dataJson, NetworkJson.Settings);
                    if (established != null && established.ActivityTimeout > 0)
                        _pingIntervalSeconds = Mathf.Max(YahtzeeConfig.Current.MinPingIntervalSeconds, established.ActivityTimeout / 2f);
                    _nextPingUnscaled = Time.unscaledTime + _pingIntervalSeconds;
                    _socketIdTcs?.TrySetResult(established != null ? established.SocketId : null);
                    break;

                case "pusher_internal:subscription_succeeded":
                    if (channel == _expectedChannel)
                        _subscribeTcs?.TrySetResult(true);
                    break;

                case "pusher:error":
                    var failure = JsonConvert.DeserializeObject<PusherErrorData>(dataJson, NetworkJson.Settings);
                    var message = failure != null && !string.IsNullOrEmpty(failure.Message)
                        ? failure.Message
                        : "Ошибка Reverb.";
                    var exception = new NetworkException(0, "REVERB", message);
                    _socketIdTcs?.TrySetException(exception);
                    _subscribeTcs?.TrySetException(exception);
                    break;
            }
        }

        private void PublishGameEvent(string eventName, string channel, string dataJson)
        {
            var name = NormalizeEventName(eventName);
            try
            {
                switch (name)
                {
                    case "DiceRolled":
                        var rolled = Deserialize<DiceRolledEvent>(dataJson, eventName, channel);
                        OnDiceRolled?.Invoke(rolled.Dice ?? Array.Empty<int>());
                        OnDiceRolledEvent?.Invoke(rolled);
                        break;
                    case "DiceHeld":
                        OnDiceHeld?.Invoke(Deserialize<DiceHeldEvent>(dataJson, eventName, channel));
                        break;
                    case "MatchStarted":
                        LastMatchStarted = Deserialize<MatchStartedEvent>(dataJson, eventName, channel);
                        OnMatchStarted?.Invoke(LastMatchStarted);
                        break;
                    case "TurnChanged":
                        OnTurnChanged?.Invoke(Deserialize<TurnChangedEvent>(dataJson, eventName, channel));
                        break;
                    case "MatchOver":
                        OnMatchOver?.Invoke(Deserialize<MatchOverEvent>(dataJson, eventName, channel));
                        break;
                    case "OpponentDisconnected":
                        OnOpponentDisconnected?.Invoke(Deserialize<OpponentDisconnectedEvent>(dataJson, eventName, channel));
                        break;
                    case "PlayerLeft":
                        OnPlayerLeft?.Invoke(Deserialize<PlayerLeftEvent>(dataJson, eventName, channel));
                        break;
                    case "TurnTimedOut":
                        OnTurnTimedOut?.Invoke(Deserialize<TurnTimedOutEvent>(dataJson, eventName, channel));
                        break;
                    case "PlayerReconnected":
                        OnPlayerReconnected?.Invoke(Deserialize<PlayerReconnectedEvent>(dataJson, eventName, channel));
                        break;
                    case "OpponentAbandonChoice":
                        OnOpponentAbandonChoice?.Invoke(Deserialize<OpponentAbandonChoiceEvent>(dataJson, eventName, channel));
                        break;
                    case "PlayerReplacedByBot":
                        OnPlayerReplacedByBot?.Invoke(Deserialize<PlayerReplacedByBotEvent>(dataJson, eventName, channel));
                        break;
                    default:
                        Debug.Log("Yahtzee socket event: " + eventName);
                        break;
                }
            }
            catch (JsonException exception)
            {
                Debug.LogWarning("Yahtzee event " + eventName + ": " + exception.Message);
            }
        }

        private static T Deserialize<T>(string json, string eventName, string channel) where T : ReverbEvent, new()
        {
            var payload = string.IsNullOrEmpty(json)
                ? new T()
                : JsonConvert.DeserializeObject<T>(json, NetworkJson.Settings) ?? new T();
            payload.EventName = eventName;
            payload.Channel = channel;
            return payload;
        }

        private void HandleError(int generation, string error)
        {
            if (generation != _socketGeneration)
                return;

            Debug.LogWarning("Yahtzee socket error: " + error);
            if (!_socketReady)
                FailSocketWait(string.IsNullOrEmpty(error) ? "Ошибка Reverb." : error);
        }

        private void HandleClose(int generation)
        {
            if (generation != _socketGeneration)
                return;

            websocket = null;
            if (!_socketReady)
            {
                FailSocketWait("Соединение с Reverb закрылось.");
                return;
            }

            _socketReady = false;
            MarkConnectionLost();
            ScheduleReconnect();
        }

        private void FailSocketWait(string message)
        {
            var exception = new NetworkException(0, "REVERB", message);
            _socketIdTcs?.TrySetException(exception);
            _subscribeTcs?.TrySetException(exception);
        }

        private async Task<string> WaitForSocketId(int generation)
        {
            var pending = _socketIdTcs.Task;
            var timeout = Task.Delay(TimeSpan.FromSeconds(YahtzeeConfig.Current.SocketTimeoutSeconds), _lifetime.Token);
            var finished = await Task.WhenAny(pending, timeout);
            if (generation != _socketGeneration)
                throw new OperationCanceledException();
            if (finished != pending)
                throw new NetworkException(0, "REVERB_TIMEOUT", "Reverb не прислал socket id.");

            var socketId = await pending;
            if (string.IsNullOrEmpty(socketId))
                throw new NetworkException(0, "REVERB", "Пустой socket id.");

            return socketId;
        }

        private async Task WaitForSubscription(int generation)
        {
            var pending = _subscribeTcs.Task;
            var timeout = Task.Delay(TimeSpan.FromSeconds(YahtzeeConfig.Current.SocketTimeoutSeconds), _lifetime.Token);
            var finished = await Task.WhenAny(pending, timeout);
            if (generation != _socketGeneration)
                throw new OperationCanceledException();
            if (finished != pending)
                throw new NetworkException(0, "REVERB_TIMEOUT", "Reverb не подтвердил подписку на матч.");

            await pending;
        }

        private async Task<HttpResponse> SendAsync(
            string method,
            string path,
            string body,
            string contentType,
            bool authorize)
        {
            await YieldToMainThread();
            _lifetime.Token.ThrowIfCancellationRequested();

            if (authorize && string.IsNullOrEmpty(_token))
                throw new InvalidOperationException("Нет токена Sanctum. Сначала вызовите LoginAsGuest.");

            using (var request = new UnityWebRequest(_apiBaseUrl + path, method))
            {
                if (method != UnityWebRequest.kHttpVerbGET && method != UnityWebRequest.kHttpVerbHEAD)
                {
                    var payload = body ?? string.Empty;
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(payload));
                    if (!string.IsNullOrEmpty(contentType) && payload.Length > 0)
                        request.SetRequestHeader("Content-Type", contentType);
                }

                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Accept", "application/json");
                request.SetRequestHeader("X-Client-Version", ClientGate.ClientVersion);
                if (authorize)
                    request.SetRequestHeader("Authorization", "Bearer " + _token);
                request.timeout = YahtzeeConfig.Current.HttpTimeoutSeconds;

                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    if (_lifetime.IsCancellationRequested)
                    {
                        request.Abort();
                        _lifetime.Token.ThrowIfCancellationRequested();
                    }

                    await Task.Yield();
                }

                var responseBody = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
                if (authorize && request.result == UnityWebRequest.Result.ConnectionError)
                    MarkConnectionLost();
                if (authorize && request.responseCode == 401)
                    ClearToken();

                return new HttpResponse(request.responseCode, responseBody, request.result == UnityWebRequest.Result.Success);
            }
        }

        private Task YieldToMainThread()
        {
            if (_main == null || SynchronizationContext.Current == _main)
                return Task.CompletedTask;

            var done = new TaskCompletionSource<bool>();
            _main.Post(_ => done.TrySetResult(true), null);
            return done.Task;
        }

        private void CloseSocketQuietly()
        {
            // Bump generation first so the old socket's OnClose never calls MarkConnectionLost.
            _socketGeneration++;
            _socketReady = false;
            var socket = websocket;
            websocket = null;
            if (socket == null)
                return;

            try
            {
                if (socket.State == WebSocketState.Open || socket.State == WebSocketState.Connecting)
                    _ = socket.Close();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Yahtzee socket close: " + exception.Message);
            }
        }

        private string BuildReverbUrl()
        {
            var config = YahtzeeConfig.Current;
            var scheme = config.ReverbUseTls ? "wss" : "ws";
            var authority = config.ReverbHost;
            if (config.ReverbPort > 0)
                authority += ":" + config.ReverbPort.ToString(CultureInfo.InvariantCulture);

            return scheme + "://" + authority
                + "/app/" + Uri.EscapeDataString(config.ReverbAppKey)
                + "?protocol=7&client=unity&version=1.0&flash=false";
        }

        private void ClearToken()
        {
            _token = string.Empty;
            CurrentUser = null;
            PlayerPrefs.DeleteKey(YahtzeeConfig.Current.TokenPrefKey);
            PlayerPrefs.Save();
        }

        private static void ThrowIfBanned(HttpResponse response)
        {
            if (response.StatusCode != 403 || !IsBanned(response.Body))
                return;

            var reason = ReadReason(response.Body, "You are banned.");
            PublishBan(response.Body);
            var banned = new NetworkException(403, ApiErrorCode.UserBanned, reason, "auth", false, null);
            PlayerFeedback.FromNetwork(banned);
            throw banned;
        }

        private static void PublishBan(string body)
        {
            LastBanReason = ReadReason(body, "Вы заблокированы.");
            LastBanUntil = ReadBanUntil(body);
            OnUserBanned?.Invoke(LastBanReason);
        }

        private static string ReadBanUntil(string body)
        {
            if (!TryReadError(body, out var error))
                return string.Empty;

            if (!string.IsNullOrEmpty(error.BannedUntil))
                return error.BannedUntil;

            return error.ExpiresAt ?? string.Empty;
        }

        private static bool IsBanned(string body)
        {
            return TryReadError(body, out var error) && error.Error == "USER_BANNED";
        }

        private static bool TryReadError(string body, out ApiErrorBody error)
        {
            error = null;
            if (string.IsNullOrEmpty(body))
                return false;

            try
            {
                error = JsonConvert.DeserializeObject<ApiErrorBody>(body, NetworkJson.Settings);
                return error != null && !string.IsNullOrEmpty(error.Error);
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static string ReadReason(string body, string fallback)
        {
            if (TryReadError(body, out var error) && !string.IsNullOrEmpty(error.Message))
                return error.Message;

            return fallback;
        }

        private static string ReadDataJson(JToken data)
        {
            if (data == null || data.Type == JTokenType.Null)
                return "{}";

            if (data.Type == JTokenType.String)
                return data.Value<string>() ?? "{}";

            return data.ToString(Formatting.None);
        }

        private static string NormalizeEventName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return string.Empty;

            var slash = name.LastIndexOf('\\');
            if (slash >= 0 && slash < name.Length - 1)
                name = name.Substring(slash + 1);

            var dot = name.LastIndexOf('.');
            if (dot >= 0 && dot < name.Length - 1)
                name = name.Substring(dot + 1);

            if (name.Length > 0 && name[0] == '.')
                name = name.Substring(1);

            if (name.EndsWith("Event", StringComparison.Ordinal))
                name = name.Substring(0, name.Length - "Event".Length);

            return name;
        }

        private static bool IsPlatform(string platform)
        {
            return platform == "android" || platform == "ios" || platform == "webgl" || platform == "pc";
        }

        private static bool IsDeviceId(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length < 8 || value.Length > 128)
                return false;

            for (var i = 0; i < value.Length; i++)
            {
                var character = value[i];
                var allowed = (character >= 'A' && character <= 'Z')
                    || (character >= 'a' && character <= 'z')
                    || (character >= '0' && character <= '9')
                    || character == '.'
                    || character == '_'
                    || character == ':'
                    || character == '-';
                if (!allowed)
                    return false;
            }

            return true;
        }

        private readonly struct HttpResponse
        {
            public readonly long StatusCode;
            public readonly string Body;
            public readonly bool IsSuccess;

            public HttpResponse(long statusCode, string body, bool isSuccess)
            {
                StatusCode = statusCode;
                Body = body ?? string.Empty;
                IsSuccess = isSuccess;
            }
        }
    }
}
