using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using Yahtzee.Audio;
using Yahtzee.Feedback;
using Yahtzee.Localization;
using Yahtzee.Network;

namespace Yahtzee.UI
{
    public sealed class CustomizationWindowController : UIScreen
    {
        public override ScreenType Type => ScreenType.Customization;

        private Text _status;
        private RectTransform _list;
        private bool _busy;

        public override void Show()
        {
            base.Show();
            var rect = (RectTransform)transform;
            UiFactory.EnsureScreenFillsCanvas(rect);
            UiFactory.ApplyOpaqueMenuBackground(rect);
            EnsureChrome();
            UiMotion.PopIn(transform);
            _ = Reload();
        }

        private void EnsureChrome()
        {
            if (_list != null)
                return;

            UiFactory.CreateScreenHeader(transform, "Кастомизация", "Экипировка купленных скинов");
            _status = UiFactory.CreateLabel(transform, "Status", string.Empty, 24, TextAnchor.MiddleCenter);
            UiFactory.Place(_status.rectTransform, 0.08f, 0.82f, 0.92f, 0.9f);

            var listObject = new GameObject("List", typeof(RectTransform));
            listObject.layer = gameObject.layer;
            listObject.transform.SetParent(transform, false);
            _list = listObject.GetComponent<RectTransform>();
            UiFactory.Place(_list, 0.08f, 0.16f, 0.92f, 0.81f);

            var back = UiFactory.CreateButton(transform, "Назад");
            UiFactory.StyleGlass(back);
            UiFactory.Place(back.GetComponent<RectTransform>(), 0.22f, 0.03f, 0.78f, 0.12f);
            back.onClick.AddListener(ReturnToMenu);
        }

        private async Task Reload()
        {
            if (_busy)
                return;

            _busy = true;
            ScreenFeedback.SetInfo(_status, "ui.shop.loading");

            try
            {
                await SkinManager.Ensure().Refresh();
            }
            catch (Exception exception)
            {
                ScreenFeedback.SetError(_status, exception);
            }
            finally
            {
                _busy = false;
                if (this != null)
                    Paint();
            }
        }

        private void Paint()
        {
            if (_list == null)
                return;

            for (var index = _list.childCount - 1; index >= 0; index--)
                Destroy(_list.GetChild(index).gameObject);

            var manager = SkinManager.Instance;
            if (manager == null)
                return;

            var owned = 0;
            for (var index = 0; index < manager.Skins.Count; index++)
            {
                if (manager.Skins[index] != null && manager.Skins[index].Owned)
                    owned++;
            }

            if (owned == 0)
                ScreenFeedback.SetInfo(_status, "ui.shop.empty_owned");
            else
                ScreenFeedback.Clear(_status);

            var slot = 0;
            for (var index = 0; index < manager.Skins.Count; index++)
            {
                var skin = manager.Skins[index];
                if (skin == null || !skin.Owned)
                    continue;

                var height = 1f / Mathf.Max(1, owned);
                var top = 1f - slot * height;
                var bottom = top - height + 0.02f;
                slot++;

                var row = new GameObject("Row", typeof(RectTransform), typeof(Image));
                row.layer = gameObject.layer;
                row.transform.SetParent(_list, false);
                var rowImage = row.GetComponent<Image>();
                rowImage.sprite = UiFactory.RoundSprite;
                rowImage.type = Image.Type.Sliced;
                rowImage.color = skin.Equipped
                    ? new Color(0.95f, 0.62f, 0.16f, 0.2f)
                    : new Color(1f, 1f, 1f, 0.08f);
                var rowRect = row.GetComponent<RectTransform>();
                rowRect.anchorMin = new Vector2(0f, bottom);
                rowRect.anchorMax = new Vector2(1f, top);
                rowRect.offsetMin = Vector2.zero;
                rowRect.offsetMax = Vector2.zero;

                var preview = SkinPreviewFactory.Create(row.transform, skin);
                UiFactory.Place(preview.rectTransform, 0.02f, 0.12f, 0.22f, 0.88f);

                var kind = skin.Kind == SkinType.TableBox ? "Коробка" : "Кубики";
                var label = UiFactory.CreateLabel(row.transform, "Name", skin.Name + "\n" + kind, 22, TextAnchor.MiddleLeft);
                UiFactory.Place(label.rectTransform, 0.24f, 0.08f, 0.62f, 0.92f);
                var button = UiFactory.CreateButton(row.transform, skin.Equipped ? "Надето" : "Применить");
                if (skin.Equipped)
                    UiFactory.StyleAccent(button);
                else
                    UiFactory.StyleGlass(button);
                UiFactory.Place(button.GetComponent<RectTransform>(), 0.64f, 0.15f, 0.98f, 0.85f);
                button.interactable = !skin.Equipped;
                var skinId = skin.Id;
                button.onClick.AddListener(() => _ = Equip(skinId));
            }
        }

        private async Task Equip(int skinId)
        {
            if (_busy)
                return;

            _busy = true;
            GameAudio.PlayUiClick();
            try
            {
                await SkinManager.Ensure().Equip(skinId);
                ScreenFeedback.SetInfo(_status, "ui.shop.equipped");
            }
            catch (Exception exception)
            {
                ScreenFeedback.SetError(_status, exception);
            }
            finally
            {
                _busy = false;
                if (this != null)
                    Paint();
            }
        }

        private static void ReturnToMenu()
        {
            GameAudio.PlayUiClick();
            if (UIManager.Instance != null)
                UIManager.Instance.SwitchToScreen(ScreenType.MainMenu);
        }
    }
}
