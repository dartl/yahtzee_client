using UnityEngine;
using UnityEngine.UI;
using Yahtzee.Audio;
using Yahtzee.Core;
using Yahtzee.Localization;

namespace Yahtzee.UI
{
    public sealed class MainMenuScreen : UIScreen
    {
        public override ScreenType Type => ScreenType.MainMenu;

        [SerializeField] private Button quickMatchButton;
        [SerializeField] private Button rankedMatchButton;
        [SerializeField] private Button tournamentButton;
        [SerializeField] private Button leaderboardButton;
        [SerializeField] private Button customLobbyButton;
        [SerializeField] private Button beginnerBotButton;
        [SerializeField] private Button professionalBotButton;
        [SerializeField] private Button passAndPlayButton;
        [SerializeField] private Button localTableButton;
        [SerializeField] private Sprite menuBackdropSprite;

        public Sprite MenuBackdropSprite => menuBackdropSprite;

        private Button _offlineButton;
        private Button _settingsButton;
        private Button _shopButton;
        private Button _customButton;
#if UNITY_STANDALONE || UNITY_ANDROID
        private Button _quitButton;
#endif
        private bool _menuLocked;
        private bool _onlineAvailable = true;
        private bool _shopAvailable = true;


        private bool _quickMatchBusy;

        private void Awake()
        {
            UiFactory.PrepareScreen((RectTransform)transform, true);
            EnsureBackdrop();
            GameplayScreen.ApplyGameplayCamera();
            ApplyGameTitle();
            EnsureOnlineButton();
            EnsureOfflineButton();
            EnsureStubButtons();
            StyleMenuChrome();
            ArrangeMainButtons();
            EnsureMenuController();
        }

        private void EnsureOnlineButton()
        {
            if (quickMatchButton != null)
                return;

            quickMatchButton = UiFactory.CreateButton(transform, GameText.Get("ui.menu.online"));
        }

        private void EnsureOfflineButton()
        {
            if (_offlineButton != null)
                return;

            _offlineButton = UiFactory.CreateButton(transform, GameText.Get("ui.menu.offline"));
            UiFactory.StyleGlass(_offlineButton);
        }

        private void EnsureStubButtons()
        {
            if (_settingsButton == null)
            {
                _settingsButton = UiFactory.CreateButton(transform, GameText.Get("ui.menu.settings"));
                UiFactory.StyleGlass(_settingsButton);
            }

            if (_shopButton == null)
            {
                _shopButton = UiFactory.CreateButton(transform, GameText.Get("ui.menu.shop"));
                UiFactory.StyleGlass(_shopButton);
            }

            if (_customButton == null)
            {
                _customButton = UiFactory.CreateButton(transform, GameText.Get("ui.menu.customization"));
                UiFactory.StyleGlass(_customButton);
            }
        }

        private void EnsureMenuController()
        {
            if (GetComponent<MainMenuController>() == null)
                gameObject.AddComponent<MainMenuController>();
        }

        public void SetOnlinePlayAvailable(bool available)
        {
            _onlineAvailable = available;
            ApplyButtonLocks();
        }

        public void SetShopAvailable(bool available)
        {
            _shopAvailable = available;
            ApplyButtonLocks();
        }

        public void SetMenuLocked(bool locked)
        {
            _menuLocked = locked;
            ApplyButtonLocks();
        }

        private void ApplyButtonLocks()
        {
            var localPlay = !_menuLocked;
            var onlinePlay = localPlay && _onlineAvailable;
            if (quickMatchButton != null)
                quickMatchButton.interactable = onlinePlay && !_quickMatchBusy;
            if (rankedMatchButton != null)
                rankedMatchButton.interactable = onlinePlay && !_quickMatchBusy;
            if (tournamentButton != null)
                tournamentButton.interactable = onlinePlay && !_quickMatchBusy;
            if (leaderboardButton != null)
                leaderboardButton.interactable = onlinePlay;
            if (customLobbyButton != null)
                customLobbyButton.interactable = onlinePlay;
            if (_offlineButton != null)
                _offlineButton.interactable = localPlay;
            if (_shopButton != null)
                _shopButton.interactable = localPlay && _shopAvailable;
        }

        private void OnEnable()
        {
            GameplayScreen.ApplyGameplayCamera();
            if (quickMatchButton != null)
                quickMatchButton.onClick.AddListener(OpenOnlineMenu);
            if (_offlineButton != null)
                _offlineButton.onClick.AddListener(OpenOfflineMenu);
            if (_settingsButton != null)
                _settingsButton.onClick.AddListener(OpenSettings);
            if (_shopButton != null)
                _shopButton.onClick.AddListener(OpenShop);
            if (_customButton != null)
                _customButton.onClick.AddListener(OpenCustomization);
#if UNITY_STANDALONE || UNITY_ANDROID
            if (_quitButton != null)
                _quitButton.onClick.AddListener(QuitGame);
#endif
        }

        private void OnRectTransformDimensionsChange()
        {
            if (!isActiveAndEnabled)
                return;

            GameplayScreen.ApplyGameplayCamera();
        }

        private void EnsureBackdrop()
        {
            UiFactory.ApplyMenuBackdrop((RectTransform)transform, menuBackdropSprite);
        }

        private void OnDisable()
        {
            if (quickMatchButton != null)
                quickMatchButton.onClick.RemoveListener(OpenOnlineMenu);
            if (_offlineButton != null)
                _offlineButton.onClick.RemoveListener(OpenOfflineMenu);
            if (_settingsButton != null)
                _settingsButton.onClick.RemoveListener(OpenSettings);
            if (_shopButton != null)
                _shopButton.onClick.RemoveListener(OpenShop);
            if (_customButton != null)
                _customButton.onClick.RemoveListener(OpenCustomization);
#if UNITY_STANDALONE || UNITY_ANDROID
            if (_quitButton != null)
                _quitButton.onClick.RemoveListener(QuitGame);
#endif
        }

        private void OpenOnlineMenu()
        {
            if (_menuLocked || !_onlineAvailable || UIManager.Instance == null)
                return;

            UIManager.Instance.SwitchToScreen(ScreenType.OnlineMenu);
        }

        private void OpenOfflineMenu()
        {
            if (_menuLocked || UIManager.Instance == null)
                return;

            UIManager.Instance.SwitchToScreen(ScreenType.OfflineMenu);
        }

        private void OpenSettings()
        {
            if (_menuLocked || UIManager.Instance == null)
                return;

            GameAudio.PlayUiClick();
            UIManager.Instance.SwitchToScreen(ScreenType.Settings);
        }

        private void OpenShop()
        {
            if (_menuLocked || !_shopAvailable || UIManager.Instance == null)
                return;

            GameAudio.PlayUiClick();
            UIManager.Instance.SwitchToScreen(ScreenType.Shop);
        }

        private void OpenCustomization()
        {
            if (_menuLocked || UIManager.Instance == null)
                return;

            GameAudio.PlayUiClick();
            UIManager.Instance.SwitchToScreen(ScreenType.Customization);
        }

        private void ApplyGameTitle()
        {
            var existing = transform.Find("Title");
            if (existing != null)
                Destroy(existing.gameObject);
            var rule = transform.Find("TitleRule");
            if (rule != null)
                Destroy(rule.gameObject);
            var subtitle = transform.Find("Subtitle");
            if (subtitle != null)
                Destroy(subtitle.gameObject);

            var title = UiFactory.CreateScreenHeader(
                transform,
                YahtzeeConfig.Current.GameName,
                GameText.Get("ui.menu.subtitle"));
            title.resizeTextMaxSize = 56;
            UiFactory.Place(title.rectTransform, 0.1f, 0.8f, 0.9f, 0.9f);
            var goldRule = transform.Find("TitleRule");
            if (goldRule != null)
                UiFactory.Place(goldRule.GetComponent<RectTransform>(), 0.38f, 0.788f, 0.62f, 0.798f);
            var sub = transform.Find("Subtitle");
            if (sub != null)
                UiFactory.Place(sub.GetComponent<RectTransform>(), 0.14f, 0.74f, 0.86f, 0.788f);
            UiMotion.PopIn(title.transform);
        }

        private void StyleMenuChrome()
        {
            // No full-menu underlay card — buttons carry their own glass style.
            var staleCard = transform.Find("MenuCard");
            if (staleCard != null)
                Destroy(staleCard.gameObject);

            UiFactory.StyleAccent(quickMatchButton);
            UiFactory.StyleGlass(_offlineButton);
            UiFactory.StyleGlass(_settingsButton);
            UiFactory.StyleGlass(_shopButton);
            UiFactory.StyleGlass(_customButton);
        }

        private void ArrangeMainButtons()
        {
            PlaceCaption(quickMatchButton, GameText.Get("ui.menu.online"), 0.16f, 0.58f, 0.84f, 0.68f);
            PlaceCaption(_offlineButton, GameText.Get("ui.menu.offline"), 0.16f, 0.46f, 0.84f, 0.56f);
            PlaceCaption(_settingsButton, GameText.Get("ui.menu.settings"), 0.16f, 0.36f, 0.84f, 0.44f);
            PlaceCaption(_shopButton, GameText.Get("ui.menu.shop"), 0.16f, 0.26f, 0.84f, 0.34f);
            PlaceCaption(_customButton, GameText.Get("ui.menu.customization"), 0.16f, 0.16f, 0.84f, 0.24f);
            UiFactory.AttachMenuIcon(quickMatchButton, "online");
            UiFactory.AttachMenuIcon(_offlineButton, "offline");
            UiFactory.AttachMenuIcon(_settingsButton, "settings");
            UiFactory.AttachMenuIcon(_shopButton, "shop");
            UiFactory.AttachMenuIcon(_customButton, "custom");
            EnsureQuitButton();
            HideButton(rankedMatchButton);
            HideButton(tournamentButton);
            HideButton(leaderboardButton);
            HideButton(customLobbyButton);
            HideButton(beginnerBotButton);
            HideButton(professionalBotButton);
            HideButton(passAndPlayButton);
            HideButton(localTableButton);
        }

        private static void PlaceCaption(Button button, string caption, float xMin, float yMin, float xMax, float yMax)
        {
            if (button == null)
                return;

            button.gameObject.SetActive(true);
            UiFactory.Place(button.GetComponent<RectTransform>(), xMin, yMin, xMax, yMax);
            var label = button.GetComponentInChildren<Text>();
            if (label != null)
                label.text = caption;
        }

        private void EnsureQuitButton()
        {
#if UNITY_STANDALONE || UNITY_ANDROID
            if (_quitButton == null)
            {
                _quitButton = UiFactory.CreateButton(transform, GameText.Get("ui.menu.quit"));
                UiFactory.StyleDestructive(_quitButton);
            }

            _quitButton.gameObject.SetActive(true);
            UiFactory.StyleDestructive(_quitButton);
            UiFactory.Place(_quitButton.GetComponent<RectTransform>(), 0.28f, 0.03f, 0.72f, 0.11f);
            var label = _quitButton.GetComponentInChildren<Text>(true);
            if (label != null)
                label.text = GameText.Get("ui.menu.quit");
            _quitButton.transform.SetAsLastSibling();
#endif
        }

#if UNITY_STANDALONE || UNITY_ANDROID
        private static void QuitGame()
        {
            Application.Quit();
        }
#endif

        private static void HideButton(Button button)
        {
            if (button != null)
                button.gameObject.SetActive(false);
        }
    }
}

