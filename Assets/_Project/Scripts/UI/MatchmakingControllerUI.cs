using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Yahtzee.Core;
using Yahtzee.Localization;
using Yahtzee.Network;

namespace Yahtzee.UI
{
    public sealed class MatchmakingControllerUI : UIScreen
    {
        public override ScreenType Type => ScreenType.Matchmaking;

        [SerializeField] private TextMeshProUGUI timerText;
        [SerializeField] private TextMeshProUGUI estimatedTimeText;
        [SerializeField] private Button cancelSearchButton;

        private CancellationTokenSource _loops;
        private int _elapsedSeconds;
        private int _session;
        private bool _pollInFlight;
        private bool _enteringMatch;
        private bool _accepting;
        private bool _wired;
        private int _queueMode;
        private bool _patienceShown;
        private GameObject _patiencePanel;
        private Button _continueWaitButton;
        private Button _leaveMenuButton;
        private TextMeshProUGUI _titleText;
        private TextMeshProUGUI _queueCountText;

        private void Awake()
        {
            UiFactory.ApplyOpaqueMenuBackground((RectTransform)transform);
            EnsureWidgets();
        }

        public void Prepare(bool ranked)
        {
            _queueMode = ranked ? 1 : 0;
        }

        public void PrepareTournament()
        {
            _queueMode = 2;
        }

        public override void Show()
        {
            UiFactory.EnsureScreenFillsCanvas((RectTransform)transform);
            UiFactory.ApplyOpaqueMenuBackground((RectTransform)transform);
            base.Show();
            StartQueueSearch();
        }

        public override void Hide()
        {
            StopSearch();
            base.Hide();
        }

        private void OnEnable()
        {
            NetworkManager.OnQuickMatchUpdated += HandleQuickMatchUpdated;
            if (_wired || cancelSearchButton == null)
                return;

            cancelSearchButton.onClick.AddListener(OnCancelClicked);
            if (_continueWaitButton != null)
                _continueWaitButton.onClick.AddListener(HidePatiencePopup);
            if (_leaveMenuButton != null)
                _leaveMenuButton.onClick.AddListener(OnCancelClicked);
            _wired = true;
        }

        private void OnDisable()
        {
            NetworkManager.OnQuickMatchUpdated -= HandleQuickMatchUpdated;
            if (!_wired || cancelSearchButton == null)
                return;

            cancelSearchButton.onClick.RemoveListener(OnCancelClicked);
            if (_continueWaitButton != null)
                _continueWaitButton.onClick.RemoveListener(HidePatiencePopup);
            if (_leaveMenuButton != null)
                _leaveMenuButton.onClick.RemoveListener(OnCancelClicked);
            _wired = false;
        }

        private void OnDestroy()
        {
            StopLoops();
        }

        public async void StartQueueSearch()
        {
            var session = ++_session;
            StopLoops();
            _enteringMatch = false;
            _accepting = true;
            _elapsedSeconds = 0;
            _patienceShown = false;
            HidePatiencePopup();
            EnsureWidgets();
            if (_titleText == null)
            {
                var title = transform.Find("Title");
                if (title != null)
                    _titleText = title.GetComponent<TextMeshProUGUI>();
            }

            if (_titleText != null)
            {
                if (_queueMode == 1)
                    _titleText.text = "Рейтинговый поиск";
                else if (_queueMode == 2)
                    _titleText.text = "Турнир";
                else
                    _titleText.text = "Быстрый поиск";
            }
            WriteTimer();
            WriteEstimate(null);
            WriteQueueCount(null);
            if (cancelSearchButton != null)
                cancelSearchButton.interactable = true;

            if (NetworkManager.Instance == null)
            {
                Debug.LogWarning("NetworkManager не найден. Поиск матча недоступен.");
                return;
            }

            _loops = new CancellationTokenSource();
            var token = _loops.Token;
            _ = RunTimer(session, token);

            try
            {
                var response = await JoinCurrentQueue();
                if (session != _session || this == null)
                    return;

                HandleQuickMatchUpdated(response);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (NetworkException exception)
            {
                if (exception.ErrorCode == "USER_BANNED" || session != _session || this == null)
                    return;

                Debug.LogWarning("Yahtzee queue: " + exception.Message);
            }
            catch (Exception exception)
            {
                if (session != _session || this == null)
                    return;

                Debug.LogWarning("Yahtzee queue: " + exception.Message);
            }

            if (session != _session || this == null || _enteringMatch)
                return;

            _ = RunPoll(session, token);
        }

        public void StopSearch()
        {
            _accepting = false;
            _session++;
            StopLoops();
        }

        private async Task RunTimer(int session, CancellationToken token)
        {
            while (!token.IsCancellationRequested && session == _session)
            {
                try
                {
                    await Task.Delay(1000, token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                if (session != _session || this == null)
                    return;

                _elapsedSeconds++;
                WriteTimer();
                if (_elapsedSeconds == 30)
                    ShowPatiencePopup();
            }
        }

        private async Task RunPoll(int session, CancellationToken token)
        {
            var delay = TimeSpan.FromSeconds(YahtzeeConfig.Current.MatchmakingPollSeconds);
            while (!token.IsCancellationRequested && session == _session && !_enteringMatch)
            {
                try
                {
                    await Task.Delay(delay, token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                if (session != _session || this == null || _enteringMatch || NetworkManager.Instance == null)
                    return;

                _pollInFlight = true;
                try
                {
                    var response = await JoinCurrentQueue();
                    if (session == _session && this != null)
                        HandleQuickMatchUpdated(response);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (NetworkException exception)
                {
                    if (exception.ErrorCode == "USER_BANNED")
                        return;

                    Debug.LogWarning("Yahtzee queue poll: " + exception.Message);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("Yahtzee queue poll: " + exception.Message);
                }
                finally
                {
                    _pollInFlight = false;
                }
            }
        }

        private void HandleQuickMatchUpdated(MatchmakingResponse response)
        {
            if (!_accepting || response == null || _enteringMatch)
                return;

            WriteEstimate(response);
            WriteQueueCount(response);
            if (response.Status == "matched" && response.Match != null)
                _ = EnterMatch(response.Match.Id);
        }

        private async Task EnterMatch(int matchId)
        {
            if (_enteringMatch || NetworkManager.Instance == null || matchId <= 0)
                return;

            _enteringMatch = true;
            _accepting = false;
            _session++;
            StopLoops();
            HidePatiencePopup();
            if (cancelSearchButton != null)
                cancelSearchButton.interactable = false;
            if (estimatedTimeText != null)
                estimatedTimeText.text = GameText.Get("ui.matchmaking.searching");

            var matchKey = matchId.ToString(CultureInfo.InvariantCulture);
            try
            {
                await NetworkManager.Instance.ConnectToMatchWebSocket(matchKey);
                // Snapshot turn timer before Gameplay opens (MatchStarted may already have fired).
                await NetworkManager.Instance.SendReconnectRequest(matchKey);
                if (this == null)
                    return;

                var gameplay = GameplayScreen.Instance;
                if (gameplay == null)
                    gameplay = FindAnyObjectByType<GameplayScreen>(FindObjectsInactive.Include);
                if (gameplay != null)
                    gameplay.InitializeMatchMode(true, matchKey);

                if (UIManager.Instance != null)
                    UIManager.Instance.SwitchToScreen(ScreenType.Gameplay);
            }
            catch (Exception exception)
            {
                _enteringMatch = false;
                Debug.LogWarning("Yahtzee match join: " + exception.Message);
                if (this == null || !isActiveAndEnabled)
                    return;

                if (estimatedTimeText != null)
                    estimatedTimeText.text = GameText.FromException(exception);
                if (cancelSearchButton != null)
                    cancelSearchButton.interactable = true;

                var session = _session;
                _ = RetryEnter(matchId, session);
            }
        }

        private async Task RetryEnter(int matchId, int session)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(YahtzeeConfig.Current.MatchmakingPollSeconds));
            }
            catch (Exception)
            {
                return;
            }

            if (session != _session || this == null || !isActiveAndEnabled || _enteringMatch)
                return;

            await EnterMatch(matchId);
        }

        private async void OnCancelClicked()
        {
            if (_enteringMatch)
                return;

            var session = ++_session;
            _accepting = false;
            StopLoops();
            if (cancelSearchButton != null)
                cancelSearchButton.interactable = false;

            while (_pollInFlight && session == _session)
                await Task.Yield();

            if (session != _session || this == null)
                return;

            try
            {
                if (NetworkManager.Instance != null)
                {
                    if (_queueMode == 2)
                        await NetworkManager.Instance.LeaveTournamentQueue();
                    else
                        await NetworkManager.Instance.LeaveQuickMatch();
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Yahtzee leave queue: " + exception.Message);
            }

            if (cancelSearchButton != null)
                cancelSearchButton.interactable = true;

            if (session == _session && UIManager.Instance != null)
                UIManager.Instance.SwitchToScreen(ScreenType.OnlineMenu);
        }

        private void StopLoops()
        {
            if (_loops == null)
                return;

            _loops.Cancel();
            _loops.Dispose();
            _loops = null;
        }

        private Task<MatchmakingResponse> JoinCurrentQueue()
        {
            if (_queueMode == 1)
                return NetworkManager.Instance.JoinRankedMatch();
            if (_queueMode == 2)
                return NetworkManager.Instance.JoinTournamentQueue();

            return NetworkManager.Instance.JoinQuickMatch();
        }

        private void WriteTimer()
        {
            if (timerText == null)
                return;

            var minutes = _elapsedSeconds / 60;
            var seconds = _elapsedSeconds % 60;
            timerText.text = minutes.ToString("00", CultureInfo.InvariantCulture)
                + ":"
                + seconds.ToString("00", CultureInfo.InvariantCulture);
        }

        private void WriteEstimate(MatchmakingResponse response)
        {
            if (estimatedTimeText == null)
                return;

            if (response == null || !response.EstimatedWaitTime.HasValue)
            {
                estimatedTimeText.text = "Примерное время ожидания: …";
                return;
            }

            estimatedTimeText.text = "Примерное время ожидания: "
                + response.EstimatedWaitTime.Value.ToString(CultureInfo.InvariantCulture)
                + " сек";
        }

        private void WriteQueueCount(MatchmakingResponse response)
        {
            EnsureQueueCountLabel();
            if (_queueCountText == null)
                return;

            if (response != null && string.Equals(response.Status, "matched", StringComparison.OrdinalIgnoreCase))
            {
                _queueCountText.text = string.Empty;
                return;
            }

            var count = response != null && response.QueueCount.HasValue
                ? Mathf.Max(1, response.QueueCount.Value)
                : 1;
            _queueCountText.text = "сейчас в поиске игры ~"
                + count.ToString(CultureInfo.InvariantCulture);
        }

        private void EnsureWidgets()
        {
            if (timerText == null)
            {
                _titleText = UiFactory.CreateAutoLabel(transform, "Title", "Быстрый поиск", TextAlignmentOptions.Center);
                UiFactory.Place(_titleText.rectTransform, 0.12f, 0.72f, 0.88f, 0.88f);

                timerText = UiFactory.CreateAutoLabel(transform, "Timer", "00:00", TextAlignmentOptions.Center);
                UiFactory.Place(timerText.rectTransform, 0.2f, 0.52f, 0.8f, 0.68f);
            }

            if (estimatedTimeText == null)
            {
                estimatedTimeText = UiFactory.CreateAutoLabel(
                    transform,
                    "Estimate",
                    "Примерное время ожидания: …",
                    TextAlignmentOptions.Center);
                UiFactory.Place(estimatedTimeText.rectTransform, 0.08f, 0.34f, 0.92f, 0.48f);
            }

            EnsureQueueCountLabel();

            if (cancelSearchButton == null)
            {
                cancelSearchButton = UiFactory.CreateButton(transform, "Отмена");
                UiFactory.Place(cancelSearchButton.GetComponent<RectTransform>(), 0.28f, 0.08f, 0.72f, 0.2f);
                if (isActiveAndEnabled && !_wired)
                {
                    cancelSearchButton.onClick.AddListener(OnCancelClicked);
                    _wired = true;
                }
            }

            EnsurePatiencePopup();
        }

        private void EnsureQueueCountLabel()
        {
            if (_queueCountText != null)
                return;

            var existing = transform.Find("QueueCount");
            if (existing != null)
                _queueCountText = existing.GetComponent<TextMeshProUGUI>();

            if (_queueCountText == null)
            {
                _queueCountText = UiFactory.CreateAutoLabel(
                    transform,
                    "QueueCount",
                    "сейчас в поиске игры ~1",
                    TextAlignmentOptions.TopRight);
            }

            _queueCountText.fontSizeMax = 20f;
            _queueCountText.fontSizeMin = 12f;
            _queueCountText.enableWordWrapping = true;
            _queueCountText.overflowMode = TextOverflowModes.Ellipsis;
            _queueCountText.color = new Color(0.35f, 0.4f, 0.48f, 1f);
            UiFactory.Place(_queueCountText.rectTransform, 0.52f, 0.88f, 0.97f, 0.97f);
            _queueCountText.transform.SetAsLastSibling();
        }

        private void EnsurePatiencePopup()
        {
            if (_patiencePanel != null)
                return;

            _patiencePanel = new GameObject("PatiencePopup", typeof(RectTransform), typeof(Image));
            _patiencePanel.layer = gameObject.layer;
            _patiencePanel.transform.SetParent(transform, false);
            _patiencePanel.transform.SetAsLastSibling();
            var image = _patiencePanel.GetComponent<Image>();
            image.color = UIThemeConfig.PanelBgSolid;
            image.raycastTarget = true;
            var rect = _patiencePanel.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var message = UiFactory.CreateAutoLabel(
                _patiencePanel.transform,
                "Message",
                "Пока что не удается найти игрока с равным навыком игры, пожалуйста, ожидайте или попробуйте позже.",
                TextAlignmentOptions.Center);
            message.enableWordWrapping = true;
            message.overflowMode = TextOverflowModes.Overflow;
            UiFactory.Place(message.rectTransform, 0.08f, 0.48f, 0.92f, 0.82f);

            _continueWaitButton = UiFactory.CreateButton(_patiencePanel.transform, "Продолжить ожидать");
            UiFactory.StyleAccent(_continueWaitButton);
            UiFactory.Place(_continueWaitButton.GetComponent<RectTransform>(), 0.18f, 0.28f, 0.82f, 0.4f);
            _leaveMenuButton = UiFactory.CreateButton(_patiencePanel.transform, "Назад");
            UiFactory.StyleGlass(_leaveMenuButton);
            UiFactory.Place(_leaveMenuButton.GetComponent<RectTransform>(), 0.18f, 0.12f, 0.82f, 0.24f);

            if (isActiveAndEnabled && _wired)
            {
                _continueWaitButton.onClick.AddListener(HidePatiencePopup);
                _leaveMenuButton.onClick.AddListener(OnCancelClicked);
            }

            _patiencePanel.SetActive(false);
        }

        private void ShowPatiencePopup()
        {
            if (_patienceShown || _enteringMatch)
                return;

            EnsurePatiencePopup();
            _patienceShown = true;
            if (_patiencePanel != null)
            {
                _patiencePanel.transform.SetAsLastSibling();
                _patiencePanel.SetActive(true);
            }
        }

        private void HidePatiencePopup()
        {
            if (_patiencePanel != null)
                _patiencePanel.SetActive(false);
        }
    }
}
