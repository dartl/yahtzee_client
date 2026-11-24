using UnityEngine;
using UnityEngine.UI;
using Yahtzee.Audio;
using Yahtzee.Core;
using Yahtzee.Network;

namespace Yahtzee.UI
{
    public sealed class OfflineMenuScreen : UIScreen
    {
        public override ScreenType Type => ScreenType.OfflineMenu;

        private bool _built;
        private bool _layoutLandscape = true;
        private GameObject _passPanel;

        public override void Show()
        {
            base.Show();
            var rect = (RectTransform)transform;
            UiFactory.EnsureScreenFillsCanvas(rect);
            UiFactory.ApplyOpaqueMenuBackground(rect);
            UiFactory.FitCanvas(GetComponentInParent<Canvas>());

            var landscape = Screen.width > Screen.height;
            if (!_built || landscape != _layoutLandscape || rect.rect.width < 80f)
            {
                Rebuild();
                _built = true;
                _layoutLandscape = landscape;
            }

            if (_passPanel != null)
                _passPanel.SetActive(false);

            UiMotion.PopIn(transform);
        }

        private void OnRectTransformDimensionsChange()
        {
            if (!isActiveAndEnabled || !_built)
                return;

            var landscape = Screen.width > Screen.height;
            if (landscape == _layoutLandscape)
                return;

            var passOpen = _passPanel != null && _passPanel.activeSelf;
            Rebuild();
            _layoutLandscape = landscape;
            if (passOpen && _passPanel != null)
                _passPanel.SetActive(true);
        }

        private void Rebuild()
        {
            UiFactory.ClearContent(transform);
            UiFactory.ApplyOpaqueMenuBackground((RectTransform)transform);
            Build();
        }

        private void Build()
        {
            var content = UiFactory.BeginScrollForm(transform, out var scroll);

            UiFactory.AddMenuTitle(content, "Офлайн", "Бот, обучение, один телефон или локальная сеть");

            Wire(UiFactory.AddMenuAction(content, "Обучение (как играть?)", true), OpenTutorial);
            Wire(UiFactory.AddMenuAction(content, "Игра с ботом", false), OpenBot);
            Wire(UiFactory.AddMenuAction(content, "На одном телефоне", false), OpenPassPanel);
            Wire(UiFactory.AddMenuAction(content, "По локальной сети (Wi-Fi)", false), () => OpenTable(false));

            if (BluetoothNetworkManager.IsSupported)
                Wire(UiFactory.AddMenuAction(content, "Bluetooth", false), () => OpenTable(true));

            Wire(UiFactory.AddMenuAction(content, "Назад", false), ReturnToMenu);

            _passPanel = CreatePassPanel();
            UiFactory.FinalizeMenuForm(scroll);
        }

        private static void Wire(Button button, UnityEngine.Events.UnityAction action)
        {
            button.onClick.AddListener(GameAudio.PlayUiClick);
            button.onClick.AddListener(action);
        }

        private void OpenTutorial()
        {
            if (UIManager.Instance == null)
                return;

            GameplayScreen.PrepareTutorial();
            if (NetworkManager.Instance != null)
                NetworkManager.Instance.ForgetLocalMatch();

            UIManager.Instance.SwitchToScreen(ScreenType.Gameplay);
        }

        private void OpenBot()
        {
            if (UIManager.Instance == null)
                return;

            GameplayScreen.DiscardLocalSessions();
            if (NetworkManager.Instance != null)
                NetworkManager.Instance.ForgetLocalMatch();

            GameplayScreen.PrepareBotChoice();
            UIManager.Instance.SwitchToScreen(ScreenType.Gameplay);
        }

        private void OpenPassPanel()
        {
            if (_passPanel == null)
                return;

            _passPanel.SetActive(true);
            _passPanel.transform.SetAsLastSibling();
        }

        private static void OpenTable(bool bluetooth)
        {
            if (UIManager.Instance == null)
                return;

            GameplayScreen.DiscardLocalSessions();
            if (NetworkManager.Instance != null)
                NetworkManager.Instance.ForgetLocalMatch();

            var screen = FindAnyObjectByType<LocalNetworkScreen>(FindObjectsInactive.Include);
            if (screen != null)
                screen.Prepare(bluetooth);

            UIManager.Instance.SwitchToScreen(ScreenType.LocalNetwork);
        }

        private void StartPassAndPlay(int playerCount)
        {
            if (UIManager.Instance == null)
                return;

            GameplayScreen.DiscardLocalSessions();
            if (NetworkManager.Instance != null)
                NetworkManager.Instance.ForgetLocalMatch();

            PassAndPlayManager.Begin(playerCount);
            if (_passPanel != null)
                _passPanel.SetActive(false);

            UIManager.Instance.SwitchToScreen(ScreenType.Gameplay);
        }

        private GameObject CreatePassPanel()
        {
            var panel = new GameObject("PassPanel", typeof(RectTransform), typeof(Image));
            panel.layer = gameObject.layer;
            panel.transform.SetParent(transform, false);
            var image = panel.GetComponent<Image>();
            image.sprite = UiFactory.SolidSprite;
            image.type = Image.Type.Simple;
            image.color = UIThemeConfig.Backdrop;
            image.raycastTarget = true;
            UiFactory.Stretch(panel.GetComponent<RectTransform>());

            var content = UiFactory.BeginScrollForm(panel.transform, out var scroll);

            UiFactory.AddMenuTitle(content, "Pass & Play", "Сколько игроков за столом?");
            Wire(UiFactory.AddMenuAction(content, "2 игрока", true), () => StartPassAndPlay(2));
            Wire(UiFactory.AddMenuAction(content, "3 игрока", false), () => StartPassAndPlay(3));
            Wire(UiFactory.AddMenuAction(content, "4 игрока", false), () => StartPassAndPlay(4));
            Wire(UiFactory.AddMenuAction(content, "Назад", false), () => panel.SetActive(false));
            UiFactory.FinalizeMenuForm(scroll);

            panel.SetActive(false);
            return panel;
        }

        private static void ReturnToMenu()
        {
            if (UIManager.Instance != null)
                UIManager.Instance.SwitchToScreen(ScreenType.MainMenu);
        }
    }
}
