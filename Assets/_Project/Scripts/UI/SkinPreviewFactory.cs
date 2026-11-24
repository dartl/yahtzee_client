using UnityEngine;
using UnityEngine.UI;
using Yahtzee.Core;
using Yahtzee.Network;

namespace Yahtzee.UI
{
    public static class SkinPreviewFactory
    {
        public static Image Create(Transform parent, SkinData skin)
        {
            var go = new GameObject("Preview", typeof(RectTransform), typeof(Image));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = UiFactory.RoundSprite;
            image.type = Image.Type.Sliced;
            image.raycastTarget = false;
            Paint(image, skin);
            return image;
        }

        public static void Paint(Image image, SkinData skin)
        {
            if (image == null)
                return;

            if (skin == null)
            {
                image.color = UIThemeConfig.CardBg;
                return;
            }

            if (skin.Kind == SkinType.TableBox)
            {
                var look = SkinVisuals.Table(skin.Slug);
                image.color = look.Felt;
                EnsureInner(image.transform, look.Rails, 0.18f);
                return;
            }

            var dice = SkinVisuals.Dice(skin.Slug);
            image.color = dice.Body;
            EnsurePip(image.transform, dice.Pips);
        }

        private static void EnsureInner(Transform parent, Color color, float inset)
        {
            var existing = parent.Find("Inner");
            Image inner;
            if (existing == null)
            {
                var go = new GameObject("Inner", typeof(RectTransform), typeof(Image));
                go.layer = parent.gameObject.layer;
                go.transform.SetParent(parent, false);
                inner = go.GetComponent<Image>();
                inner.sprite = UiFactory.RoundSprite;
                inner.type = Image.Type.Sliced;
                inner.raycastTarget = false;
            }
            else
            {
                inner = existing.GetComponent<Image>();
            }

            inner.color = color;
            var rect = inner.rectTransform;
            rect.anchorMin = new Vector2(inset, inset);
            rect.anchorMax = new Vector2(1f - inset, 1f - inset);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void EnsurePip(Transform parent, Color color)
        {
            var existing = parent.Find("Pip");
            Image pip;
            if (existing == null)
            {
                var go = new GameObject("Pip", typeof(RectTransform), typeof(Image));
                go.layer = parent.gameObject.layer;
                go.transform.SetParent(parent, false);
                pip = go.GetComponent<Image>();
                pip.sprite = UiFactory.CoinSpritePublic;
                pip.raycastTarget = false;
                pip.preserveAspect = true;
            }
            else
            {
                pip = existing.GetComponent<Image>();
            }

            pip.color = color;
            var rect = pip.rectTransform;
            rect.anchorMin = new Vector2(0.3f, 0.3f);
            rect.anchorMax = new Vector2(0.7f, 0.7f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
