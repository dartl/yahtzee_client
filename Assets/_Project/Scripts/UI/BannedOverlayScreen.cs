using UnityEngine;
using UnityEngine.UI;

using Yahtzee.Network;

namespace Yahtzee.UI
{
    public sealed class BannedOverlayScreen : UIScreen
    {
        public override ScreenType Type => ScreenType.BannedOverlay;

        [SerializeField] private Text reasonText;

        private void Awake()
        {
            UiFactory.ApplyOpaqueMenuBackground((RectTransform)transform);
            if (reasonText != null)
                return;

            reasonText = UiFactory.CreateLabel(transform, "Reason", "Вы заблокированы.", 32, TextAnchor.MiddleCenter);
            UiFactory.Place(reasonText.rectTransform, 0.1f, 0.35f, 0.9f, 0.65f);
        }

        public override void Show()
        {
            UiFactory.ApplyOpaqueMenuBackground((RectTransform)transform);
            base.Show();
        }

        public void SetReason(string reason)
        {
            SetBan(reason, NetworkManager.LastBanUntil);
        }

        public void SetBan(string reason, string until)
        {
            if (reasonText == null)
                return;

            var text = string.IsNullOrEmpty(reason) ? "Вы заблокированы." : reason;
            if (!string.IsNullOrEmpty(until))
                text += "\nСрок: " + until;

            reasonText.text = text;
        }
    }
}
