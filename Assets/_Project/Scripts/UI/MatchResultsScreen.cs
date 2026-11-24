using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using Yahtzee.Audio;
using Yahtzee.Core;

namespace Yahtzee.UI
{
    public sealed class MatchResultsScreen : UIScreen
    {
        public override ScreenType Type => ScreenType.MatchResults;

        private Text _headline;
        private RectTransform _list;
        private Button _menuButton;
        private Button _rematchButton;
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
            if (!_built || landscape != _layoutLandscape)
            {
                Rebuild();
                _built = true;
                _layoutLandscape = landscape;
            }

            Paint();
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
            Paint();
        }

        private void Rebuild()
        {
            UiFactory.ClearContent(transform);
            UiFactory.ApplyOpaqueMenuBackground((RectTransform)transform);
            Build();
        }

        private void Build()
        {
            var landscape = Screen.width > Screen.height;
            var content = UiFactory.BeginScrollForm(transform, out var scroll);

            _headline = UiFactory.AddMenuTitle(content, "Итоги партии");

            var listObject = new GameObject("List", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            listObject.layer = gameObject.layer;
            listObject.transform.SetParent(content, false);
            _list = listObject.GetComponent<RectTransform>();
            var listLayout = listObject.GetComponent<VerticalLayoutGroup>();
            listLayout.spacing = 8f;
            listLayout.childControlWidth = true;
            listLayout.childControlHeight = true;
            listLayout.childForceExpandWidth = true;
            listLayout.childForceExpandHeight = false;
            var listElement = listObject.GetComponent<LayoutElement>();
            listElement.minHeight = landscape ? 160f : 220f;
            listElement.preferredHeight = landscape ? 200f : 280f;
            listElement.minWidth = UiFactory.MenuActionWidth * 0.92f;
            listElement.preferredWidth = UiFactory.MenuActionWidth;
            listElement.flexibleWidth = 0f;

            var actions = new GameObject("Actions", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            actions.layer = gameObject.layer;
            actions.transform.SetParent(content, false);
            UiFactory.SizeMenuAction(actions.GetComponent<RectTransform>());
            var actionsLayout = actions.GetComponent<HorizontalLayoutGroup>();
            actionsLayout.spacing = 12f;
            actionsLayout.childControlWidth = true;
            actionsLayout.childControlHeight = true;
            actionsLayout.childForceExpandWidth = true;
            actionsLayout.childForceExpandHeight = true;

            _menuButton = UiFactory.CreateButton(actions.transform, "В меню");
            UiFactory.StyleGlass(_menuButton);
            _menuButton.onClick.AddListener(ReturnToMenu);

            _rematchButton = UiFactory.CreateButton(actions.transform, "Ещё раз");
            UiFactory.StyleAccent(_rematchButton);
            _rematchButton.onClick.AddListener(Rematch);

            UiFactory.FinalizeMenuForm(scroll);
        }

        private void Paint()
        {
            if (_headline != null)
                _headline.text = string.IsNullOrWhiteSpace(MatchResultsBridge.Headline)
                    ? "Итоги партии"
                    : MatchResultsBridge.Headline;

            if (_rematchButton != null)
            {
                _rematchButton.gameObject.SetActive(MatchResultsBridge.CanRematch);
                var rematchLabel = _rematchButton.GetComponentInChildren<Text>(true);
                if (rematchLabel != null)
                    rematchLabel.text = MatchResultsBridge.RematchLabel;
            }

            if (_list == null)
                return;

            for (var index = _list.childCount - 1; index >= 0; index--)
                Destroy(_list.GetChild(index).gameObject);

            var rows = MatchResultsBridge.Rows;
            var localWon = false;
            for (var index = 0; index < rows.Count; index++)
            {
                var row = rows[index];
                if (row != null && row.IsLocal && index == 0)
                    localWon = true;

                var line = new GameObject("Row", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                line.layer = gameObject.layer;
                line.transform.SetParent(_list, false);
                var image = line.GetComponent<Image>();
                image.sprite = UiFactory.RoundSprite;
                image.type = Image.Type.Sliced;
                image.color = row != null && row.IsLocal
                    ? UIThemeConfig.RowHint
                    : UIThemeConfig.CardBg;
                UiFactory.SizeMenuRow(line.GetComponent<RectTransform>(), 56f);

                var place = (index + 1).ToString(CultureInfo.InvariantCulture) + ".";
                var name = row == null ? "—" : row.Nickname + (row.IsBot ? " (бот)" : string.Empty);
                var score = row == null ? "0" : row.Score.ToString(CultureInfo.InvariantCulture);
                var mmr = row == null || row.MmrDelta == 0
                    ? string.Empty
                    : (row.MmrDelta > 0 ? "+" : string.Empty) + row.MmrDelta.ToString(CultureInfo.InvariantCulture) + " MMR";

                var placeLabel = UiFactory.CreateLabel(line.transform, "Place", place, 24, TextAnchor.MiddleLeft);
                UiFactory.Place(placeLabel.rectTransform, 0.02f, 0.1f, 0.12f, 0.9f);
                var nameLabel = UiFactory.CreateLabel(line.transform, "Name", name, 22, TextAnchor.MiddleLeft);
                UiFactory.Place(nameLabel.rectTransform, 0.12f, 0.1f, 0.5f, 0.9f);
                var scoreLabel = UiFactory.CreateLabel(line.transform, "Score", score, 24, TextAnchor.MiddleCenter);
                UiFactory.Place(scoreLabel.rectTransform, 0.5f, 0.1f, 0.68f, 0.9f);
                var metaLabel = UiFactory.CreateLabel(line.transform, "Meta", mmr, 18, TextAnchor.MiddleRight);
                UiFactory.Place(metaLabel.rectTransform, 0.68f, 0.1f, 0.98f, 0.9f);
            }

            if (localWon)
                GameAudio.PlayVictory();
            else
                GameAudio.PlayDefeat();
        }

        private static void ReturnToMenu()
        {
            GameAudio.PlayUiClick();
            MatchResultsBridge.Clear();
            if (UIManager.Instance != null)
                UIManager.Instance.SwitchToScreen(ScreenType.MainMenu);
        }

        private static void Rematch()
        {
            GameAudio.PlayUiClick();
            var action = MatchResultsBridge.RematchAction;
            MatchResultsBridge.Clear();
            action?.Invoke();
        }
    }
}
