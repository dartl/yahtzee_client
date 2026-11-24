using UnityEngine;
using UnityEngine.UI;
using Yahtzee.Audio;

namespace Yahtzee.UI
{
    public sealed class OnlineMenuScreen : UIScreen
    {
        public override ScreenType Type => ScreenType.OnlineMenu;

        private bool _built;
        private bool _layoutLandscape = true;

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

            UiMotion.PopIn(transform);
        }

        private void OnRectTransformDimensionsChange()
        {
            if (!isActiveAndEnabled || !_built)
                return;

            var landscape = Screen.width > Screen.height;
            if (landscape == _layoutLandscape)
                return;

            Rebuild();
            _layoutLandscape = landscape;
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

            UiFactory.AddMenuTitle(content, "Онлайн", "Быстрый поиск и рейтинг");

            Wire(UiFactory.AddMenuAction(content, "Быстрая игра", true), () => OpenQueue(false));
            Wire(UiFactory.AddMenuAction(content, "Рейтинговый матч", false), () => OpenQueue(true));
            Wire(UiFactory.AddMenuAction(content, "Турнир", false), OpenTournament);
            Wire(UiFactory.AddMenuAction(content, "Своё лобби", false), OpenLobby);
            Wire(UiFactory.AddMenuAction(content, "Лидеры", false), OpenLeaders);
            Wire(UiFactory.AddMenuAction(content, "Назад", false), ReturnToMenu);

            UiFactory.FinalizeMenuForm(scroll);
        }

        private static void Wire(Button button, UnityEngine.Events.UnityAction action)
        {
            button.onClick.AddListener(GameAudio.PlayUiClick);
            button.onClick.AddListener(action);
        }

        private static void OpenQueue(bool ranked)
        {
            if (UIManager.Instance == null)
                return;

            var screen = FindAnyObjectByType<MatchmakingScreen>(FindObjectsInactive.Include);
            if (screen != null)
                screen.Prepare(ranked);

            var controller = FindAnyObjectByType<MatchmakingControllerUI>(FindObjectsInactive.Include);
            if (controller != null)
                controller.Prepare(ranked);

            UIManager.Instance.SwitchToScreen(ScreenType.Matchmaking);
        }

        private static void OpenTournament()
        {
            if (UIManager.Instance == null)
                return;

            var screen = FindAnyObjectByType<MatchmakingScreen>(FindObjectsInactive.Include);
            if (screen != null)
                screen.PrepareTournament();

            var controller = FindAnyObjectByType<MatchmakingControllerUI>(FindObjectsInactive.Include);
            if (controller != null)
                controller.PrepareTournament();

            UIManager.Instance.SwitchToScreen(ScreenType.Matchmaking);
        }

        private static void OpenLobby()
        {
            if (UIManager.Instance != null)
                UIManager.Instance.SwitchToScreen(ScreenType.LobbyList);
        }

        private static void OpenLeaders()
        {
            if (UIManager.Instance != null)
                UIManager.Instance.SwitchToScreen(ScreenType.Leaderboard);
        }

        private static void ReturnToMenu()
        {
            if (UIManager.Instance != null)
                UIManager.Instance.SwitchToScreen(ScreenType.MainMenu);
        }
    }
}
