using UnityEngine;

namespace Yahtzee.UI
{
    public sealed class MatchmakingScreen : UIScreen
    {
        public override ScreenType Type => ScreenType.Matchmaking;

        private void Awake()
        {
            UiFactory.ApplyOpaqueMenuBackground((RectTransform)transform);
            EnsureController();
        }

        public void Prepare(bool ranked)
        {
            EnsureController().Prepare(ranked);
        }

        public void PrepareTournament()
        {
            EnsureController().PrepareTournament();
        }

        public override void Show()
        {
            UiFactory.EnsureScreenFillsCanvas((RectTransform)transform);
            UiFactory.ApplyOpaqueMenuBackground((RectTransform)transform);
            base.Show();
            EnsureController().StartQueueSearch();
        }

        public override void Hide()
        {
            var controller = GetComponent<MatchmakingControllerUI>();
            if (controller != null && controller != this)
                controller.StopSearch();

            base.Hide();
        }

        private MatchmakingControllerUI EnsureController()
        {
            var controller = GetComponent<MatchmakingControllerUI>();
            if (controller != null)
                return controller;

            return gameObject.AddComponent<MatchmakingControllerUI>();
        }
    }
}
