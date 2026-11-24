using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Yahtzee.Core;
using Yahtzee.Localization;
using Yahtzee.Network;

namespace Yahtzee.UI
{
    public sealed class LeaderboardScreen : UIScreen
    {
        public override ScreenType Type => ScreenType.Leaderboard;

        private const int MaxRows = 100;
        private static readonly Color RowColor = new Color(0.1f, 0.16f, 0.13f, 0.95f);
        private static readonly Color SelfColor = new Color(0.16f, 0.32f, 0.24f, 1f);

        [SerializeField] private Button tabMmrButton;
        [SerializeField] private Button tabTournamentButton;
        [SerializeField] private Transform entriesContainer;
        [SerializeField] private GameObject entryRowPrefab;
        [SerializeField] private TextMeshProUGUI dailyMatchesLimitText;
        [SerializeField] private Button backToMenuButton;
        [SerializeField] private GameObject myStatsPanel;
        [SerializeField] private TextMeshProUGUI myRankText;
        [SerializeField] private TextMeshProUGUI myNicknameText;
        [SerializeField] private TextMeshProUGUI myValueText;

        private readonly List<GameObject> _rowPool = new List<GameObject>();
        private RectTransform _scrollRoot;
        private Text _statusLabel;
        private int _usedRows;
        private int _requestId;
        private bool _showingMmr = true;
        private bool _built;
        private bool _wired;

        public override void Show()
        {
            base.Show();
            EnsureInterface();
            _showingMmr = true;
            ApplyTabColors();
            _ = FetchAndRenderLeaderboard(true);
        }

        public override void Hide()
        {
            _requestId++;
            base.Hide();
        }

        private void OnEnable()
        {
            Wire();
        }

        private void OnDisable()
        {
            Unwire();
        }

        private void EnsureInterface()
        {
            var rect = (RectTransform)transform;
            UiFactory.EnsureScreenFillsCanvas(rect);
            ApplyOpaqueMenuBackground(rect);
            if (_built && entriesContainer != null && myStatsPanel != null && rect.rect.width >= 200f)
                return;

            BuildInterface();
            ApplyOpaqueMenuBackground(rect);
        }

        private static void ApplyOpaqueMenuBackground(RectTransform screen)
        {
            UiFactory.ApplyOpaqueMenuBackground(screen);
        }

        private void BuildInterface()
        {
            var root = new GameObject("LeaderboardRoot", typeof(RectTransform));
            root.layer = gameObject.layer;
            root.transform.SetParent(transform, false);
            UiFactory.Stretch(root.GetComponent<RectTransform>());

            var title = UiFactory.CreateLabel(root.transform, "Title", "Лидеры", 48, TextAnchor.MiddleCenter);
            UiFactory.Place(title.rectTransform, 0.08f, 0.92f, 0.92f, 0.985f);

            tabMmrButton = UiFactory.CreateButton(root.transform, "Рейтинг");
            UiFactory.StyleAccent(tabMmrButton);
            UiFactory.Place(tabMmrButton.GetComponent<RectTransform>(), 0.08f, 0.835f, 0.48f, 0.91f);
            tabTournamentButton = UiFactory.CreateButton(root.transform, "Турнир");
            UiFactory.StyleGlass(tabTournamentButton);
            UiFactory.Place(tabTournamentButton.GetComponent<RectTransform>(), 0.52f, 0.835f, 0.92f, 0.91f);

            dailyMatchesLimitText = UiFactory.CreateAutoLabel(
                root.transform,
                "DailyLimit",
                "Матчи турнира на сегодня: 3/3",
                TextAlignmentOptions.Center);
            UiFactory.Place(dailyMatchesLimitText.rectTransform, 0.08f, 0.775f, 0.92f, 0.83f);
            dailyMatchesLimitText.gameObject.SetActive(false);

            _scrollRoot = CreateScroll(root.transform, out var content);
            UiFactory.Place(_scrollRoot, 0.06f, 0.22f, 0.94f, 0.765f);
            entriesContainer = content;
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            entryRowPrefab = BuildRowTemplate();
            entryRowPrefab.transform.SetParent(transform, false);
            entryRowPrefab.SetActive(false);

            _statusLabel = UiFactory.CreateLabel(root.transform, "Status", "Загрузка...", 28, TextAnchor.MiddleCenter);
            UiFactory.Place(_statusLabel.rectTransform, 0.1f, 0.42f, 0.9f, 0.54f);
            _statusLabel.raycastTarget = false;

            myStatsPanel = new GameObject("MyStats", typeof(RectTransform), typeof(Image));
            myStatsPanel.layer = gameObject.layer;
            myStatsPanel.transform.SetParent(root.transform, false);
            myStatsPanel.GetComponent<Image>().color = SelfColor;
            UiFactory.Place(myStatsPanel.GetComponent<RectTransform>(), 0.06f, 0.115f, 0.94f, 0.21f);
            myRankText = UiFactory.CreateAutoLabel(myStatsPanel.transform, "myRankText", "—", TextAlignmentOptions.Center);
            UiFactory.Place(myRankText.rectTransform, 0.01f, 0.1f, 0.16f, 0.9f);
            myNicknameText = UiFactory.CreateAutoLabel(myStatsPanel.transform, "myNicknameText", "Вы", TextAlignmentOptions.MidlineLeft);
            UiFactory.Place(myNicknameText.rectTransform, 0.16f, 0.1f, 0.7f, 0.9f);
            myValueText = UiFactory.CreateAutoLabel(myStatsPanel.transform, "myValueText", "—", TextAlignmentOptions.MidlineRight);
            UiFactory.Place(myValueText.rectTransform, 0.7f, 0.1f, 0.98f, 0.9f);

            backToMenuButton = UiFactory.CreateButton(root.transform, "Назад");
            UiFactory.StyleGlass(backToMenuButton);
            UiFactory.Place(backToMenuButton.GetComponent<RectTransform>(), 0.22f, 0.02f, 0.78f, 0.1f);

            _built = true;
            Wire();
            ApplyTabColors();
        }

        private async Task FetchAndRenderLeaderboard(bool isMmr)
        {
            var request = ++_requestId;
            _showingMmr = isMmr;
            ApplyTabColors();
            ReleaseRows();
            SetStatus("Загрузка...");
            if (dailyMatchesLimitText != null)
                dailyMatchesLimitText.gameObject.SetActive(!isMmr);

            if (NetworkManager.Instance == null)
            {
                if (request == _requestId)
                    SetStatus("Нет подключения к серверу.");
                return;
            }

            try
            {
                LeaderboardResponseDto board = isMmr
                    ? LeaderboardResponseDto.FromMmr(await NetworkManager.Instance.GetMmrLeaderboard())
                    : LeaderboardResponseDto.FromTournament(await NetworkManager.Instance.GetTournamentLeaderboard());

                if (request != _requestId || this == null)
                    return;

                Render(board, isMmr);
            }
            catch (Exception exception)
            {
                if (request != _requestId || this == null)
                    return;

                Debug.LogWarning("Yahtzee leaderboard: " + exception.Message);
                SetStatus(GameText.FromException(exception));
                PaintSelf(null, isMmr);
            }
        }

        private void Render(LeaderboardResponseDto board, bool isMmr)
        {
            var entries = board != null ? board.Entries : null;
            var count = entries != null ? entries.Length : 0;
            if (count > MaxRows)
                count = MaxRows;

            for (var index = 0; index < count; index++)
                FillRow(RentRow(), entries[index], isMmr);

            if (entriesContainer is RectTransform content)
                LayoutRebuilder.ForceRebuildLayoutImmediate(content);

            SetStatus(count == 0 ? "В таблице пока нет игроков." : null);
            PaintSelf(board != null ? board.Meta : null, isMmr);
            PaintDailyLimit(board != null ? board.Meta : null, isMmr);
        }

        private void PaintSelf(LeaderboardSelfMetaDto meta, bool isMmr)
        {
            if (myStatsPanel != null)
                myStatsPanel.SetActive(true);

            if (myRankText != null)
                myRankText.text = meta == null || meta.MyRank <= 0
                    ? "—"
                    : meta.MyRank.ToString(CultureInfo.InvariantCulture);
            if (myNicknameText != null)
                myNicknameText.text = SelfNickname(meta != null ? meta.Nickname : null);
            if (myValueText != null)
                myValueText.text = meta == null
                    ? "—"
                    : (isMmr ? FormatRating(meta.MyRating) : FormatScore(meta.MyScore));
        }

        private void PaintDailyLimit(LeaderboardSelfMetaDto meta, bool isMmr)
        {
            if (dailyMatchesLimitText == null)
                return;

            dailyMatchesLimitText.gameObject.SetActive(!isMmr);
            if (isMmr || meta == null)
                return;

            var left = Mathf.Max(0, meta.DailyMatchesLeft);
            var total = Mathf.Max(left, left + Mathf.Max(0, meta.DailyMatchesPlayed));
            dailyMatchesLimitText.text = "Матчи турнира на сегодня: "
                + left.ToString(CultureInfo.InvariantCulture)
                + "/"
                + total.ToString(CultureInfo.InvariantCulture);
        }

        private void FillRow(GameObject row, LeaderboardEntryDto entry, bool isMmr)
        {
            if (row == null)
                return;

            var rank = FindLabel(row.transform, "rankText");
            var nickname = FindLabel(row.transform, "nicknameText");
            var value = FindLabel(row.transform, "valueText");
            var country = FindLabel(row.transform, "countryText");
            if (rank != null)
                rank.text = entry != null ? entry.Rank.ToString(CultureInfo.InvariantCulture) : "—";
            if (nickname != null)
                nickname.text = FormatNickname(entry != null ? entry.Nickname : null);
            if (value != null)
                value.text = entry == null
                    ? "—"
                    : (isMmr ? FormatRating(entry.Rating) : FormatScore(entry.Score));
            if (country != null)
                country.text = FormatCountry(entry != null ? entry.CountryCode : null);
        }

        private GameObject RentRow()
        {
            GameObject row;
            if (_usedRows < _rowPool.Count)
            {
                row = _rowPool[_usedRows];
            }
            else
            {
                row = Instantiate(entryRowPrefab, entriesContainer);
                row.name = "EntryRow";
                _rowPool.Add(row);
            }

            _usedRows++;
            row.transform.SetParent(entriesContainer, false);
            row.transform.SetAsLastSibling();
            row.SetActive(true);
            return row;
        }

        private void ReleaseRows()
        {
            for (var index = 0; index < _rowPool.Count; index++)
            {
                if (_rowPool[index] != null)
                    _rowPool[index].SetActive(false);
            }

            _usedRows = 0;
        }

        private void ShowMmrTab()
        {
            _ = FetchAndRenderLeaderboard(true);
        }

        private void ShowTournamentTab()
        {
            _ = FetchAndRenderLeaderboard(false);
        }

        private void BackToMenu()
        {
            _requestId++;
            if (UIManager.Instance != null)
                UIManager.Instance.SwitchToScreen(ScreenType.OnlineMenu);
        }

        private void ApplyTabColors()
        {
            PaintTab(tabMmrButton, _showingMmr);
            PaintTab(tabTournamentButton, !_showingMmr);
        }

        private static void PaintTab(Button button, bool active)
        {
            if (button == null)
                return;

            if (active)
                UiFactory.StyleAccent(button);
            else
                UiFactory.StyleGlass(button);
        }

        private void SetStatus(string message)
        {
            if (_statusLabel == null)
                return;

            var visible = !string.IsNullOrEmpty(message);
            _statusLabel.gameObject.SetActive(visible);
            if (visible)
                _statusLabel.text = message;
        }

        private void Wire()
        {
            if (_wired)
                return;

            if (tabMmrButton != null)
                tabMmrButton.onClick.AddListener(ShowMmrTab);
            if (tabTournamentButton != null)
                tabTournamentButton.onClick.AddListener(ShowTournamentTab);
            if (backToMenuButton != null)
                backToMenuButton.onClick.AddListener(BackToMenu);
            _wired = tabMmrButton != null && tabTournamentButton != null && backToMenuButton != null;
        }

        private void Unwire()
        {
            if (!_wired)
                return;

            if (tabMmrButton != null)
                tabMmrButton.onClick.RemoveListener(ShowMmrTab);
            if (tabTournamentButton != null)
                tabTournamentButton.onClick.RemoveListener(ShowTournamentTab);
            if (backToMenuButton != null)
                backToMenuButton.onClick.RemoveListener(BackToMenu);
            _wired = false;
        }

        private GameObject BuildRowTemplate()
        {
            var row = new GameObject("EntryRowTemplate", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            row.layer = gameObject.layer;
            row.GetComponent<Image>().color = RowColor;
            var element = row.GetComponent<LayoutElement>();
            element.minHeight = 72f;
            element.preferredHeight = 72f;
            element.flexibleWidth = 1f;

            var rank = UiFactory.CreateAutoLabel(row.transform, "rankText", "1", TextAlignmentOptions.Center);
            UiFactory.Place(rank.rectTransform, 0.01f, 0.08f, 0.14f, 0.92f);
            var country = UiFactory.CreateAutoLabel(row.transform, "countryText", "[--]", TextAlignmentOptions.Center);
            UiFactory.Place(country.rectTransform, 0.14f, 0.08f, 0.3f, 0.92f);
            var nickname = UiFactory.CreateAutoLabel(row.transform, "nicknameText", "Игрок", TextAlignmentOptions.MidlineLeft);
            UiFactory.Place(nickname.rectTransform, 0.3f, 0.08f, 0.72f, 0.92f);
            var value = UiFactory.CreateAutoLabel(row.transform, "valueText", "0", TextAlignmentOptions.MidlineRight);
            UiFactory.Place(value.rectTransform, 0.72f, 0.08f, 0.99f, 0.92f);
            return row;
        }

        private static RectTransform CreateScroll(Transform parent, out RectTransform content)
        {
            var scrollObject = new GameObject("Scroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            scrollObject.layer = parent.gameObject.layer;
            scrollObject.transform.SetParent(parent, false);
            scrollObject.GetComponent<Image>().color = UIThemeConfig.PanelBg;
            var scroll = scrollObject.GetComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            viewport.layer = parent.gameObject.layer;
            viewport.transform.SetParent(scrollObject.transform, false);
            UiFactory.Stretch(viewport.GetComponent<RectTransform>());
            viewport.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.02f);
            viewport.GetComponent<Mask>().showMaskGraphic = false;

            var contentObject = new GameObject("Content", typeof(RectTransform));
            contentObject.layer = parent.gameObject.layer;
            contentObject.transform.SetParent(viewport.transform, false);
            content = contentObject.GetComponent<RectTransform>();
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;
            content.sizeDelta = new Vector2(0f, 0f);

            scroll.viewport = viewport.GetComponent<RectTransform>();
            scroll.content = content;
            return scrollObject.GetComponent<RectTransform>();
        }

        private static TextMeshProUGUI FindLabel(Transform root, string childName)
        {
            var nested = root.GetComponentsInChildren<TextMeshProUGUI>(true);
            for (var index = 0; index < nested.Length; index++)
            {
                if (nested[index] != null && nested[index].name == childName)
                    return nested[index];
            }

            return null;
        }

        private static string FormatNickname(string nickname)
        {
            return string.IsNullOrWhiteSpace(nickname) ? "Игрок" : nickname.Trim();
        }

        private static string SelfNickname(string nickname)
        {
            if (!string.IsNullOrWhiteSpace(nickname))
                return nickname.Trim();

            if (NetworkManager.Instance != null && NetworkManager.Instance.CurrentUser != null
                && !string.IsNullOrWhiteSpace(NetworkManager.Instance.CurrentUser.Nickname))
                return NetworkManager.Instance.CurrentUser.Nickname;

            return "Вы";
        }

        private static string FormatCountry(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return "—";

            return "[" + code.Trim().ToUpperInvariant() + "]";
        }

        private static string FormatRating(int rating)
        {
            return rating.ToString(CultureInfo.InvariantCulture);
        }

        private static string FormatScore(float score)
        {
            var whole = Mathf.Round(score);
            if (Mathf.Abs(score - whole) < 0.05f)
                return whole.ToString("0", CultureInfo.InvariantCulture);

            return score.ToString("0.0", CultureInfo.InvariantCulture);
        }
    }
}
