using System;
using System.Globalization;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Yahtzee.Core;
using Yahtzee.Feedback;
using Yahtzee.Localization;
using Yahtzee.Network;

namespace Yahtzee.UI
{
    public sealed class MainMenuController : MonoBehaviour
    {
        private const string DeviceIdKey = "yahtzee.device.id";
        private const string DeviceSecretKey = "yahtzee.device.secret";
        private const string LocalNicknameKey = "yahtzee.local.nickname";
        private const string LocalCoinsKey = SkinManager.CoinsPrefKey;
        private const string LocalMmrKey = "yahtzee.local.mmr";

        [SerializeField] private TextMeshProUGUI nicknameText;
        [SerializeField] private TextMeshProUGUI coinBalanceText;
        [SerializeField] private TextMeshProUGUI mmrText;

        private MainMenuScreen _menu;
        private TextMeshProUGUI _offlineNotice;
        private RectTransform _profileCard;
        private Image _coinIcon;
        private Image _ratingIcon;
        private GameObject _connectionPanel;
        private Text _connectionLabel;
        private Button _reconnectButton;
        private bool _subscribed;
        private bool _banned;
        private bool _reconnectBusy;
        private bool _manualReconnect;

        private void Awake()
        {
            _menu = GetComponent<MainMenuScreen>();
            EnsureProfileLabels();
            ShowStoredProfile();
        }

        private void OnEnable()
        {
            if (!_subscribed)
            {
                NetworkManager.OnUserBanned += HandleUserBanned;
                NetworkManager.OnConnectionLost += HandleConnectionLost;
                NetworkManager.OnConnectionRestored += HandleConnectionRestored;
                NetworkManager.OnMatchOver += HandleMatchOverUnlock;
                SkinManager.CurrencyChanged += HandleSkinCurrency;
                _subscribed = true;
            }

            SyncBalanceFromNetwork();
            RefreshOnlinePlayLocks();
            if (NetworkManager.Instance != null && NetworkManager.Instance.ConnectionDropped)
                ShowConnectionPanel();
            else if (NetworkManager.Instance != null && NetworkManager.Instance.ActiveMatchId.HasValue)
                ResumeOnlineMatch();
        }

        private void OnDisable()
        {
            if (!_subscribed)
                return;

            NetworkManager.OnUserBanned -= HandleUserBanned;
            NetworkManager.OnConnectionLost -= HandleConnectionLost;
            NetworkManager.OnConnectionRestored -= HandleConnectionRestored;
            NetworkManager.OnMatchOver -= HandleMatchOverUnlock;
            SkinManager.CurrencyChanged -= HandleSkinCurrency;
            _subscribed = false;
        }

        private void HandleMatchOverUnlock(MatchOverEvent _)
        {
            if (_banned || this == null)
                return;

            RefreshOnlinePlayLocks();
        }

        private async void Start()
        {
            if (_menu != null)
                _menu.SetOnlinePlayAvailable(false);

            try
            {
                await Bootstrap();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Yahtzee menu: " + exception.Message);
                if (!_banned)
                    EnterOffline();
            }
            finally
            {
                if (!_banned && this != null)
                    TryLaunchFirstRunTutorial();
            }
        }

        private static void TryLaunchFirstRunTutorial()
        {
            if (TutorialCampaign.HasSeen || UIManager.Instance == null)
                return;

            GameplayScreen.PrepareTutorial();
            if (NetworkManager.Instance != null)
                NetworkManager.Instance.ForgetLocalMatch();
            UIManager.Instance.SwitchToScreen(ScreenType.Gameplay);
        }

        private async Task Bootstrap()
        {
            var network = ResolveNetwork();
            if (network == null)
            {
                EnterOffline();
                return;
            }

            try
            {
                await ClientGate.RefreshAsync();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Yahtzee client gate: " + exception.Message);
            }

            if (PlayerFeedback.ServiceStatus == ServiceUiStatus.VersionBlocked)
            {
                EnterOffline();
                return;
            }

            var token = PlayerPrefs.GetString(YahtzeeConfig.Current.TokenPrefKey, string.Empty);
            if (string.IsNullOrEmpty(token))
            {
                await LoginGuest(network);
                return;
            }

            try
            {
                var profile = await network.GetProfile();
                if (_banned || this == null)
                    return;

                ApplyProfile(profile);
                await OfferReconnectIfNeeded(network);
                RefreshOnlinePlayLocks();
            }
            catch (NetworkException exception)
            {
                if (_banned || exception.ErrorCode == "USER_BANNED")
                {
                    HandleUserBanned(string.IsNullOrEmpty(exception.Message)
                        ? NetworkManager.LastBanReason
                        : exception.Message);
                    return;
                }

                if (exception.StatusCode == 401)
                {
                    await LoginGuest(network);
                    return;
                }

                EnterOffline();
            }
        }

        private async Task OfferReconnectIfNeeded(NetworkManager network)
        {
            if (network == null || _banned || this == null)
                return;

            var pending = await network.TryRestorePersistedMatch();
            if (_banned || this == null)
                return;

            RefreshOnlinePlayLocks();
            if (pending || network.ConnectionDropped)
                ShowConnectionPanel();
        }

        private async Task LoginGuest(NetworkManager network)
        {
            var nickname = "Guest_" + UnityEngine.Random.Range(1000, 9999).ToString(CultureInfo.InvariantCulture);
            PlayerPrefs.SetString(LocalNicknameKey, nickname);
            PlayerPrefs.Save();
            ShowStoredProfile();

            var signedIn = await network.LoginAsGuest(
                ResolveDeviceId(),
                nickname,
                CurrentPlatform(),
                ResolveDeviceSecret());
            if (_banned || this == null)
                return;

            if (!signedIn)
            {
                EnterOffline();
                return;
            }

            if (network.CurrentUser != null)
                ApplyProfile(network.CurrentUser);
            else
                ApplyLocalNickname(nickname);

            RefreshOnlinePlayLocks();
        }

        private void HandleUserBanned(string reason)
        {
            _banned = true;
            HideOfflineNotice();
            if (_menu == null)
                _menu = GetComponent<MainMenuScreen>();
            if (_menu == null)
                _menu = FindAnyObjectByType<MainMenuScreen>(FindObjectsInactive.Include);
            if (_menu != null)
                _menu.SetMenuLocked(true);

            var overlay = FindAnyObjectByType<BannedOverlayScreen>(FindObjectsInactive.Include);
            if (overlay != null)
                overlay.SetBan(
                    string.IsNullOrEmpty(reason) ? NetworkManager.LastBanReason : reason,
                    NetworkManager.LastBanUntil);

            if (UIManager.Instance != null)
                UIManager.Instance.SwitchToScreen(ScreenType.BannedOverlay);
        }

        private void EnterOffline()
        {
            if (_banned || this == null)
                return;

            ShowStoredProfile();
            if (_menu != null)
                _menu.SetOnlinePlayAvailable(false);

            EnsureOfflineNotice();
            PlaceOfflineNotice();
            if (NetworkManager.Instance != null && NetworkManager.Instance.ConnectionDropped)
            {
                ShowConnectionPanel();
                return;
            }

            if (_offlineNotice != null)
                _offlineNotice.gameObject.SetActive(true);
        }

        private void ApplyProfile(ProfileResponse profile)
        {
            if (profile == null)
            {
                EnterOffline();
                return;
            }

            var nickname = string.IsNullOrWhiteSpace(profile.Nickname)
                ? PlayerPrefs.GetString(LocalNicknameKey, "Гость")
                : profile.Nickname.Trim();
            var coins = profile.SoftCurrency.ToString(CultureInfo.InvariantCulture);
            var mmr = profile.Mmr.ToString(CultureInfo.InvariantCulture);

            PlayerPrefs.SetString(LocalNicknameKey, nickname);
            PlayerPrefs.SetString(LocalCoinsKey, coins);
            PlayerPrefs.SetString(LocalMmrKey, mmr);
            PlayerPrefs.Save();

            WriteProfile(nickname, coins, mmr);
            HideOfflineNotice();
            HideConnectionPanel();
            SkinManager.Remember(profile);
        }

        private void HandleSkinCurrency(int coins)
        {
            var nickname = PlayerPrefs.GetString(LocalNicknameKey, "Гость");
            var mmr = PlayerPrefs.GetString(LocalMmrKey, "1000");
            var text = coins.ToString(CultureInfo.InvariantCulture);
            PlayerPrefs.SetString(LocalCoinsKey, text);
            PlayerPrefs.Save();
            WriteProfile(nickname, text, mmr);
        }

        private void SyncBalanceFromNetwork()
        {
            if (SkinManager.Instance != null && SkinManager.Instance.HasCatalog)
            {
                HandleSkinCurrency(SkinManager.Instance.SoftCurrency);
                return;
            }

            var network = NetworkManager.Instance;
            if (network != null && network.CurrentUser != null)
                HandleSkinCurrency(network.CurrentUser.SoftCurrency);
            else
                ShowStoredProfile();
        }

        private void ApplyLocalNickname(string nickname)
        {
            PlayerPrefs.SetString(LocalNicknameKey, nickname);
            PlayerPrefs.Save();
            ShowStoredProfile();
        }

        private void ShowStoredProfile()
        {
            WriteProfile(
                PlayerPrefs.GetString(LocalNicknameKey, "Гость"),
                PlayerPrefs.GetString(LocalCoinsKey, "0"),
                PlayerPrefs.GetString(LocalMmrKey, "0"));
        }

        private void WriteProfile(string nickname, string coins, string mmr)
        {
            EnsureProfileLabels();
            if (nicknameText != null)
                nicknameText.text = nickname;
            if (coinBalanceText != null)
                coinBalanceText.text = "Баланс: " + coins;
            if (mmrText != null)
                mmrText.text = "Рейтинг: " + mmr;
        }

        private void EnsureProfileLabels()
        {
            if (_profileCard == null)
            {
                _profileCard = UiFactory.CreateFramedCard(transform, "ProfileCard");
                _profileCard.SetSiblingIndex(2);
                UiFactory.Place(_profileCard, 0.06f, 0.91f, 0.94f, 0.985f);

                _coinIcon = UiFactory.CreateBadge(_profileCard, "CoinIcon", true);
                PlaceBadge(_coinIcon.rectTransform, 0.4f);
                _ratingIcon = UiFactory.CreateBadge(_profileCard, "RatingIcon", false);
                PlaceBadge(_ratingIcon.rectTransform, 0.7f);
            }

            if (nicknameText == null)
                nicknameText = UiFactory.CreateAutoLabel(_profileCard, "Nickname", "Гость", TextAlignmentOptions.MidlineLeft);
            else
                nicknameText.transform.SetParent(_profileCard, false);
            UiFactory.Place(nicknameText.rectTransform, 0.03f, 0.12f, 0.38f, 0.88f);

            if (coinBalanceText == null)
                coinBalanceText = UiFactory.CreateAutoLabel(_profileCard, "Coins", "Баланс: 0", TextAlignmentOptions.MidlineLeft);
            else
                coinBalanceText.transform.SetParent(_profileCard, false);
            UiFactory.Place(coinBalanceText.rectTransform, 0.46f, 0.12f, 0.68f, 0.88f);

            if (mmrText == null)
                mmrText = UiFactory.CreateAutoLabel(_profileCard, "Mmr", "Рейтинг: 0", TextAlignmentOptions.MidlineLeft);
            else
                mmrText.transform.SetParent(_profileCard, false);
            UiFactory.Place(mmrText.rectTransform, 0.76f, 0.12f, 0.98f, 0.88f);
        }

        private static void PlaceBadge(RectTransform rect, float x)
        {
            rect.anchorMin = new Vector2(x, 0.18f);
            rect.anchorMax = new Vector2(x, 0.82f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(26f, 0f);
            rect.localScale = Vector3.one;
        }

        private void EnsureOfflineNotice()
        {
            if (_offlineNotice != null)
                return;

            _offlineNotice = UiFactory.CreateAutoLabel(
                transform,
                "OfflineNotice",
                "Игра в офлайн-режиме",
                TextAlignmentOptions.Center);
            UiFactory.Place(_offlineNotice.rectTransform, 0.1f, 0.75f, 0.9f, 0.81f);
            _offlineNotice.color = new Color(1f, 0.86f, 0.45f, 1f);
            _offlineNotice.enableAutoSizing = true;
            _offlineNotice.fontSizeMin = 16f;
            _offlineNotice.fontSizeMax = 24f;
            _offlineNotice.gameObject.SetActive(false);
        }

        private void PlaceOfflineNotice()
        {
            if (_offlineNotice == null)
                return;

            UiFactory.Place(_offlineNotice.rectTransform, 0.1f, 0.75f, 0.9f, 0.81f);
            _offlineNotice.transform.SetAsLastSibling();
        }

        private void HideOfflineNotice()
        {
            if (_offlineNotice != null)
                _offlineNotice.gameObject.SetActive(false);
        }

        private void HandleConnectionLost()
        {
            if (_banned || this == null)
                return;

            RefreshOnlinePlayLocks();
            ShowConnectionPanel();
        }

        private void HandleConnectionRestored()
        {
            if (_banned || this == null)
                return;

            _reconnectBusy = false;
            HideConnectionPanel();
            RefreshOnlinePlayLocks();

            var network = NetworkManager.Instance;
            if (network != null && network.CurrentUser != null)
                ApplyProfile(network.CurrentUser);

            // Manual «Переподключиться» resumes itself; auto socket recovery resumes here.
            if (!_manualReconnect)
                _ = ResumeOnlineMatchAsync();
        }

        private void ShowConnectionPanel()
        {
            EnsureConnectionPanel();
            HideOfflineNotice();
            if (_connectionLabel != null)
            {
                var inMatch = NetworkManager.Instance != null && NetworkManager.Instance.ActiveMatchId.HasValue;
                _connectionLabel.text = inMatch
                    ? GameText.Get("ui.connection.lost_match")
                    : GameText.Get("service.body_offline");
            }

            if (_reconnectButton != null)
            {
                var caption = _reconnectButton.GetComponentInChildren<Text>();
                if (caption != null)
                    caption.text = GameText.Get("ui.connection.reconnect");
            }

            if (_reconnectButton != null)
                _reconnectButton.interactable = !_reconnectBusy;
            if (_connectionPanel != null)
            {
                _connectionPanel.SetActive(true);
                _connectionPanel.transform.SetAsLastSibling();
            }
        }

        private void HideConnectionPanel()
        {
            if (_connectionPanel != null)
                _connectionPanel.SetActive(false);
        }

        private void EnsureConnectionPanel()
        {
            if (_connectionPanel != null)
                return;

            _connectionPanel = new GameObject("ConnectionPanel", typeof(RectTransform), typeof(Image));
            _connectionPanel.layer = gameObject.layer;
            _connectionPanel.transform.SetParent(transform, false);
            var image = _connectionPanel.GetComponent<Image>();
            image.color = UIThemeConfig.PanelBgSolid;
            UiFactory.EnsureSoftShadow(_connectionPanel);
            var rect = _connectionPanel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.1f, 0.34f);
            rect.anchorMax = new Vector2(0.9f, 0.78f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            _connectionLabel = UiFactory.CreateLabel(
                _connectionPanel.transform,
                "Text",
                GameText.Get("service.body_offline"),
                26,
                TextAnchor.MiddleCenter);
            _connectionLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            _connectionLabel.verticalOverflow = VerticalWrapMode.Truncate;
            _connectionLabel.resizeTextForBestFit = true;
            _connectionLabel.resizeTextMinSize = 16;
            _connectionLabel.resizeTextMaxSize = 26;
            UiFactory.Place(_connectionLabel.rectTransform, 0.08f, 0.44f, 0.92f, 0.92f);

            _reconnectButton = UiFactory.CreateButton(_connectionPanel.transform, GameText.Get("ui.connection.reconnect"));
            UiFactory.StyleAccent(_reconnectButton);
            UiFactory.Place(_reconnectButton.GetComponent<RectTransform>(), 0.14f, 0.1f, 0.86f, 0.36f);
            _reconnectButton.onClick.AddListener(OnReconnectClicked);
            _connectionPanel.SetActive(false);
        }

        private async void OnReconnectClicked()
        {
            var network = NetworkManager.Instance;
            if (_reconnectBusy || network == null)
                return;

            _reconnectBusy = true;
            _manualReconnect = true;
            if (_reconnectButton != null)
                _reconnectButton.interactable = false;
            if (_connectionLabel != null)
                _connectionLabel.text = GameText.Get("ui.connection.reconnecting");

            var restored = false;
            try
            {
                restored = await network.TryReconnect();
            }
            catch (OperationCanceledException)
            {
                restored = false;
            }

            if (this == null)
                return;

            if (!restored)
            {
                _manualReconnect = false;
                _reconnectBusy = false;
                if (_connectionLabel != null)
                    _connectionLabel.text = GameText.Get("service.body_offline");
                if (_reconnectButton != null)
                    _reconnectButton.interactable = true;
                return;
            }

            HideConnectionPanel();
            RefreshOnlinePlayLocks();
            if (network.CurrentUser != null)
                ApplyProfile(network.CurrentUser);
            await ResumeOnlineMatchAsync();
            _manualReconnect = false;
            _reconnectBusy = false;
        }

        private void RefreshOnlinePlayLocks()
        {
            if (_menu == null || _banned)
                return;

            var network = NetworkManager.Instance;
            // Active unfinished match blocks starting a new one until it ends or reconnect succeeds into gameplay.
            var canStartNew = network != null
                && !network.ConnectionDropped
                && !network.ActiveMatchId.HasValue
                && ClientGate.MatchmakingEnabled
                && ClientGate.LobbiesEnabled;
            _menu.SetOnlinePlayAvailable(canStartNew);
            _menu.SetShopAvailable(ClientGate.ShopEnabled && !_banned);
        }

        private void ResumeOnlineMatch()
        {
            _ = ResumeOnlineMatchAsync();
        }

        private async Task ResumeOnlineMatchAsync()
        {
            var network = NetworkManager.Instance;
            if (network == null || !network.ActiveMatchId.HasValue || UIManager.Instance == null)
                return;

            var gameplay = GameplayScreen.Instance != null
                ? GameplayScreen.Instance
                : FindAnyObjectByType<GameplayScreen>(FindObjectsInactive.Include);
            if (gameplay == null)
                return;

            var matchKey = network.ActiveMatchId.Value.ToString(CultureInfo.InvariantCulture);
            MatchReconnectResponseDto snapshot = null;
            if (network.ResumeState != null && network.ResumeState.Match != null)
            {
                snapshot = MatchReconnectResponseDto.From(network.ResumeState);
                network.ClearResumeState();
            }

            if (snapshot == null || snapshot.Match == null || snapshot.Match.Status != "playing")
            {
                try
                {
                    snapshot = await network.SendReconnectRequest(matchKey);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("Yahtzee resume match: " + exception.Message);
                    return;
                }
            }

            if (snapshot == null || snapshot.Match == null || snapshot.Match.Status != "playing")
                return;

            UIManager.Instance.SwitchToScreen(ScreenType.Gameplay);
            await gameplay.RestoreReconnectedMatch(snapshot, true);
        }

        private static NetworkManager ResolveNetwork()
        {
            if (NetworkManager.Instance != null)
                return NetworkManager.Instance;

            var existing = FindAnyObjectByType<NetworkManager>();
            if (existing != null)
                return existing;

            var host = new GameObject("NetworkManager");
            return host.AddComponent<NetworkManager>();
        }

        private static string ResolveDeviceId()
        {
            var saved = PlayerPrefs.GetString(DeviceIdKey, string.Empty);
            if (IsDeviceId(saved))
                return saved;

            // Random local id — deviceUniqueIdentifier alone is not a credential.
            var built = "device-" + Guid.NewGuid().ToString("N");

#if UNITY_EDITOR
            built += "_Editor";
#else
            built += "_Build";
#endif

            PlayerPrefs.SetString(DeviceIdKey, built);
            PlayerPrefs.Save();
            return built;
        }

        private static string ResolveDeviceSecret()
        {
            var saved = PlayerPrefs.GetString(DeviceSecretKey, string.Empty);
            if (!string.IsNullOrEmpty(saved) && saved.Length >= 32 && saved.Length <= 128)
                return saved;

            var secret = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
            PlayerPrefs.SetString(DeviceSecretKey, secret);
            PlayerPrefs.Save();
            return secret;
        }

        private static string SanitizeDeviceId(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return string.Empty;

            var buffer = new char[Math.Min(raw.Length, 128)];
            var count = 0;
            for (var index = 0; index < raw.Length && count < buffer.Length; index++)
            {
                var character = raw[index];
                var allowed = (character >= 'A' && character <= 'Z')
                    || (character >= 'a' && character <= 'z')
                    || (character >= '0' && character <= '9')
                    || character == '.'
                    || character == '_'
                    || character == ':'
                    || character == '-';
                if (allowed)
                    buffer[count++] = character;
            }

            return count == 0 ? string.Empty : new string(buffer, 0, count);
        }

        private static bool IsDeviceId(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length < 8 || value.Length > 128)
                return false;

            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
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

        private static string CurrentPlatform()
        {
#if UNITY_ANDROID
            return "android";
#elif UNITY_IOS
            return "ios";
#elif UNITY_WEBGL
            return "webgl";
#else
            return "pc";
#endif
        }
    }
}
