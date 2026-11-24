using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Yahtzee.Core;

namespace Yahtzee.UI
{
    public struct PlayerScoreSlot
    {
        public string Name;
        public int Score;
        public bool Visible;
        public bool Active;
        public bool Local;
        public bool Bot;
    }

    public sealed class PlayerScoreStrip : MonoBehaviour
    {
        public const int Capacity = 4;

        private readonly Card[] _cards = new Card[Capacity];
        private bool _built;

        public void Show(PlayerScoreSlot[] slots)
        {
            Ensure();
            PlaceLocalFirst(slots);
            for (var index = 0; index < Capacity; index++)
            {
                var slot = slots != null && index < slots.Length ? slots[index] : default;
                var card = _cards[index];
                card.Root.SetActive(slot.Visible);
                if (!slot.Visible)
                    continue;

                card.Background.color = FaceColor(slot);
                card.Outline.enabled = slot.Active;
                card.Outline.effectColor = slot.Active ? TurnGlow : Color.clear;
                var textColor = slot.Active ? UIThemeConfig.TextOnAccent : UIThemeConfig.TextPrimary;
                UiFactory.ApplyAutoText(card.Name, string.IsNullOrWhiteSpace(slot.Name) ? "Игрок" : slot.Name, textColor);
                UiFactory.ApplyAutoText(card.Score, slot.Score.ToString(), textColor);
                card.Score.fontStyle = FontStyles.Bold;
            }

            FitText();
        }

        private static void PlaceLocalFirst(PlayerScoreSlot[] slots)
        {
            if (slots == null)
                return;

            var local = -1;
            for (var index = 0; index < slots.Length; index++)
            {
                if (!slots[index].Visible || !slots[index].Local)
                    continue;

                local = index;
                break;
            }

            if (local <= 0)
                return;

            var mine = slots[local];
            for (var index = local; index > 0; index--)
                slots[index] = slots[index - 1];

            slots[0] = mine;
        }

        public void FitText()
        {
            if (!_built)
                return;

            for (var index = 0; index < Capacity; index++)
            {
                var card = _cards[index];
                if (card.Root == null || !card.Root.activeSelf)
                    continue;

                var nameRect = card.Name.rectTransform.rect;
                var scoreRect = card.Score.rectTransform.rect;
                if (nameRect.height < 8f || scoreRect.height < 8f || nameRect.width < 24f)
                    continue;

                var cardHeight = ((RectTransform)card.Root.transform).rect.height;
                var scoreSize = Mathf.Clamp(cardHeight * 0.34f, 16f, 28f);
                var nameWidth = nameRect.width - 8f;
                var nameHeight = nameRect.height * 0.88f;
                var nameSize = Mathf.Min(scoreSize * 1.32f, nameHeight);
                var fitted = LargestFittingSize(card.Name, card.Name.text, nameWidth, nameHeight);
                if (nameSize > fitted)
                    nameSize = fitted;
                if (nameSize <= scoreSize)
                    scoreSize = Mathf.Max(14f, nameSize / 1.32f);

                ApplyFixedSize(card.Name, nameSize);
                ApplyFixedSize(card.Score, scoreSize);
            }
        }

        private void Ensure()
        {
            if (_built)
                return;

            _built = true;
            var tray = gameObject.GetComponent<Image>();
            if (tray == null)
                tray = gameObject.AddComponent<Image>();
            tray.sprite = UiFactory.SolidSprite;
            tray.type = Image.Type.Simple;
            tray.color = TrayColor;
            tray.raycastTarget = false;

            var layout = gameObject.GetComponent<HorizontalLayoutGroup>();
            if (layout == null)
                layout = gameObject.AddComponent<HorizontalLayoutGroup>();

            layout.spacing = 4f;
            layout.padding = new RectOffset(6, 6, 4, 4);
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;

            for (var index = 0; index < Capacity; index++)
                _cards[index] = CreateCard(index);
        }

        private Card CreateCard(int index)
        {
            var root = new GameObject("Seat" + (index + 1), typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            root.layer = gameObject.layer;
            root.transform.SetParent(transform, false);

            var background = root.GetComponent<Image>();
            background.sprite = UiFactory.RoundSprite;
            background.type = Image.Type.Sliced;
            background.color = IdleColor;
            background.raycastTarget = false;

            var stack = root.GetComponent<VerticalLayoutGroup>();
            stack.padding = new RectOffset(6, 6, 2, 2);
            stack.spacing = 0f;
            stack.childAlignment = TextAnchor.MiddleCenter;
            stack.childControlWidth = true;
            stack.childControlHeight = true;
            stack.childForceExpandWidth = true;
            stack.childForceExpandHeight = true;

            var element = root.GetComponent<LayoutElement>();
            element.flexibleWidth = 1f;
            element.minHeight = 40f;
            element.preferredHeight = 46f;

            var outline = root.AddComponent<Outline>();
            outline.effectColor = TurnGlow;
            outline.effectDistance = new Vector2(3f, -3f);
            outline.useGraphicAlpha = false;
            outline.enabled = false;

            var name = UiFactory.CreateAutoLabel(root.transform, "Name", "Игрок", TextAlignmentOptions.Center);
            var score = UiFactory.CreateAutoLabel(root.transform, "Score", "0", TextAlignmentOptions.Center);
            var nameElement = name.gameObject.AddComponent<LayoutElement>();
            nameElement.flexibleWidth = 1f;
            nameElement.flexibleHeight = 1.15f;
            nameElement.minHeight = 18f;
            var scoreElement = score.gameObject.AddComponent<LayoutElement>();
            scoreElement.flexibleWidth = 1f;
            scoreElement.flexibleHeight = 1f;
            scoreElement.minHeight = 14f;
            // Overflow + FitText: keep "Nick 45" fully visible instead of ellipsis eating the timer.
            name.textWrappingMode = TextWrappingModes.NoWrap;
            name.overflowMode = TextOverflowModes.Overflow;
            score.textWrappingMode = TextWrappingModes.NoWrap;
            score.overflowMode = TextOverflowModes.Overflow;
            score.fontStyle = FontStyles.Bold;

            return new Card
            {
                Root = root,
                Background = background,
                Outline = outline,
                Name = name,
                Score = score
            };
        }

        private static float LargestFittingSize(TextMeshProUGUI label, string text, float width, float height)
        {
            if (label == null || width < 8f || height < 8f)
                return 14f;

            var best = 12f;
            var low = 12f;
            var high = Mathf.Min(42f, height);
            for (var step = 0; step < 8; step++)
            {
                var mid = (low + high) * 0.5f;
                var measured = Measure(label, text, mid);
                if (measured.x <= width && measured.y <= height)
                {
                    best = mid;
                    low = mid;
                }
                else
                {
                    high = mid;
                }
            }

            return best;
        }

        private static Vector2 Measure(TextMeshProUGUI label, string text, float size)
        {
            var sizeBefore = label.fontSize;
            var autoBefore = label.enableAutoSizing;
            label.enableAutoSizing = false;
            label.fontSize = size;
            var values = label.GetPreferredValues(string.IsNullOrEmpty(text) ? " " : text);
            label.fontSize = sizeBefore;
            label.enableAutoSizing = autoBefore;
            return values;
        }

        private static void ApplyFixedSize(TextMeshProUGUI label, float size)
        {
            if (label == null)
                return;

            label.enableAutoSizing = false;
            label.fontSize = size;
            var fallback = label.GetComponentInChildren<Text>(true);
            if (fallback == null)
                return;

            fallback.resizeTextForBestFit = false;
            fallback.fontSize = Mathf.RoundToInt(size);
        }

        private static Color FaceColor(PlayerScoreSlot slot)
        {
            if (slot.Active)
                return ActiveColor;

            if (slot.Local)
                return LocalColor;

            if (slot.Bot)
                return BotColor;

            return IdleColor;
        }

        private static Color TrayColor => UIThemeConfig.PanelBg;
        private static Color IdleColor => UIThemeConfig.CardBg;
        private static readonly Color LocalColor = new Color(0.91f, 0.94f, 0.99f, 1f);
        private static readonly Color BotColor = new Color(0.95f, 0.94f, 0.92f, 1f);
        private static Color ActiveColor => UIThemeConfig.ButtonAccent;
        private static Color TurnGlow => UIThemeConfig.TextAccent;

        private sealed class Card
        {
            public GameObject Root;
            public Image Background;
            public Outline Outline;
            public TextMeshProUGUI Name;
            public TextMeshProUGUI Score;
        }
    }
}
