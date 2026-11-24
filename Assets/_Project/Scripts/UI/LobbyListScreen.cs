using System;
using System.Collections.Generic;
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
    public sealed class LobbyListScreen : UIScreen
    {
        public override ScreenType Type => ScreenType.LobbyList;

        [SerializeField] private TMP_InputField searchInputField;
        [SerializeField] private Toggle hideFullToggle;
        [SerializeField] private Toggle hidePasswordToggle;
        [SerializeField] private Transform lobbyContainer;
        [SerializeField] private GameObject lobbyRowPrefab;
        [SerializeField] private TMP_InputField roomPasswordInputField;
        [SerializeField] private Button createNewLobbyButton;

        private GameObject _listRoot;
        private GameObject _passwordRoot;
        private GameObject _waitingRoot;
        private Text _passwordError;
        private Text _emptyLabel;
        private Text _waitingLabel;
        private Button _startButton;
        private Button _confirmPasswordButton;
        private LobbyRoomDto _pendingRoom;
        private MatchState _waitingMatch;
        private bool _openWaitingOnShow;
        private bool _busy;
        private bool _enteringGame;
        private int _refreshGeneration;
        private int _page = 1;
        private int _usedRows;
        private bool _hasMore;
        private bool _loadingPage;
        private ScrollRect _scroll;
        private readonly List<GameObject> _rowPool = new List<GameObject>();

        private const int PageSize = 20;

        public override void Show()
        {
            base.Show();
            EnsureInterface();
            if (_openWaitingOnShow && _waitingMatch != null)
            {
                _openWaitingOnShow = false;
                PresentWaiting(_waitingMatch);
                return;
            }

            PresentList();
            QueueRefresh(true);
        }

        private void EnsureInterface()
        {
            var rect = (RectTransform)transform;
            UiFactory.EnsureScreenFillsCanvas(rect);
            ApplyOpaqueMenuBackground(rect);
            if (_listRoot != null && searchInputField != null && lobbyContainer != null && rect.rect.width >= 200f)
                return;

            ClearInterface();
            UiFactory.EnsureScreenFillsCanvas(rect);
            ApplyOpaqueMenuBackground(rect);
            BuildInterface();
            Wire();
        }

        private static void ApplyOpaqueMenuBackground(RectTransform screen)
        {
            UiFactory.ApplyOpaqueMenuBackground(screen);
        }

        private void ClearInterface()
        {
            if (_listRoot != null)
                DestroyImmediate(_listRoot);
            if (_passwordRoot != null)
                DestroyImmediate(_passwordRoot);
            if (_waitingRoot != null)
                DestroyImmediate(_waitingRoot);
            if (lobbyRowPrefab != null)
                DestroyImmediate(lobbyRowPrefab);

            _listRoot = null;
            _passwordRoot = null;
            _waitingRoot = null;
            lobbyRowPrefab = null;
            searchInputField = null;
            hideFullToggle = null;
            hidePasswordToggle = null;
            lobbyContainer = null;
            roomPasswordInputField = null;
            createNewLobbyButton = null;
            _passwordError = null;
            _emptyLabel = null;
            _waitingLabel = null;
            _startButton = null;
            _confirmPasswordButton = null;
            _scroll = null;
            _rowPool.Clear();
            _usedRows = 0;
            _page = 1;
            _hasMore = false;
            _loadingPage = false;
        }

        private void OnEnable()
        {
            NetworkManager.OnLobbyJoinFailed += HandleLobbyJoinFailed;
            NetworkManager.OnMatchStarted += HandleMatchStarted;
        }

        private void OnDisable()
        {
            NetworkManager.OnLobbyJoinFailed -= HandleLobbyJoinFailed;
            NetworkManager.OnMatchStarted -= HandleMatchStarted;
        }


        public void OpenWaitingRoom(MatchState match)
        {
            _waitingMatch = match;
            _openWaitingOnShow = match != null;
        }

        /// <summary>
        /// Enter the host waiting room after create. Call after SwitchToScreen(LobbyList)
        /// so we hit the UIManager-owned screen instance (not a stray FindAnyObject).
        /// </summary>
        public void EnterWaitingRoom(MatchState match)
        {
            if (match == null)
                return;

            EnsureInterface();
            _openWaitingOnShow = false;
            PresentWaiting(match);
        }

        public async Task RefreshLobbyList()
        {
            await LoadRooms(true);
        }

        private async Task LoadRooms(bool reset)
        {
            if (NetworkManager.Instance == null || lobbyContainer == null)
                return;
            if (!reset && (_loadingPage || !_hasMore))
                return;

            var generation = _refreshGeneration;
            var page = reset ? 1 : _page + 1;
            var search = searchInputField != null ? searchInputField.text : string.Empty;
            var hideFull = hideFullToggle == null || hideFullToggle.isOn;
            var hidePassword = hidePasswordToggle != null && hidePasswordToggle.isOn;
            _loadingPage = true;
            LobbyListResponseDto parsed;
            try
            {
                parsed = await NetworkManager.Instance.GetLobbies(search, hideFull, hidePassword, page, PageSize);
            }
            catch (Exception exception)
            {
                if (generation == _refreshGeneration)
                    _loadingPage = false;

                if (generation != _refreshGeneration || this == null)
                    return;

                if (reset)
                {
                    ReleaseRows();
                    ShowEmpty(GameText.FromException(exception));
                }

                return;
            }

            if (generation != _refreshGeneration || this == null || lobbyContainer == null || parsed == null)
            {
                if (generation == _refreshGeneration)
                    _loadingPage = false;

                return;
            }

            var rooms = parsed.Lobbies;
            var added = 0;
            if (reset)
                ReleaseRows();

            if (rooms != null)
            {
                for (var index = 0; index < rooms.Length; index++)
                {
                    var room = rooms[index];
                    if (room == null || room.MatchId <= 0)
                        continue;

                    SpawnRow(room);
                    added++;
                }
            }

            _page = page;
            _hasMore = parsed.HasMore && added > 0;
            _loadingPage = false;
            if (reset && _usedRows == 0)
            {
                ShowEmpty(GameText.Get("ui.lobby.empty_list"));
                return;
            }

            ShowEmpty(null);
            Canvas.ForceUpdateCanvases();
            if (reset && _scroll != null)
                _scroll.verticalNormalizedPosition = 1f;

            if (_hasMore && ContentHasHeight() && ListShorterThanView())
                _ = LoadRooms(false);
        }

        private bool ContentHasHeight()
        {
            var content = lobbyContainer as RectTransform;
            return content != null && content.rect.height > 40f;
        }

        private bool ListShorterThanView()
        {
            var content = lobbyContainer as RectTransform;
            if (content == null || _scroll == null || _scroll.viewport == null)
                return false;

            return content.rect.height <= _scroll.viewport.rect.height + 4f;
        }

        private void Wire()
        {
            if (searchInputField != null)
            {
                searchInputField.onValueChanged.RemoveListener(OnSearchChanged);
                searchInputField.onValueChanged.AddListener(OnSearchChanged);
            }

            if (hideFullToggle != null)
            {
                hideFullToggle.onValueChanged.RemoveListener(OnFilterChanged);
                hideFullToggle.onValueChanged.AddListener(OnFilterChanged);
            }

            if (hidePasswordToggle != null)
            {
                hidePasswordToggle.onValueChanged.RemoveListener(OnFilterChanged);
                hidePasswordToggle.onValueChanged.AddListener(OnFilterChanged);
            }

            if (createNewLobbyButton != null)
            {
                createNewLobbyButton.onClick.RemoveListener(OpenCreateLobby);
                createNewLobbyButton.onClick.AddListener(OpenCreateLobby);
            }

            if (_scroll != null)
            {
                _scroll.onValueChanged.RemoveListener(OnListScrolled);
                _scroll.onValueChanged.AddListener(OnListScrolled);
            }

            if (_confirmPasswordButton != null)
            {
                _confirmPasswordButton.onClick.RemoveListener(ConfirmPassword);
                _confirmPasswordButton.onClick.AddListener(ConfirmPassword);
            }
        }

        private void RefreshWithFilters()
        {
            QueueRefresh(true);
        }

        private void OnSearchChanged(string _)
        {
            QueueRefresh(false);
        }

        private void OnFilterChanged(bool _)
        {
            QueueRefresh(true);
        }

        private void OnListScrolled(Vector2 position)
        {
            if (_loadingPage || !_hasMore || position.y > 0.08f)
                return;
            if (_waitingRoot != null && _waitingRoot.activeSelf || ListShorterThanView())
                return;

            _ = LoadRooms(false);
        }

        private void QueueRefresh(bool immediate)
        {
            var generation = ++_refreshGeneration;
            _ = RefreshAfter(generation, immediate ? 0 : 350);
        }

        private async Task RefreshAfter(int generation, int delayMilliseconds)
        {
            if (delayMilliseconds > 0)
            {
                try
                {
                    await Task.Delay(delayMilliseconds);
                }
                catch (Exception)
                {
                    return;
                }
            }

            if (generation != _refreshGeneration || this == null || !isActiveAndEnabled || _waitingRoot != null && _waitingRoot.activeSelf)
                return;

            await RefreshLobbyList();
        }

        private void SpawnRow(LobbyRoomDto room)
        {
            if (room == null || lobbyRowPrefab == null || lobbyContainer == null)
                return;

            GameObject rowObject;
            if (_usedRows < _rowPool.Count)
            {
                rowObject = _rowPool[_usedRows];
            }
            else
            {
                rowObject = Instantiate(lobbyRowPrefab, lobbyContainer);
                _rowPool.Add(rowObject);
            }

            _usedRows++;
            rowObject.transform.SetParent(lobbyContainer, false);
            rowObject.SetActive(true);
            rowObject.transform.SetSiblingIndex(_usedRows - 1);
            var row = rowObject.GetComponent<LobbyRoomRow>();
            if (row == null)
                row = rowObject.AddComponent<LobbyRoomRow>();

            row.Bind(room, () => OnJoinClicked(room));
        }

        private void ReleaseRows()
        {
            for (var index = 0; index < _rowPool.Count; index++)
            {
                var row = _rowPool[index];
                if (row != null)
                    row.SetActive(false);
            }

            _usedRows = 0;
        }

        private void ShowEmpty(string message)
        {
            if (_emptyLabel == null)
                return;

            var show = !string.IsNullOrEmpty(message);
            _emptyLabel.gameObject.SetActive(show);
            if (show)
                _emptyLabel.text = message;
        }

        private void OnJoinClicked(LobbyRoomDto room)
        {
            if (room == null || _busy)
                return;

            if (!room.HasPassword)
            {
                _ = EnterRoom(room, null);
                return;
            }

            _pendingRoom = room;
            if (roomPasswordInputField != null)
                roomPasswordInputField.text = string.Empty;
            if (_passwordError != null)
                _passwordError.text = string.Empty;
            if (_passwordRoot != null)
            {
                _passwordRoot.SetActive(true);
                _passwordRoot.transform.SetAsLastSibling();
            }
        }

        private void ConfirmPassword()
        {
            if (_pendingRoom == null || _busy)
                return;

            var password = roomPasswordInputField != null ? roomPasswordInputField.text : string.Empty;
            _ = EnterRoom(_pendingRoom, password);
        }

        private async Task EnterRoom(LobbyRoomDto room, string password)
        {
            if (room == null || NetworkManager.Instance == null)
                return;

            _busy = true;
            try
            {
                var joined = await NetworkManager.Instance.JoinLobbyWithPassword(
                    room.MatchId.ToString(CultureInfo.InvariantCulture),
                    password);
                if (!joined || this == null)
                    return;

                if (_passwordRoot != null)
                    _passwordRoot.SetActive(false);
                PresentWaiting(NetworkManager.Instance.LastLobby);
            }
            catch (Exception exception)
            {
                if (_passwordRoot != null && _passwordRoot.activeSelf && _passwordError != null)
                    ScreenFeedback.SetError(_passwordError, exception);
                else
                    ShowEmpty(GameText.FromException(exception));
            }
            finally
            {
                _busy = false;
            }
        }

        private void HandleLobbyJoinFailed(string message)
        {
            if (!isActiveAndEnabled || _passwordRoot == null || !_passwordRoot.activeSelf)
                return;

            if (_passwordError != null)
            {
                _passwordError.color = new Color(0.92f, 0.35f, 0.32f, 1f);
                _passwordError.text = string.IsNullOrEmpty(message)
                    ? GameText.Get("ui.lobby.wrong_password")
                    : GameText.FromError(ApiErrorCode.InvalidPassword, message);
            }
        }

        private void PresentList()
        {
            if (_listRoot != null)
                _listRoot.SetActive(true);
            if (_passwordRoot != null)
                _passwordRoot.SetActive(false);
            if (_waitingRoot != null)
                _waitingRoot.SetActive(false);
            _waitingMatch = null;
        }

        private void PresentWaiting(MatchState match)
        {
            _waitingMatch = match;
            if (_listRoot != null)
                _listRoot.SetActive(false);
            if (_passwordRoot != null)
                _passwordRoot.SetActive(false);
            if (_waitingRoot != null)
                _waitingRoot.SetActive(true);

            var title = match != null && match.Players != null ? RoomTitle(match) : GameText.Get("ui.lobby.room_default");
            var count = match != null ? match.PlayerCount : 0;
            var max = match != null ? match.MaxPlayers : 0;
            var names = PlayerNames(match);
            if (_waitingLabel != null)
                _waitingLabel.text = title + "\nИгроков: " + count.ToString(CultureInfo.InvariantCulture)
                    + "/" + max.ToString(CultureInfo.InvariantCulture) + "\n" + names;

            var localId = NetworkManager.Instance != null && NetworkManager.Instance.CurrentUser != null
                ? NetworkManager.Instance.CurrentUser.Id
                : 0;
            var host = match != null && match.HostUserId.HasValue && match.HostUserId.Value == localId;
            if (_startButton != null)
                _startButton.gameObject.SetActive(host);

            if (match != null && NetworkManager.Instance != null)
                _ = Watch(match.Id);
        }

        private async Task Watch(int matchId)
        {
            try
            {
                await NetworkManager.Instance.WatchLobby(matchId.ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Yatzy lobby watch: " + exception.Message);
            }
        }

        private async void HandleMatchStarted(MatchStartedEvent started)
        {
            if (!isActiveAndEnabled || _enteringGame || started == null || _waitingMatch == null || started.MatchId != _waitingMatch.Id)
                return;

            if (NetworkManager.Instance == null)
                return;

            _enteringGame = true;
            try
            {
                await NetworkManager.Instance.SendReconnectRequest(started.MatchId.ToString(CultureInfo.InvariantCulture));
                if (this == null || UIManager.Instance == null)
                    return;

                var gameplay = GameplayScreen.Instance != null
                    ? GameplayScreen.Instance
                    : FindAnyObjectByType<GameplayScreen>(FindObjectsInactive.Include);
                if (gameplay != null)
                    gameplay.InitializeMatchMode(true, started.MatchId.ToString(CultureInfo.InvariantCulture));
                UIManager.Instance.SwitchToScreen(ScreenType.Gameplay);
            }
            catch (Exception exception)
            {
                _enteringGame = false;
                Debug.LogWarning("Yatzy lobby start: " + exception.Message);
            }
        }

        private async void StartWaitingMatch()
        {
            if (_busy || _waitingMatch == null || NetworkManager.Instance == null)
                return;

            _busy = true;
            _enteringGame = true;
            try
            {
                await NetworkManager.Instance.StartLobbyMatch(_waitingMatch.Id.ToString(CultureInfo.InvariantCulture));
                if (this == null || UIManager.Instance == null)
                    return;

                var gameplay = GameplayScreen.Instance != null
                    ? GameplayScreen.Instance
                    : FindAnyObjectByType<GameplayScreen>(FindObjectsInactive.Include);
                if (gameplay != null)
                    gameplay.InitializeMatchMode(true, _waitingMatch.Id.ToString(CultureInfo.InvariantCulture));
                UIManager.Instance.SwitchToScreen(ScreenType.Gameplay);
            }
            catch (Exception exception)
            {
                _enteringGame = false;
                if (_waitingLabel != null)
                    ScreenFeedback.SetError(_waitingLabel, exception);
            }
            finally
            {
                _busy = false;
            }
        }

        private async void LeaveWaitingMatch()
        {
            if (_busy)
                return;

            var match = _waitingMatch;
            _busy = true;
            try
            {
                if (match != null && NetworkManager.Instance != null)
                    await NetworkManager.Instance.LeaveLobby(match.Id.ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Yatzy lobby leave: " + exception.Message);
            }
            finally
            {
                _busy = false;
            }

            if (this == null)
                return;

            PresentList();
            QueueRefresh(true);
        }

        private void OpenCreateLobby()
        {
            if (UIManager.Instance != null)
                UIManager.Instance.SwitchToScreen(ScreenType.CustomLobby);
        }

        private void ClosePassword()
        {
            _pendingRoom = null;
            if (_passwordRoot != null)
                _passwordRoot.SetActive(false);
        }

        private void ReturnToMenu()
        {
            if (UIManager.Instance != null)
                UIManager.Instance.SwitchToScreen(ScreenType.OnlineMenu);
        }

        private static string RoomTitle(MatchState match)
        {
            if (match.Players == null)
                return "Лобби игрока";

            for (var index = 0; index < match.Players.Length; index++)
            {
                var seat = match.Players[index];
                if (seat == null || !seat.IsHost)
                    continue;

                var nickname = string.IsNullOrWhiteSpace(seat.Nickname) ? "Игрок" : seat.Nickname.Trim();
                return "Лобби игрока " + nickname;
            }

            return "Лобби игрока";
        }

        private static string PlayerNames(MatchState match)
        {
            if (match == null || match.Players == null || match.Players.Length == 0)
                return "Пока никого нет.";

            var names = new List<string>(match.Players.Length);
            for (var index = 0; index < match.Players.Length; index++)
            {
                var seat = match.Players[index];
                if (seat == null)
                    continue;

                var nickname = string.IsNullOrWhiteSpace(seat.Nickname) ? "Игрок" : seat.Nickname.Trim();
                names.Add(seat.IsHost ? nickname + " (хост)" : nickname);
            }

            return names.Count == 0 ? "Пока никого нет." : string.Join("\n", names);
        }

        private void BuildInterface()
        {
            _listRoot = new GameObject("List", typeof(RectTransform));
            _listRoot.layer = gameObject.layer;
            _listRoot.transform.SetParent(transform, false);
            Stretch((RectTransform)_listRoot.transform);

            var title = UiFactory.CreateLabel(_listRoot.transform, "Title", "Комнаты", 48, TextAnchor.MiddleCenter);
            UiFactory.Place(title.rectTransform, 0.08f, 0.9f, 0.92f, 0.98f);

            searchInputField = CreateTmpInput(_listRoot.transform, "Search", "Поиск по названию");
            UiFactory.Place(searchInputField.GetComponent<RectTransform>(), 0.08f, 0.82f, 0.92f, 0.89f);

            hideFullToggle = CreateToggle(_listRoot.transform, "HideFull", "Скрыть заполненные", 0.08f, 0.75f, 0.48f, 0.81f, true);
            hidePasswordToggle = CreateToggle(_listRoot.transform, "HidePassword", "Скрыть с паролем", 0.52f, 0.75f, 0.92f, 0.81f, false);

            var scroll = CreateScroll(_listRoot.transform, out var content);
            _scroll = scroll.GetComponent<ScrollRect>();
            UiFactory.Place(scroll, 0.06f, 0.24f, 0.94f, 0.73f);
            lobbyContainer = content;
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            lobbyRowPrefab = BuildRowTemplate();
            lobbyRowPrefab.transform.SetParent(transform, false);
            lobbyRowPrefab.SetActive(false);

            _emptyLabel = UiFactory.CreateLabel(_listRoot.transform, "Empty", "Открытых комнат нет.", 24, TextAnchor.MiddleCenter);
            UiFactory.Place(_emptyLabel.rectTransform, 0.1f, 0.42f, 0.9f, 0.52f);

            var refresh = UiFactory.CreateButton(_listRoot.transform, "Обновить");
            UiFactory.StyleGlass(refresh);
            UiFactory.Place(refresh.GetComponent<RectTransform>(), 0.08f, 0.12f, 0.48f, 0.21f);
            refresh.onClick.AddListener(RefreshWithFilters);

            createNewLobbyButton = UiFactory.CreateButton(_listRoot.transform, "Создать комнату");
            UiFactory.StyleAccent(createNewLobbyButton);
            UiFactory.Place(createNewLobbyButton.GetComponent<RectTransform>(), 0.52f, 0.12f, 0.92f, 0.21f);

            var back = UiFactory.CreateButton(_listRoot.transform, "Назад");
            UiFactory.StyleGlass(back);
            UiFactory.Place(back.GetComponent<RectTransform>(), 0.08f, 0.02f, 0.92f, 0.1f);
            back.onClick.AddListener(ReturnToMenu);

            BuildPasswordPanel();
            BuildWaitingPanel();
        }

        private void BuildPasswordPanel()
        {
            _passwordRoot = new GameObject("PasswordPanel", typeof(RectTransform), typeof(Image));
            _passwordRoot.layer = gameObject.layer;
            _passwordRoot.transform.SetParent(transform, false);
            Stretch((RectTransform)_passwordRoot.transform);
            _passwordRoot.GetComponent<Image>().color = new Color(0.1f, 0.14f, 0.22f, 0.42f);

            var card = new GameObject("Card", typeof(RectTransform), typeof(Image));
            card.layer = gameObject.layer;
            card.transform.SetParent(_passwordRoot.transform, false);
            card.GetComponent<Image>().color = new Color(0.1f, 0.16f, 0.13f, 0.98f);
            UiFactory.Place(card.GetComponent<RectTransform>(), 0.12f, 0.32f, 0.88f, 0.7f);

            var caption = UiFactory.CreateLabel(card.transform, "Caption", "Пароль комнаты", 32, TextAnchor.MiddleCenter);
            UiFactory.Place(caption.rectTransform, 0.08f, 0.72f, 0.92f, 0.92f);

            roomPasswordInputField = CreateTmpInput(card.transform, "RoomPassword", "Пароль");
            roomPasswordInputField.contentType = TMP_InputField.ContentType.Password;
            UiFactory.Place(roomPasswordInputField.GetComponent<RectTransform>(), 0.1f, 0.48f, 0.9f, 0.68f);

            _passwordError = UiFactory.CreateLabel(card.transform, "PasswordError", string.Empty, 22, TextAnchor.MiddleCenter);
            _passwordError.color = new Color(0.95f, 0.28f, 0.24f, 1f);
            UiFactory.Place(_passwordError.rectTransform, 0.08f, 0.32f, 0.92f, 0.46f);

            _confirmPasswordButton = UiFactory.CreateButton(card.transform, "Подтвердить");
            UiFactory.StyleAccent(_confirmPasswordButton);
            UiFactory.Place(_confirmPasswordButton.GetComponent<RectTransform>(), 0.1f, 0.08f, 0.48f, 0.26f);

            var cancel = UiFactory.CreateButton(card.transform, "Отмена");
            UiFactory.StyleGlass(cancel);
            UiFactory.Place(cancel.GetComponent<RectTransform>(), 0.52f, 0.08f, 0.9f, 0.26f);
            cancel.onClick.AddListener(ClosePassword);
            _passwordRoot.SetActive(false);
        }

        private void BuildWaitingPanel()
        {
            _waitingRoot = new GameObject("Waiting", typeof(RectTransform));
            _waitingRoot.layer = gameObject.layer;
            _waitingRoot.transform.SetParent(transform, false);
            Stretch((RectTransform)_waitingRoot.transform);

            var caption = UiFactory.CreateLabel(_waitingRoot.transform, "WaitingTitle", "Ожидание игроков", 40, TextAnchor.MiddleCenter);
            UiFactory.Place(caption.rectTransform, 0.08f, 0.84f, 0.92f, 0.94f);

            _waitingLabel = UiFactory.CreateLabel(_waitingRoot.transform, "WaitingBody", string.Empty, 28, TextAnchor.UpperCenter);
            UiFactory.Place(_waitingLabel.rectTransform, 0.1f, 0.36f, 0.9f, 0.8f);

            _startButton = UiFactory.CreateButton(_waitingRoot.transform, "Начать");
            UiFactory.StyleAccent(_startButton);
            UiFactory.Place(_startButton.GetComponent<RectTransform>(), 0.18f, 0.2f, 0.82f, 0.3f);
            _startButton.onClick.AddListener(StartWaitingMatch);

            var leave = UiFactory.CreateButton(_waitingRoot.transform, "Выйти из комнаты");
            UiFactory.StyleDestructive(leave);
            UiFactory.Place(leave.GetComponent<RectTransform>(), 0.18f, 0.08f, 0.82f, 0.17f);
            leave.onClick.AddListener(LeaveWaitingMatch);
            _waitingRoot.SetActive(false);
        }

        private GameObject BuildRowTemplate()
        {
            var row = new GameObject("LobbyRow", typeof(RectTransform), typeof(Image), typeof(LayoutElement), typeof(LobbyRoomRow));
            row.layer = gameObject.layer;
            row.GetComponent<Image>().color = UIThemeConfig.CardBg;
            var element = row.GetComponent<LayoutElement>();
            element.minHeight = 92f;
            element.preferredHeight = 92f;

            var name = UiFactory.CreateLabel(row.transform, "RoomName", "Комната", 26, TextAnchor.MiddleLeft);
            UiFactory.Place(name.rectTransform, 0.04f, 0.46f, 0.62f, 0.92f);
            var occupancy = UiFactory.CreateLabel(row.transform, "Occupancy", "0/4", 24, TextAnchor.MiddleLeft);
            UiFactory.Place(occupancy.rectTransform, 0.04f, 0.08f, 0.28f, 0.46f);

            var lockIcon = new GameObject("LockIcon", typeof(RectTransform), typeof(Image));
            lockIcon.layer = gameObject.layer;
            lockIcon.transform.SetParent(row.transform, false);
            lockIcon.GetComponent<Image>().color = new Color(0.95f, 0.78f, 0.28f, 1f);
            UiFactory.Place(lockIcon.GetComponent<RectTransform>(), 0.3f, 0.18f, 0.4f, 0.42f);
            var lockCaption = UiFactory.CreateLabel(lockIcon.transform, "Caption", "пароль", 16, TextAnchor.MiddleCenter);
            Stretch(lockCaption.rectTransform);

            var join = UiFactory.CreateButton(row.transform, "Войти");
            UiFactory.StyleAccent(join);
            UiFactory.Place(join.GetComponent<RectTransform>(), 0.68f, 0.18f, 0.96f, 0.82f);

            var view = row.GetComponent<LobbyRoomRow>();
            view.Capture(name, occupancy, lockIcon, join);
            return row;
        }

        private static RectTransform CreateScroll(Transform parent, out RectTransform content)
        {
            var scrollObject = new GameObject("Scroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            scrollObject.layer = parent.gameObject.layer;
            scrollObject.transform.SetParent(parent, false);
            scrollObject.GetComponent<Image>().color = UIThemeConfig.PanelBg;
            var scroll = scrollObject.GetComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            viewport.layer = parent.gameObject.layer;
            viewport.transform.SetParent(scrollObject.transform, false);
            Stretch((RectTransform)viewport.transform);
            viewport.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.02f);
            viewport.GetComponent<Mask>().showMaskGraphic = false;

            var contentObject = new GameObject("Content", typeof(RectTransform));
            contentObject.layer = parent.gameObject.layer;
            contentObject.transform.SetParent(viewport.transform, false);
            content = contentObject.GetComponent<RectTransform>();
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;

            scroll.viewport = (RectTransform)viewport.transform;
            scroll.content = content;
            return (RectTransform)scrollObject.transform;
        }

        private static TMP_InputField CreateTmpInput(Transform parent, string name, string placeholder)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
            root.layer = parent.gameObject.layer;
            root.transform.SetParent(parent, false);
            root.GetComponent<Image>().color = new Color(0.96f, 0.94f, 0.88f, 1f);

            var text = new GameObject("Text", typeof(RectTransform));
            text.layer = root.layer;
            text.transform.SetParent(root.transform, false);
            var textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(16f, 8f);
            textRect.offsetMax = new Vector2(-16f, -8f);
            var tmp = text.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = 28f;
            tmp.color = new Color(0.1f, 0.12f, 0.1f, 1f);
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.raycastTarget = false;
            var font = Resources.Load("TMP Settings") != null ? TMP_Settings.defaultFontAsset : null;
            if (font != null)
                tmp.font = font;

            var hintObject = new GameObject("Placeholder", typeof(RectTransform));
            hintObject.layer = root.layer;
            hintObject.transform.SetParent(root.transform, false);
            var hintRect = hintObject.GetComponent<RectTransform>();
            hintRect.anchorMin = Vector2.zero;
            hintRect.anchorMax = Vector2.one;
            hintRect.offsetMin = new Vector2(16f, 8f);
            hintRect.offsetMax = new Vector2(-16f, -8f);
            var hint = hintObject.AddComponent<TextMeshProUGUI>();
            hint.text = placeholder;
            hint.fontSize = 28f;
            hint.fontStyle = FontStyles.Italic;
            hint.color = new Color(0.35f, 0.38f, 0.34f, 0.8f);
            hint.alignment = TextAlignmentOptions.MidlineLeft;
            hint.raycastTarget = false;
            if (font != null)
                hint.font = font;

            var field = root.GetComponent<TMP_InputField>();
            field.textViewport = textRect;
            field.textComponent = tmp;
            field.placeholder = hint;
            field.lineType = TMP_InputField.LineType.SingleLine;
            return field;
        }

        private static Toggle CreateToggle(Transform parent, string name, string caption, float xMin, float yMin, float xMax, float yMax, bool on)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Toggle));
            root.layer = parent.gameObject.layer;
            root.transform.SetParent(parent, false);
            UiFactory.Place(root.GetComponent<RectTransform>(), xMin, yMin, xMax, yMax);
            var hit = root.GetComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0.08f);
            hit.raycastTarget = true;

            var box = new GameObject("Box", typeof(RectTransform), typeof(Image));
            box.layer = root.layer;
            box.transform.SetParent(root.transform, false);
            var boxRect = box.GetComponent<RectTransform>();
            boxRect.anchorMin = new Vector2(0f, 0.5f);
            boxRect.anchorMax = new Vector2(0f, 0.5f);
            boxRect.pivot = new Vector2(0f, 0.5f);
            boxRect.sizeDelta = new Vector2(36f, 36f);
            boxRect.anchoredPosition = new Vector2(8f, 0f);
            var boxImage = box.GetComponent<Image>();
            boxImage.raycastTarget = false;

            var mark = new GameObject("Check", typeof(RectTransform), typeof(Image));
            mark.layer = root.layer;
            mark.transform.SetParent(box.transform, false);
            var markRect = mark.GetComponent<RectTransform>();
            markRect.anchorMin = new Vector2(0.22f, 0.22f);
            markRect.anchorMax = new Vector2(0.78f, 0.78f);
            markRect.offsetMin = Vector2.zero;
            markRect.offsetMax = Vector2.zero;
            markRect.localScale = Vector3.one;
            var markImage = mark.GetComponent<Image>();
            markImage.color = new Color(0.93f, 0.96f, 0.9f, 1f);
            markImage.raycastTarget = false;

            var label = UiFactory.CreateLabel(root.transform, "Caption", caption, 22, TextAnchor.MiddleLeft);
            var labelRect = label.rectTransform;
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.offsetMin = new Vector2(52f, 0f);
            labelRect.offsetMax = new Vector2(-8f, 0f);
            labelRect.localScale = Vector3.one;

            var toggle = root.GetComponent<Toggle>();
            toggle.targetGraphic = hit;
            toggle.graphic = null;
            toggle.toggleTransition = Toggle.ToggleTransition.None;
            toggle.onValueChanged.AddListener(value => ApplyToggleMark(boxImage, mark, value));
            toggle.SetIsOnWithoutNotify(on);
            ApplyToggleMark(boxImage, mark, on);
            return toggle;
        }

        private static void ApplyToggleMark(Image box, GameObject mark, bool on)
        {
            if (box != null)
                box.color = on ? new Color(0.16f, 0.45f, 0.28f, 1f) : new Color(0.93f, 0.9f, 0.82f, 1f);
            if (mark != null)
                mark.SetActive(on);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
        }
    }

    public sealed class LobbyRoomRow : MonoBehaviour
    {
        [SerializeField] private Text roomNameLabel;
        [SerializeField] private Text occupancyLabel;
        [SerializeField] private GameObject lockIcon;
        [SerializeField] private Button joinButton;

        private Action _join;

        public void Capture(Text nameLabel, Text occupancy, GameObject lockObject, Button join)
        {
            roomNameLabel = nameLabel;
            occupancyLabel = occupancy;
            lockIcon = lockObject;
            joinButton = join;
        }

        public void Bind(LobbyRoomDto room, Action join)
        {
            _join = join;
            if (roomNameLabel != null)
                roomNameLabel.text = room != null && !string.IsNullOrWhiteSpace(room.RoomName)
                    ? room.RoomName
                    : GameText.Get("ui.lobby.room_default");
            if (occupancyLabel != null)
            {
                var current = room != null ? room.CurrentPlayersCount : 0;
                var max = room != null ? room.MaxPlayers : 0;
                occupancyLabel.text = current.ToString(CultureInfo.InvariantCulture) + "/" + max.ToString(CultureInfo.InvariantCulture);
            }

            if (lockIcon != null)
                lockIcon.SetActive(room != null && room.HasPassword);
            if (joinButton != null)
            {
                joinButton.onClick.RemoveListener(ClickJoin);
                joinButton.onClick.AddListener(ClickJoin);
            }
        }

        private void ClickJoin()
        {
            _join?.Invoke();
        }
    }
}
