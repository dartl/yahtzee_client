using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Yahtzee.Core;
using Yahtzee.Localization;
using Yahtzee.Network;
using Yahtzee.UI;

namespace Yahtzee.Feedback
{
    /// <summary>
    /// Root-canvas overlay: toasts, persistent banner, blocking/modal service states.
    /// </summary>
    public sealed class FeedbackOverlay : MonoBehaviour
    {
        public static FeedbackOverlay Instance { get; private set; }

        private readonly Queue<string> _toasts = new Queue<string>();
        private GameObject _bannerRoot;
        private Text _bannerLabel;
        private GameObject _blockRoot;
        private Text _blockTitle;
        private Text _blockBody;
        private Button _blockRetry;
        private Text _blockRetryLabel;
        private GameObject _toastRoot;
        private Text _toastLabel;
        private float _toastUntil;
        private float _retryCountdown;
        private Action _retryAction;
        private bool _localeHooked;

        public static FeedbackOverlay Ensure(Transform canvasRoot)
        {
            if (Instance != null)
                return Instance;

            var existing = canvasRoot != null
                ? canvasRoot.GetComponentInChildren<FeedbackOverlay>(true)
                : FindAnyObjectByType<FeedbackOverlay>(FindObjectsInactive.Include);
            if (existing != null)
            {
                Instance = existing;
                existing.BuildIfNeeded();
                existing.Hook();
                return existing;
            }

            var go = new GameObject("FeedbackOverlay", typeof(RectTransform), typeof(FeedbackOverlay));
            go.layer = canvasRoot != null ? canvasRoot.gameObject.layer : 5;
            go.transform.SetParent(canvasRoot, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            Instance = go.GetComponent<FeedbackOverlay>();
            Instance.BuildIfNeeded();
            Instance.Hook();
            return Instance;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            BuildIfNeeded();
            Hook();
        }

        private void OnDestroy()
        {
            Unhook();
            if (Instance == this)
                Instance = null;
        }

        private void Hook()
        {
            if (_localeHooked)
                return;

            PlayerFeedback.OnEvent += HandleEvent;
            PlayerFeedback.OnServiceStatusChanged += HandleService;
            GameLocale.OnChanged += RefreshCopy;
            NetworkManager.OnConnectionLost += HandleConnectionLost;
            NetworkManager.OnConnectionRestored += HandleConnectionRestored;
            _localeHooked = true;
        }

        private void Unhook()
        {
            if (!_localeHooked)
                return;

            PlayerFeedback.OnEvent -= HandleEvent;
            PlayerFeedback.OnServiceStatusChanged -= HandleService;
            GameLocale.OnChanged -= RefreshCopy;
            NetworkManager.OnConnectionLost -= HandleConnectionLost;
            NetworkManager.OnConnectionRestored -= HandleConnectionRestored;
            _localeHooked = false;
        }

        private void Update()
        {
            if (_toastRoot != null && _toastRoot.activeSelf && Time.unscaledTime >= _toastUntil)
            {
                _toastRoot.SetActive(false);
                ShowNextToast();
            }

            if (_blockRetryLabel == null || _retryCountdown <= 0f)
                return;

            _retryCountdown -= Time.unscaledDeltaTime;
            if (_retryCountdown <= 0f)
            {
                _retryCountdown = 0f;
                if (_blockRetry != null)
                    _blockRetry.interactable = true;
                if (_blockRetryLabel != null)
                    _blockRetryLabel.text = GameText.Get("service.retry");
                return;
            }

            if (_blockRetryLabel != null)
                _blockRetryLabel.text = GameText.Format("service.retry_in", Mathf.CeilToInt(_retryCountdown));
        }

        public void SetRetryAction(Action action)
        {
            _retryAction = action;
        }

        private void HandleEvent(FeedbackEvent feedback)
        {
            switch (feedback.Severity)
            {
                case FeedbackSeverity.Toast:
                    EnqueueToast(feedback.Message);
                    break;
                case FeedbackSeverity.Banner:
                    ShowBanner(feedback.Message);
                    break;
                case FeedbackSeverity.Modal:
                case FeedbackSeverity.Blocking:
                    ShowBlock(
                        TitleFor(feedback.Code, feedback.Category),
                        feedback.Message,
                        feedback.Retryable,
                        feedback.RetryAfterSeconds);
                    break;
            }
        }

        private void HandleService(ServiceUiStatus status, string message, int? retryAfter)
        {
            if (status == ServiceUiStatus.Ok)
            {
                HideBlock();
                HideBanner();
                return;
            }

            if (status == ServiceUiStatus.Degraded)
            {
                ShowBanner(string.IsNullOrEmpty(message) ? GameText.Get("service.body_degraded") : message);
                return;
            }

            ShowBlock(
                TitleForStatus(status),
                string.IsNullOrEmpty(message) ? BodyForStatus(status) : message,
                status != ServiceUiStatus.Banned && status != ServiceUiStatus.VersionBlocked,
                retryAfter);
        }

        private void HandleConnectionLost()
        {
            PlayerFeedback.ReportCode(ApiErrorCode.SocketClosed);
        }

        private void HandleConnectionRestored()
        {
            if (PlayerFeedback.ServiceStatus == ServiceUiStatus.Offline
                || PlayerFeedback.ServiceStatus == ServiceUiStatus.Degraded)
            {
                PlayerFeedback.ClearServiceStatus();
            }

            HideBanner();
        }

        private void RefreshCopy()
        {
            if (_bannerRoot != null && _bannerRoot.activeSelf && _bannerLabel != null)
                _bannerLabel.text = GameText.Get("service.body_degraded");

            if (_blockRoot != null && _blockRoot.activeSelf)
                HandleService(PlayerFeedback.ServiceStatus, null, null);
        }

        private void EnqueueToast(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            _toasts.Enqueue(message);
            if (_toastRoot != null && _toastRoot.activeSelf)
                return;

            ShowNextToast();
        }

        private void ShowNextToast()
        {
            if (_toasts.Count == 0 || _toastRoot == null || _toastLabel == null)
                return;

            _toastLabel.text = _toasts.Dequeue();
            _toastRoot.SetActive(true);
            _toastRoot.transform.SetAsLastSibling();
            _toastUntil = Time.unscaledTime + 2.8f;
        }

        private void ShowBanner(string message)
        {
            if (_bannerRoot == null || _bannerLabel == null)
                return;

            _bannerLabel.text = message ?? string.Empty;
            _bannerRoot.SetActive(true);
            _bannerRoot.transform.SetAsLastSibling();
        }

        private void HideBanner()
        {
            if (_bannerRoot != null)
                _bannerRoot.SetActive(false);
        }

        private void ShowBlock(string title, string body, bool canRetry, int? retryAfter)
        {
            if (_blockRoot == null)
                return;

            if (_blockTitle != null)
                _blockTitle.text = title ?? string.Empty;
            if (_blockBody != null)
                _blockBody.text = body ?? string.Empty;

            if (_blockRetry != null)
            {
                _blockRetry.gameObject.SetActive(canRetry);
                _retryCountdown = retryAfter.HasValue ? Mathf.Max(0, retryAfter.Value) : 0f;
                _blockRetry.interactable = _retryCountdown <= 0f;
                if (_blockRetryLabel != null)
                {
                    _blockRetryLabel.text = _retryCountdown > 0f
                        ? GameText.Format("service.retry_in", Mathf.CeilToInt(_retryCountdown))
                        : GameText.Get("service.retry");
                }
            }

            _blockRoot.SetActive(true);
            _blockRoot.transform.SetAsLastSibling();
        }

        private void HideBlock()
        {
            if (_blockRoot != null)
                _blockRoot.SetActive(false);
            _retryCountdown = 0f;
        }

        private void OnRetryClicked()
        {
            if (_retryAction != null)
            {
                _retryAction.Invoke();
                return;
            }

            if (NetworkManager.Instance != null)
                _ = NetworkManager.Instance.RefreshServiceStatusAsync();
        }

        private static string TitleFor(string code, ApiErrorCategory category)
        {
            if (code == ApiErrorCode.UserBanned)
                return GameText.Get("service.title_banned");
            if (code == ApiErrorCode.VersionUnsupported)
                return GameText.Get("service.title_version");
            if (category == ApiErrorCategory.Transport)
                return GameText.Get("service.title_offline");
            return GameText.Get("service.title_maintenance");
        }

        private static string TitleForStatus(ServiceUiStatus status)
        {
            return status switch
            {
                ServiceUiStatus.Offline => GameText.Get("service.title_offline"),
                ServiceUiStatus.VersionBlocked => GameText.Get("service.title_version"),
                ServiceUiStatus.Banned => GameText.Get("service.title_banned"),
                ServiceUiStatus.Degraded => GameText.Get("service.title_degraded"),
                _ => GameText.Get("service.title_maintenance")
            };
        }

        private static string BodyForStatus(ServiceUiStatus status)
        {
            return status switch
            {
                ServiceUiStatus.Offline => GameText.Get("service.body_offline"),
                ServiceUiStatus.VersionBlocked => GameText.Get("service.body_version"),
                ServiceUiStatus.Degraded => GameText.Get("service.body_degraded"),
                _ => GameText.Get("service.body_maintenance")
            };
        }

        private void BuildIfNeeded()
        {
            if (_blockRoot != null)
                return;

            var root = (RectTransform)transform;
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            _bannerRoot = CreatePanel("Banner", new Color(0.118f, 0.161f, 0.231f, 0.92f));
            var bannerRect = _bannerRoot.GetComponent<RectTransform>();
            bannerRect.anchorMin = new Vector2(0.08f, 0.88f);
            bannerRect.anchorMax = new Vector2(0.92f, 0.97f);
            bannerRect.offsetMin = Vector2.zero;
            bannerRect.offsetMax = Vector2.zero;
            _bannerLabel = UiFactory.CreateLabel(_bannerRoot.transform, "BannerLabel", string.Empty, 22, TextAnchor.MiddleCenter);
            StretchLabel(_bannerLabel);
            _bannerRoot.SetActive(false);

            _toastRoot = CreatePanel("Toast", new Color(0.059f, 0.090f, 0.165f, 0.94f));
            var toastRect = _toastRoot.GetComponent<RectTransform>();
            toastRect.anchorMin = new Vector2(0.12f, 0.08f);
            toastRect.anchorMax = new Vector2(0.88f, 0.18f);
            toastRect.offsetMin = Vector2.zero;
            toastRect.offsetMax = Vector2.zero;
            _toastLabel = UiFactory.CreateLabel(_toastRoot.transform, "ToastLabel", string.Empty, 24, TextAnchor.MiddleCenter);
            StretchLabel(_toastLabel);
            _toastLabel.color = Color.white;
            _toastRoot.SetActive(false);

            _blockRoot = CreatePanel("Block", new Color(0.02f, 0.03f, 0.05f, 0.82f));
            var blockRect = _blockRoot.GetComponent<RectTransform>();
            blockRect.anchorMin = Vector2.zero;
            blockRect.anchorMax = Vector2.one;
            blockRect.offsetMin = Vector2.zero;
            blockRect.offsetMax = Vector2.zero;

            var card = CreatePanel("Card", UIThemeConfig.PanelBgSolid);
            card.transform.SetParent(_blockRoot.transform, false);
            var cardRect = card.GetComponent<RectTransform>();
            cardRect.anchorMin = new Vector2(0.12f, 0.28f);
            cardRect.anchorMax = new Vector2(0.88f, 0.72f);
            cardRect.offsetMin = Vector2.zero;
            cardRect.offsetMax = Vector2.zero;

            _blockTitle = UiFactory.CreateLabel(card.transform, "Title", string.Empty, 34, TextAnchor.MiddleCenter);
            Place(_blockTitle.rectTransform, 0.08f, 0.62f, 0.92f, 0.9f);
            _blockTitle.color = UIThemeConfig.TextPrimary;
            _blockTitle.fontStyle = FontStyle.Bold;

            _blockBody = UiFactory.CreateLabel(card.transform, "Body", string.Empty, 24, TextAnchor.MiddleCenter);
            Place(_blockBody.rectTransform, 0.08f, 0.28f, 0.92f, 0.62f);
            _blockBody.color = UIThemeConfig.TextMuted;
            _blockBody.horizontalOverflow = HorizontalWrapMode.Wrap;

            _blockRetry = UiFactory.CreateButton(card.transform, GameText.Get("service.retry"));
            UiFactory.StyleAccent(_blockRetry);
            Place(_blockRetry.GetComponent<RectTransform>(), 0.2f, 0.08f, 0.8f, 0.24f);
            _blockRetryLabel = _blockRetry.GetComponentInChildren<Text>();
            _blockRetry.onClick.AddListener(OnRetryClicked);

            _blockRoot.SetActive(false);
            transform.SetAsLastSibling();
        }

        private GameObject CreatePanel(string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.layer = gameObject.layer;
            go.transform.SetParent(transform, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = true;
            return go;
        }

        private static void StretchLabel(Text label)
        {
            if (label == null)
                return;

            var rect = label.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(16f, 8f);
            rect.offsetMax = new Vector2(-16f, -8f);
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.color = Color.white;
        }

        private static void Place(RectTransform rect, float x0, float y0, float x1, float y1)
        {
            if (rect == null)
                return;

            rect.anchorMin = new Vector2(x0, y0);
            rect.anchorMax = new Vector2(x1, y1);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
