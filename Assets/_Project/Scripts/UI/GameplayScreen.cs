using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Yahtzee.Audio;
using Yahtzee.Core;
using Yahtzee.Feedback;
using Yahtzee.Localization;
using Yahtzee.Models;
using Yahtzee.Network;

namespace Yahtzee.UI
{
    [Serializable]
    public class ScoreRowUI
    {
        public YahtzeeCategory category;
        public Button rowButton;
        public Image rowBackground;
        public Image valuePlate;
        public TextMeshProUGUI scoreValueText;
        public int stripe;
        public bool settledVisual;
    }

    public sealed class GameplayScreen : UIScreen
    {
        public static GameplayScreen Instance { get; private set; }

        private static int RollsPerTurn => YahtzeeConfig.Current.RollsPerTurn;

        private static Color CommittedScoreColor => UIThemeConfig.ScoreCommitted;
        private static Color HintScoreColor => UIThemeConfig.ScoreHint;
        private static Color SheetBodyColor => UIThemeConfig.PanelBgSolid;
        private static Color StripTrayColor => UIThemeConfig.PanelBg;
        private static Color RowStripeA => UIThemeConfig.RowA;
        private static Color RowStripeB => UIThemeConfig.RowB;
        private static Color RowCommittedColor => UIThemeConfig.RowCommitted;
        private static Color RowSelectedColor => UIThemeConfig.RowSelected;
        private static Color RowTutorialColor => UIThemeConfig.RowHint;
        private static Color StatChipColor => UIThemeConfig.CardBg;

        public override ScreenType Type => ScreenType.Gameplay;

        [SerializeField] private Button rollDiceButton;
        [SerializeField] private Text rollsLeftText;
        [SerializeField] private TextMeshProUGUI upperScoreText;
        [SerializeField] private TextMeshProUGUI upperBonusText;
        [SerializeField] private TextMeshProUGUI totalScoreText;
        [SerializeField] private List<ScoreRowUI> scoreRows = new List<ScoreRowUI>();
        [SerializeField] private DicePhysicsController dicePhysics;

        private readonly Scorecard _scorecard = new Scorecard();
        private readonly Scorecard _botScorecard = new Scorecard();
        private readonly AIOpponent _opponent = new AIOpponent();
        private Scorecard _shownSheet;
        private int[] _dice = Array.Empty<int>();
        private int[] _settledValues = Array.Empty<int>();
        private bool _animateNextDice;
        private Task _settling = Task.CompletedTask;
        private int _settleId;
        private int _holdTicket;
        private bool _diceFlying;
        private bool _applyingRemoteHold;
        private bool[] _remoteHold;
        private int _rollsLeft;
        private bool _rowsBound;
        private bool _online;
        private bool _ourTurn = true;
        private bool _botTurn;
        private bool _busy;
        private bool _hasSelection;
        private YahtzeeCategory _selectedCategory;
        private Button _commitButton;
        private RectTransform _commitRect;
        private Text _commitLabel;
        private int _seenMatchId;
        private RectTransform _sheetRect;
        private Image _sheetImage;
        private RectTransform _rollRect;
        private Text _opponentTurnLabel;
        private static bool _pendingBotChoice;
        private bool _tutorial;
        private int _tutorialStep;
        private int _tutorialRollIndex;
        private GameObject _tutorialBanner;
        private Text _tutorialTitle;
        private Text _tutorialBody;
        private GameObject _difficultyRoot;
        private Button _closeToMenuButton;
        private GameObject _resignRoot;
        private bool _resigning;
        private RectTransform _scoreStripRect;
        private Image _scoreStripTray;
        private PlayerScoreStrip _scoreStrip;
        private float _playLeft = 0.035f;
        private float _playRight = 0.965f;
        private TrayUIBinder _trayBinder;
        private readonly TrackedSeat[] _tracked = new TrackedSeat[PlayerScoreStrip.Capacity];
        private int _trackedCount;
        private int? _currentTurnUserId;
        private bool _layoutLandscape;
        private bool _layoutGuard;
        private bool _layoutQueued;
        private bool _zonesQueued;
        private readonly Scorecard _blankSheet = new Scorecard();
        private TableMode _tableMode = TableMode.Bot;
        private int _playEpoch;
        private int _seenPassSession = -1;
        private int _seenLanSession = -1;
        private int _seenBluetoothSession = -1;
        private bool _handoffReturnsToMenu;
        private GameObject _handoffRoot;
        private Text _handoffLabel;
        private Text _handoffButtonLabel;
        private GameObject _decisionRoot;
        private Text _decisionLabel;
        private GameObject _absenceRoot;
        private Text _absenceLabel;
        private int _clockGeneration;
        private int _clockShown = -1;
        private bool _expireSent;
        private bool _turnWarningSent;
        private int _graceGeneration;
        private bool _waitingDisconnect;
        private string _matchType;
        private static AudioClip _turnChime;

        private enum TableMode
        {
            Bot,
            PassAndPlay,
            LocalNetwork,
            Bluetooth
        }

        private void Awake()
        {
            Instance = this;
            _rollsLeft = RollsPerTurn;
            AllowCrossPlatformOrientation();

            var rect = (RectTransform)transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;

            var rootImage = GetComponent<Image>();
            if (rootImage != null)
            {
                rootImage.enabled = false;
                rootImage.raycastTarget = false;
            }

            if (rollDiceButton != null && scoreRows != null && scoreRows.Count >= 13)
            {
                _sheetRect = _sheetRect != null ? _sheetRect : transform.Find("ScoreSheet") as RectTransform;
                _sheetImage = _sheetRect != null ? _sheetRect.GetComponent<Image>() : null;
                _scoreStripRect = _scoreStripRect != null ? _scoreStripRect : transform.Find("ScoreStrip") as RectTransform;
                _scoreStripTray = _scoreStripRect != null ? _scoreStripRect.GetComponent<Image>() : null;
                _scoreStrip = _scoreStripRect != null ? _scoreStripRect.GetComponent<PlayerScoreStrip>() : null;
                StyleOpaqueHudPanels();
                StripScoreSheetExtras();
                RestyleScoreValueCells();
                EnsureOpponentTurnLabel();
                EnsureSessionButtons();
                return;
            }

            var sheet = new GameObject("ScoreSheet", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            sheet.layer = gameObject.layer;
            sheet.transform.SetParent(transform, false);
            _sheetImage = sheet.GetComponent<Image>();
            _sheetImage.sprite = UiFactory.SolidSprite;
            _sheetImage.type = Image.Type.Simple;
            _sheetImage.color = SheetBodyColor;
            _sheetImage.raycastTarget = true;
            var sheetRect = sheet.GetComponent<RectTransform>();
            _sheetRect = sheetRect;
            sheetRect.anchorMin = new Vector2(0f, 0.5f);
            sheetRect.anchorMax = new Vector2(1f, 0.935f);
            sheetRect.offsetMin = Vector2.zero;
            sheetRect.offsetMax = Vector2.zero;
            sheetRect.localScale = Vector3.one;

            var sheetLayout = sheet.GetComponent<VerticalLayoutGroup>();
            sheetLayout.padding = new RectOffset(10, 10, 8, 8);
            sheetLayout.spacing = 5f;
            sheetLayout.childAlignment = TextAnchor.UpperCenter;
            sheetLayout.childControlWidth = true;
            sheetLayout.childControlHeight = true;
            sheetLayout.childForceExpandWidth = true;
            sheetLayout.childForceExpandHeight = false;

            var stripObject = new GameObject("ScoreStrip", typeof(RectTransform), typeof(Image), typeof(PlayerScoreStrip));
            stripObject.layer = gameObject.layer;
            stripObject.transform.SetParent(transform, false);
            _scoreStripTray = stripObject.GetComponent<Image>();
            _scoreStripTray.sprite = UiFactory.SolidSprite;
            _scoreStripTray.type = Image.Type.Simple;
            _scoreStripTray.color = StripTrayColor;
            _scoreStripTray.raycastTarget = false;
            _scoreStripRect = stripObject.GetComponent<RectTransform>();
            _scoreStrip = stripObject.GetComponent<PlayerScoreStrip>();

            var columns = new GameObject("Columns", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            columns.layer = gameObject.layer;
            columns.transform.SetParent(sheet.transform, false);
            var columnsLayout = columns.GetComponent<HorizontalLayoutGroup>();
            columnsLayout.spacing = 8f;
            columnsLayout.childAlignment = TextAnchor.UpperCenter;
            columnsLayout.childControlWidth = true;
            columnsLayout.childControlHeight = true;
            columnsLayout.childForceExpandWidth = true;
            columnsLayout.childForceExpandHeight = true;
            var columnsElement = columns.GetComponent<LayoutElement>();
            columnsElement.flexibleWidth = 1f;
            columnsElement.flexibleHeight = 6f;
            columnsElement.minHeight = 280f;

            scoreRows = new List<ScoreRowUI>(13);
            var left = CreateColumn(columns.transform);
            var right = CreateColumn(columns.transform);
            AddColumnRows(left, new[]
            {
                YahtzeeCategory.Ones,
                YahtzeeCategory.Twos,
                YahtzeeCategory.Threes,
                YahtzeeCategory.Fours,
                YahtzeeCategory.Fives,
                YahtzeeCategory.Sixes
            });
            AddColumnRows(right, new[]
            {
                YahtzeeCategory.ThreeOfAKind,
                YahtzeeCategory.FourOfAKind,
                YahtzeeCategory.FullHouse,
                YahtzeeCategory.SmallStraight,
                YahtzeeCategory.LargeStraight,
                YahtzeeCategory.Chance
            });

            var yahtzee = CreateScoreRow(sheet.transform, YahtzeeCategory.Yahtzee, 1);
            var yahtzeeRowElement = yahtzee.rowButton.GetComponent<LayoutElement>();
            yahtzeeRowElement.flexibleHeight = 1f;
            yahtzeeRowElement.minHeight = 56f;
            yahtzeeRowElement.preferredHeight = 64f;
            CenterRowContent(yahtzee);
            scoreRows.Add(yahtzee);

            upperScoreText = null;
            upperBonusText = null;
            totalScoreText = null;

            _commitButton = UiFactory.CreateButton(sheet.transform, "Записать");
            var commitElement = _commitButton.gameObject.AddComponent<LayoutElement>();
            commitElement.ignoreLayout = true;
            _commitRect = _commitButton.GetComponent<RectTransform>();
            _commitRect.anchorMin = new Vector2(0.5f, 0f);
            _commitRect.anchorMax = new Vector2(0.5f, 0f);
            _commitRect.pivot = new Vector2(0.5f, 0f);
            _commitRect.sizeDelta = new Vector2(420f, 72f);
            _commitRect.anchoredPosition = new Vector2(0f, 8f);
            _commitRect.localScale = Vector3.one;
            _commitButton.transform.SetAsLastSibling();
            _commitLabel = _commitButton.GetComponentInChildren<Text>();
            _commitButton.gameObject.SetActive(false);
            if (_commitLabel != null)
            {
                _commitLabel.resizeTextForBestFit = false;
                _commitLabel.alignment = TextAnchor.MiddleCenter;
                _commitLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
                _commitLabel.verticalOverflow = VerticalWrapMode.Overflow;
            }

            rollDiceButton = UiFactory.CreateButton(transform, "ПЕРЕБРОСИТЬ ВСЕ");
            UiFactory.StylePill(rollDiceButton);
            UiFactory.StylePill(_commitButton);
            var rollRect = rollDiceButton.GetComponent<RectTransform>();
            _rollRect = rollRect;
            rollRect.anchorMin = new Vector2(0.18f, 0.008f);
            rollRect.anchorMax = new Vector2(0.82f, 0.06f);
            rollRect.offsetMin = Vector2.zero;
            rollRect.offsetMax = Vector2.zero;
            rollRect.localScale = Vector3.one;
            rollsLeftText = rollDiceButton.GetComponentInChildren<Text>();
            if (rollsLeftText != null)
            {
                rollsLeftText.fontSize = 26;
                rollsLeftText.resizeTextForBestFit = false;
                rollsLeftText.horizontalOverflow = HorizontalWrapMode.Overflow;
                rollsLeftText.verticalOverflow = VerticalWrapMode.Overflow;
                rollsLeftText.alignment = TextAnchor.MiddleCenter;
            }

            EnsureOpponentTurnLabel();
            EnsureHandoffOverlay();
            EnsureSessionButtons();
            ApplyPlatformLayout();
        }

        private void EnsureOpponentTurnLabel()
        {
            if (_opponentTurnLabel != null || rollDiceButton == null)
                return;

            _opponentTurnLabel = UiFactory.CreateLabel(transform, "OpponentTurn", "Ходит противник", 26, TextAnchor.MiddleCenter);
            _opponentTurnLabel.fontStyle = FontStyle.Bold;
            _opponentTurnLabel.color = UIThemeConfig.TextAccent;
            _opponentTurnLabel.raycastTarget = false;
            var labelRect = _opponentTurnLabel.rectTransform;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            labelRect.localScale = Vector3.one;
            _opponentTurnLabel.gameObject.SetActive(false);
        }

        private static void AllowCrossPlatformOrientation()
        {
            // Phones and tablets: portrait only. Landscape layout is for PC / editor / Web desktop.
#if UNITY_ANDROID || UNITY_IOS
            Screen.autorotateToPortrait = true;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = false;
            Screen.autorotateToLandscapeRight = false;
            Screen.orientation = ScreenOrientation.Portrait;
#endif
        }

        private void OnRectTransformDimensionsChange()
        {
            // Never rebuild UI/layout here — setting anchors/offsets re-enters this callback.
            if (_layoutGuard || _sheetRect == null)
                return;

            var landscape = Screen.width > Screen.height;
            if (landscape != _layoutLandscape)
                _layoutQueued = true;
            else if (isActiveAndEnabled)
                _zonesQueued = true;
        }

        private void LateUpdate()
        {
            if (_layoutQueued)
            {
                _layoutQueued = false;
                _zonesQueued = false;
                ApplyPlatformLayout();
                if (isActiveAndEnabled)
                    FrameDiceZone(reapplyCamera: true);
                return;
            }

            if (!_zonesQueued || !isActiveAndEnabled)
                return;

            _zonesQueued = false;
            SyncBoardPlayZones();
        }

        private void ApplyPlatformLayout()
        {
            if (_layoutGuard)
                return;

            _layoutGuard = true;
            try
            {
                var canvas = GetComponentInParent<Canvas>();
                UiFactory.FitCanvas(canvas);
                ExpandGameplayCanvas(canvas);
                _layoutLandscape = Screen.width > Screen.height;
                var bottomPad = SafeBottomNorm();

                StripScoreSheetExtras();
                StyleOpaqueHudPanels();
                RestyleScoreValueCells();

                if (_sheetRect != null)
                {
                    // Portrait: sheet on top. Landscape: sheet on the right (free column left for 3D tray).
                    _sheetRect.anchorMin = _layoutLandscape ? new Vector2(0.64f, 0f) : new Vector2(0f, 0.5f);
                    _sheetRect.anchorMax = _layoutLandscape ? new Vector2(1f, 0.9f) : new Vector2(1f, 0.935f);
                    _sheetRect.offsetMin = Vector2.zero;
                    _sheetRect.offsetMax = Vector2.zero;
                }

                if (_scoreStripRect != null)
                {
                    _scoreStripRect.anchorMin = _layoutLandscape ? new Vector2(0f, 0.9f) : new Vector2(0f, 0.935f);
                    _scoreStripRect.anchorMax = new Vector2(1f, 1f);
                    _scoreStripRect.offsetMin = Vector2.zero;
                    _scoreStripRect.offsetMax = Vector2.zero;
                    _scoreStripRect.localScale = Vector3.one;
                    _scoreStripRect.SetAsLastSibling();
                }

                AlignBottomChrome(bottomPad);
                PlaceTutorialBanner();
                FitCategoryFonts();
                FitRollButtonFont();
                if (_scoreStrip != null)
                    _scoreStrip.FitText();
                if (scoreRows != null && scoreRows.Count > 0)
                    UpdateUI(ShownSheet, _dice, _rollsLeft);
                SyncBoardPlayZones();
                // Zones define crate left/right — snap buttons to the same rails.
                AlignBottomChrome(SafeBottomNorm());
            }
            finally
            {
                _layoutGuard = false;
            }
        }

        private void AlignBottomChrome(float bottomPad)
        {
            var rollBottom = (_layoutLandscape ? 0.018f : 0.014f) + bottomPad;
            var rollTop = (_layoutLandscape ? 0.1f : 0.09f) + bottomPad;
            var menuGap = _layoutLandscape ? 0.18f : 0.22f;
            var rollLeft = Mathf.Min(_playLeft + menuGap, _playRight - 0.25f);

            if (_rollRect != null)
            {
                _rollRect.anchorMin = new Vector2(rollLeft, rollBottom);
                _rollRect.anchorMax = new Vector2(_playRight, rollTop);
                _rollRect.offsetMin = Vector2.zero;
                _rollRect.offsetMax = Vector2.zero;
                if (_opponentTurnLabel != null)
                {
                    var labelRect = _opponentTurnLabel.rectTransform;
                    labelRect.anchorMin = _rollRect.anchorMin;
                    labelRect.anchorMax = _rollRect.anchorMax;
                    labelRect.offsetMin = Vector2.zero;
                    labelRect.offsetMax = Vector2.zero;
                }
            }

            PlaceSessionButtons();
        }

        public static void ApplyGameplayCamera()
        {
            var camera = Camera.main;
            if (camera == null)
                return;

            var landscape = Screen.width > Screen.height;
            if (landscape)
            {
                // Frame the left play column (screen left → score sheet). No yaw.
                camera.fieldOfView = 40f;
                camera.transform.SetPositionAndRotation(
                    new Vector3(0f, 5.2f, -3.4f),
                    Quaternion.Euler(55f, 0f, 0f));
            }
            else
            {
                camera.fieldOfView = 42f;
                camera.transform.SetPositionAndRotation(
                    new Vector3(0f, 6.1f, -2.35f),
                    Quaternion.Euler(68f, 0f, 0f));
            }

            CoverTable(camera);
            var gameplay = Instance != null && Instance.isActiveAndEnabled
                ? Instance
                : UnityEngine.Object.FindAnyObjectByType<GameplayScreen>(FindObjectsInactive.Exclude);
            if (gameplay != null)
                gameplay.SyncBoardPlayZones();
            else
            {
                var tray = UnityEngine.Object.FindAnyObjectByType<ProceduralDiceTray>();
                if (tray != null)
                    tray.ClearCustomMargins();
                var board = UnityEngine.Object.FindAnyObjectByType<GameBoardController>();
                if (board != null)
                    board.AdjustBordersToViewport();
            }
        }

        private static void CoverTable(Camera camera)
        {
            var tableObject = GameObject.Find("Table");
            if (tableObject == null)
                return;

            var planeY = tableObject.transform.position.y;
            var plane = new Plane(Vector3.up, new Vector3(0f, planeY, 0f));
            var minX = float.PositiveInfinity;
            var maxX = float.NegativeInfinity;
            var minZ = float.PositiveInfinity;
            var maxZ = float.NegativeInfinity;
            var hit = false;
            for (var y = 0; y < 2; y++)
            {
                for (var x = 0; x < 2; x++)
                {
                    var ray = camera.ViewportPointToRay(new Vector3(x, y, 0f));
                    if (!plane.Raycast(ray, out var distance))
                        continue;

                    var point = ray.GetPoint(distance);
                    hit = true;
                    if (point.x < minX)
                        minX = point.x;
                    if (point.x > maxX)
                        maxX = point.x;
                    if (point.z < minZ)
                        minZ = point.z;
                    if (point.z > maxZ)
                        maxZ = point.z;
                }
            }

            if (!hit)
                return;

            const float margin = 1.4f;
            var center = tableObject.transform.position;
            var halfX = Mathf.Max(Mathf.Abs(minX - center.x), Mathf.Abs(maxX - center.x)) + margin;
            var halfZ = Mathf.Max(Mathf.Abs(minZ - center.z), Mathf.Abs(maxZ - center.z)) + margin;
            var scale = tableObject.transform.localScale;
            tableObject.transform.localScale = new Vector3(
                Mathf.Max(1f, halfX / 5f),
                scale.y > 0f ? scale.y : 1f,
                Mathf.Max(1f, halfZ / 5f));
        }

        private void FrameDiceZone(bool reapplyCamera = true)
        {
            // Re-applying camera/CoverTable every throw rescales the table and shifts tray walls.
            if (reapplyCamera)
                ApplyGameplayCamera();

            if (dicePhysics == null)
                return;

            var tray = UnityEngine.Object.FindAnyObjectByType<ProceduralDiceTray>();
            if (tray != null && tray.TryGetCenter(out var trayCenter))
            {
                dicePhysics.PlaceThrowAbove(trayCenter);
                return;
            }

            var board = UnityEngine.Object.FindAnyObjectByType<GameBoardController>();
            if (board != null && board.TryPitCenter(out var pitCenter))
            {
                dicePhysics.PlaceThrowAbove(pitCenter);
                return;
            }

            var camera = Camera.main;
            if (camera == null)
                return;

            var landscape = Screen.width > Screen.height;
            var viewport = landscape ? new Vector3(0.3f, 0.42f, 0f) : new Vector3(0.5f, 0.28f, 0f);
            var ray = camera.ViewportPointToRay(viewport);
            var table = new Plane(Vector3.up, Vector3.zero);
            if (!table.Raycast(ray, out var distance))
                return;

            dicePhysics.PlaceThrowAbove(ray.GetPoint(distance));
        }

        public void SyncBoardPlayZones()
        {
            var board = UnityEngine.Object.FindAnyObjectByType<GameBoardController>();
            if (board == null)
                return;

            // Same selected tray style in bot / online / pass&play / bluetooth.
            board.ApplyPlayerTrayStyle();

            if (!TryComputePlayZones(out var pit, out var shelf))
            {
                board.AdjustBordersToViewport();
                return;
            }

            EnsureTrayBinder();
            if (_trayBinder != null)
            {
                // Gameplay owns the pit rect — binder must not overwrite it in LateUpdate.
                _trayBinder.SetGameplayDriven(true);
                _trayBinder.SetShelfTop(shelf.yMax);
            }

            // One source of truth: same pit rect for visual crate + physics, then pedestal under south lip.
            var tray = UnityEngine.Object.FindAnyObjectByType<ProceduralDiceTray>();
            if (tray != null)
                tray.ApplyNormalizedVisibleRect(pit);

            board.ApplyPlayZones(pit, shelf);
            board.AlignKeepBayToTray(shelf.height);
        }

        private void EnsureTrayBinder()
        {
            if (_trayBinder == null)
                _trayBinder = GetComponent<TrayUIBinder>();
            if (_trayBinder == null)
                _trayBinder = gameObject.AddComponent<TrayUIBinder>();

            var tray = UnityEngine.Object.FindAnyObjectByType<ProceduralDiceTray>();
            if (_sheetRect != null && tray != null)
                _trayBinder.Bind(_sheetRect, tray, syncNow: false);
        }

        private bool TryComputePlayZones(out Rect pitNorm, out Rect shelfNorm)
        {
            pitNorm = default;
            shelfNorm = default;
            if (Screen.width < 8 || Screen.height < 8)
                return false;

            Canvas.ForceUpdateCanvases();
            var landscape = Screen.width > Screen.height;
            var rollTop = landscape ? 0.11f : 0.1f;
            if (_rollRect != null && TryRectToScreenNorm(_rollRect, out var roll))
                rollTop = Mathf.Clamp(roll.yMax, 0.05f, 0.35f);

            float sheetBottom;
            float sheetTop;
            float sheetLeft;
            if (_sheetRect != null && TryRectToScreenNorm(_sheetRect, out var sheet))
            {
                sheetBottom = sheet.yMin;
                sheetTop = sheet.yMax;
                sheetLeft = sheet.xMin;
            }
            else
            {
                sheetBottom = landscape ? 0.2f : 0.5f;
                sheetTop = 0.9f;
                sheetLeft = landscape ? 0.64f : 0f;
            }

            float left;
            float right;
            float shelfBottom;
            float shelfTop;
            float pitBottom;
            float pitTop;

            // Keep bay under the crate: room for nests above the roll chrome.
            const float shelfHeight = 0.09f;
            const float gap = 0.008f;
            shelfBottom = Mathf.Clamp(rollTop + gap, 0.08f, 0.26f);
            shelfTop = shelfBottom + shelfHeight;

            const float uiClearance = 0.02f;
            if (landscape)
            {
                // Free column: left screen edge → score sheet (keep margins so walls stay on-screen).
                left = 0.02f;
                right = Mathf.Clamp(sheetLeft - 0.02f, left + 0.32f, 0.72f);
                pitTop = Mathf.Min(0.82f, 1f - 0.14f);
                if (shelfTop > pitTop - 0.22f)
                    shelfTop = Mathf.Max(shelfBottom + 0.06f, pitTop - 0.22f);
                pitBottom = shelfTop + gap;
            }
            else
            {
                left = 0.03f;
                right = 0.97f;
                pitTop = Mathf.Clamp(sheetBottom - uiClearance, 0.28f, 0.92f);
                if (shelfTop > pitTop - 0.22f)
                    shelfTop = Mathf.Max(shelfBottom + 0.06f, pitTop - 0.22f);
                pitBottom = shelfTop + gap;
            }

            if (right - left < 0.18f || shelfTop <= shelfBottom || pitTop <= pitBottom)
                return false;

            _playLeft = left;
            _playRight = right;
            shelfNorm = Rect.MinMaxRect(left, shelfBottom, right, shelfTop);
            pitNorm = Rect.MinMaxRect(left, pitBottom, right, pitTop);
            return pitNorm.height >= 0.14f && shelfNorm.height >= 0.05f;
        }

        private void StyleOpaqueHudPanels()
        {
            if (_sheetImage == null && _sheetRect != null)
                _sheetImage = _sheetRect.GetComponent<Image>();
            if (_sheetImage != null)
            {
                _sheetImage.sprite = UiFactory.PanelSprite;
                _sheetImage.type = Image.Type.Sliced;
                _sheetImage.color = SheetBodyColor;
                UiFactory.ClearChromeEffects(_sheetImage.gameObject);
                UiFactory.EnsureSoftShadow(_sheetImage.gameObject);
            }

            if (_scoreStripTray == null && _scoreStripRect != null)
                _scoreStripTray = _scoreStripRect.GetComponent<Image>();
            if (_scoreStripTray != null)
            {
                _scoreStripTray.sprite = UiFactory.PanelSprite;
                _scoreStripTray.type = Image.Type.Sliced;
                _scoreStripTray.color = StripTrayColor;
                UiFactory.ClearChromeEffects(_scoreStripTray.gameObject);
                UiFactory.EnsureSoftShadow(_scoreStripTray.gameObject);
            }
        }

        private void StripScoreSheetExtras()
        {
            if (_sheetRect == null)
                return;

            DestroyChild(_sheetRect, "BonusTrack");
            DestroyChild(_sheetRect, "Totals");
            DestroyChild(_sheetRect, "Section_Яцзы");

            var columns = _sheetRect.Find("Columns");
            if (columns != null)
            {
                for (var i = 0; i < columns.childCount; i++)
                {
                    var column = columns.GetChild(i);
                    DestroyChild(column, "Section_Верхняя секция");
                    DestroyChild(column, "Section_Нижняя секция");
                }
            }

            upperScoreText = null;
            upperBonusText = null;
            totalScoreText = null;
        }

        private void RestyleScoreValueCells()
        {
            if (scoreRows == null)
                return;

            for (var index = 0; index < scoreRows.Count; index++)
            {
                var row = scoreRows[index];
                if (row?.rowButton == null || row.scoreValueText == null)
                    continue;

                row.valuePlate = null;
                var outline = row.rowButton.GetComponent<Outline>();
                if (outline != null)
                    outline.enabled = false;

                var plate = row.rowButton.transform.Find("ValuePlate");
                if (plate != null)
                {
                    // Migrate legacy value labels out of the dark plate onto the row.
                    if (row.scoreValueText.transform.parent == plate)
                        row.scoreValueText.transform.SetParent(row.rowButton.transform, false);
                    var image = plate.GetComponent<Image>();
                    if (image != null)
                    {
                        image.enabled = false;
                        image.color = Color.clear;
                    }

                    plate.gameObject.SetActive(false);
                }

                var layout = row.rowButton.GetComponent<HorizontalLayoutGroup>();
                if (layout != null)
                {
                    layout.padding = new RectOffset(10, 10, 4, 4);
                    layout.childAlignment = TextAnchor.MiddleLeft;
                    layout.childForceExpandWidth = false;
                }

                var name = row.rowButton.transform.Find("Name");
                if (name != null)
                {
                    var nameElement = name.GetComponent<LayoutElement>();
                    if (nameElement != null)
                        nameElement.flexibleWidth = 1f;
                }

                var valueElement = row.scoreValueText.GetComponent<LayoutElement>();
                if (valueElement == null)
                    valueElement = row.scoreValueText.gameObject.AddComponent<LayoutElement>();
                valueElement.flexibleWidth = 0f;
                valueElement.minWidth = 36f;
                valueElement.preferredWidth = 52f;

                if (row.category == YahtzeeCategory.Yahtzee)
                {
                    row.scoreValueText.alignment = TextAlignmentOptions.Center;
                    continue;
                }

                row.scoreValueText.alignment = TextAlignmentOptions.Right;
                row.scoreValueText.enableAutoSizing = false;
                row.scoreValueText.transform.SetAsLastSibling();
            }
        }

        private static void DestroyChild(Transform parent, string childName)
        {
            var child = parent.Find(childName);
            if (child != null)
                UnityEngine.Object.Destroy(child.gameObject);
        }

        private static bool TryRectToScreenNorm(RectTransform rect, out Rect norm)
        {
            norm = default;
            if (rect == null)
                return false;

            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            var canvas = rect.GetComponentInParent<Canvas>();
            Camera eventCamera = null;
            if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                eventCamera = canvas.worldCamera != null ? canvas.worldCamera : Camera.main;

            var minX = float.PositiveInfinity;
            var maxX = float.NegativeInfinity;
            var minY = float.PositiveInfinity;
            var maxY = float.NegativeInfinity;
            for (var index = 0; index < 4; index++)
            {
                var screen = RectTransformUtility.WorldToScreenPoint(eventCamera, corners[index]);
                if (screen.x < minX)
                    minX = screen.x;
                if (screen.x > maxX)
                    maxX = screen.x;
                if (screen.y < minY)
                    minY = screen.y;
                if (screen.y > maxY)
                    maxY = screen.y;
            }

            if (Screen.width < 1 || Screen.height < 1)
                return false;

            norm = Rect.MinMaxRect(
                Mathf.Clamp01(minX / Screen.width),
                Mathf.Clamp01(minY / Screen.height),
                Mathf.Clamp01(maxX / Screen.width),
                Mathf.Clamp01(maxY / Screen.height));
            return norm.width > 0.02f && norm.height > 0.02f;
        }

        private Transform CreateColumn(Transform parent)
        {
            var column = new GameObject("Column", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            column.layer = gameObject.layer;
            column.transform.SetParent(parent, false);
            var layout = column.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 6f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            var element = column.GetComponent<LayoutElement>();
            element.flexibleWidth = 1f;
            element.flexibleHeight = 1f;
            return column.transform;
        }

        private void AddColumnRows(Transform column, YahtzeeCategory[] categories)
        {
            for (var index = 0; index < categories.Length; index++)
                scoreRows.Add(CreateScoreRow(column, categories[index], index));
        }

        private static void CenterRowContent(ScoreRowUI row)
        {
            if (row?.rowButton == null)
                return;

            var layout = row.rowButton.GetComponent<HorizontalLayoutGroup>();
            if (layout != null)
            {
                layout.childAlignment = TextAnchor.MiddleCenter;
                layout.childForceExpandWidth = false;
            }

            var name = row.rowButton.transform.Find("Name");
            if (name != null)
            {
                var nameElement = name.GetComponent<LayoutElement>();
                if (nameElement != null)
                {
                    nameElement.minWidth = 0f;
                    nameElement.preferredWidth = -1f;
                    nameElement.flexibleWidth = 0f;
                }
            }

            if (row.scoreValueText != null)
            {
                var valueElement = row.scoreValueText.GetComponent<LayoutElement>();
                if (valueElement != null)
                {
                    valueElement.minWidth = 0f;
                    valueElement.preferredWidth = -1f;
                    valueElement.flexibleWidth = 0f;
                }

                row.scoreValueText.alignment = TextAlignmentOptions.Center;
            }
        }

        private ScoreRowUI CreateScoreRow(Transform parent, YahtzeeCategory category, int stripe)
        {
            var rowObject = new GameObject(category.ToString(), typeof(RectTransform), typeof(Image), typeof(Button), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            rowObject.layer = gameObject.layer;
            rowObject.transform.SetParent(parent, false);
            var background = rowObject.GetComponent<Image>();
            background.sprite = UiFactory.RoundSprite;
            background.type = Image.Type.Sliced;
            background.color = stripe % 2 == 0 ? RowStripeA : RowStripeB;
            background.raycastTarget = true;

            var rowButton = rowObject.GetComponent<Button>();
            rowButton.targetGraphic = background;
            rowButton.transition = Selectable.Transition.None;

            var rowLayout = rowObject.GetComponent<HorizontalLayoutGroup>();
            rowLayout.padding = new RectOffset(10, 10, 4, 4);
            rowLayout.spacing = 8f;
            rowLayout.childAlignment = TextAnchor.MiddleLeft;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = true;

            var rowElement = rowObject.GetComponent<LayoutElement>();
            rowElement.flexibleWidth = 1f;
            rowElement.flexibleHeight = 1f;
            rowElement.minHeight = 52f;
            rowElement.preferredHeight = 60f;

            var caption = UiFactory.CreateAutoLabel(rowObject.transform, "Name", CategoryLabel(category), TextAlignmentOptions.MidlineLeft);
            LockCategoryFont(caption);
            var captionElement = caption.gameObject.AddComponent<LayoutElement>();
            captionElement.flexibleWidth = 1f;
            captionElement.minWidth = 88f;
            captionElement.preferredWidth = 160f;

            // Score sits on the row itself so it can flush to the cell's right edge.
            var value = UiFactory.CreateAutoLabel(
                rowObject.transform,
                "Value",
                string.Empty,
                TextAlignmentOptions.Right);
            LockCategoryFont(value);
            value.enableAutoSizing = false;
            var valueElement = value.gameObject.AddComponent<LayoutElement>();
            valueElement.minWidth = 36f;
            valueElement.preferredWidth = 52f;
            valueElement.flexibleWidth = 0f;
            valueElement.minHeight = 28f;

            return new ScoreRowUI
            {
                category = category,
                rowButton = rowButton,
                rowBackground = background,
                valuePlate = null,
                scoreValueText = value,
                stripe = stripe
            };
        }

        private TextMeshProUGUI CreateStatChip(Transform parent, string caption, string value)
        {
            var chip = new GameObject(caption, typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            chip.layer = gameObject.layer;
            chip.transform.SetParent(parent, false);
            var chipImage = chip.GetComponent<Image>();
            chipImage.sprite = UiFactory.RoundSprite;
            chipImage.type = Image.Type.Sliced;
            chipImage.color = StatChipColor;
            chipImage.raycastTarget = false;

            var layout = chip.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 6f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            layout.padding = new RectOffset(8, 8, 2, 2);

            var element = chip.GetComponent<LayoutElement>();
            element.flexibleWidth = 1f;
            element.minHeight = 34f;

            var name = UiFactory.CreateAutoLabel(chip.transform, "Name", caption, TextAlignmentOptions.MidlineLeft);
            name.color = UiFactory.MutedText;
            var nameElement = name.gameObject.AddComponent<LayoutElement>();
            nameElement.flexibleWidth = 1f;

            var number = UiFactory.CreateAutoLabel(chip.transform, "Value", value, TextAlignmentOptions.MidlineRight);
            number.fontStyle = FontStyles.Bold;
            var numberElement = number.gameObject.AddComponent<LayoutElement>();
            numberElement.minWidth = 28f;
            numberElement.preferredWidth = 44f;
            numberElement.flexibleWidth = 0f;
            return number;
        }

        private static string CategoryLabel(YahtzeeCategory category)
        {
            switch (category)
            {
                case YahtzeeCategory.Ones: return "Единицы";
                case YahtzeeCategory.Twos: return "Двойки";
                case YahtzeeCategory.Threes: return "Тройки";
                case YahtzeeCategory.Fours: return "Четвёрки";
                case YahtzeeCategory.Fives: return "Пятёрки";
                case YahtzeeCategory.Sixes: return "Шестёрки";
                case YahtzeeCategory.ThreeOfAKind: return "Тройка";
                case YahtzeeCategory.FourOfAKind: return "Каре";
                case YahtzeeCategory.FullHouse: return "Фулл-хаус";
                case YahtzeeCategory.SmallStraight: return "Малый стрит";
                case YahtzeeCategory.LargeStraight: return "Большой стрит";
                case YahtzeeCategory.Yahtzee: return YahtzeeConfig.Current.GameName;
                case YahtzeeCategory.Chance: return "Шанс";
                default: return category.ToString();
            }
        }

        public override void Show()
        {
            base.Show();
            var board = UnityEngine.Object.FindAnyObjectByType<GameBoardController>();
            if (board != null)
                board.ApplyPlayerTrayStyle(force: true);
            ApplyPlatformLayout();
            FrameDiceZone();
            BindRows();
            BeginSession();
            UpdateUI(ShownSheet, _dice, _rollsLeft);
        }

        public override void Hide()
        {
            RestoreCanvasSafeArea(GetComponentInParent<Canvas>());
            base.Hide();
        }

        private void OnEnable()
        {
            NetworkManager.OnDiceRolledEvent += HandleDiceRolled;
            NetworkManager.OnDiceHeld += HandleDiceHeld;
            DieSelectable.SelectionChanged += OnDieSelectionChanged;
            NetworkManager.OnTurnChanged += HandleTurnChanged;
            NetworkManager.OnMatchStarted += HandleMatchStarted;
            NetworkManager.OnMatchStateCaughtUp += HandleMatchState;
            NetworkManager.OnOpponentDisconnected += HandleOpponentDisconnected;
            NetworkManager.OnPlayerReplacedByBot += HandleReplacedByBot;
            NetworkManager.OnPlayerLeft += HandlePlayerLeft;
            NetworkManager.OnPlayerReconnected += HandlePlayerReconnected;
            NetworkManager.OnMatchOver += HandleMatchOver;
            LocalNetworkManager.Instance.LocalDiceRolled += HandleLocalDice;
            LocalNetworkManager.Instance.LocalDiceHeld += HandleLocalHold;
            LocalNetworkManager.Instance.LocalCategorySelected += HandleLocalScore;
            if (BluetoothNetworkManager.IsSupported)
            {
                var radio = BluetoothNetworkManager.Instance;
                radio.DiceRolled += HandleBluetoothDice;
                radio.DiceHeld += HandleBluetoothHold;
                radio.CategorySelected += HandleBluetoothScore;
            }
            if (rollDiceButton != null)
                rollDiceButton.onClick.AddListener(OnRollClicked);
            if (_commitButton != null)
                _commitButton.onClick.AddListener(OnCommitClicked);
        }

        private void OnDisable()
        {
            NetworkManager.OnDiceRolledEvent -= HandleDiceRolled;
            NetworkManager.OnDiceHeld -= HandleDiceHeld;
            DieSelectable.SelectionChanged -= OnDieSelectionChanged;
            DieSelectable.ClicksEnabled = true;
            NetworkManager.OnTurnChanged -= HandleTurnChanged;
            NetworkManager.OnMatchStarted -= HandleMatchStarted;
            NetworkManager.OnMatchStateCaughtUp -= HandleMatchState;
            NetworkManager.OnOpponentDisconnected -= HandleOpponentDisconnected;
            NetworkManager.OnPlayerReplacedByBot -= HandleReplacedByBot;
            NetworkManager.OnPlayerLeft -= HandlePlayerLeft;
            NetworkManager.OnPlayerReconnected -= HandlePlayerReconnected;
            NetworkManager.OnMatchOver -= HandleMatchOver;
            if (LocalNetworkManager.HasInstance)
            {
                LocalNetworkManager.Instance.LocalDiceRolled -= HandleLocalDice;
                LocalNetworkManager.Instance.LocalDiceHeld -= HandleLocalHold;
                LocalNetworkManager.Instance.LocalCategorySelected -= HandleLocalScore;
            }

            if (BluetoothNetworkManager.HasInstance)
            {
                var radio = BluetoothNetworkManager.Instance;
                radio.DiceRolled -= HandleBluetoothDice;
                radio.DiceHeld -= HandleBluetoothHold;
                radio.CategorySelected -= HandleBluetoothScore;
            }

            if (rollDiceButton != null)
                rollDiceButton.onClick.RemoveListener(OnRollClicked);
            if (_commitButton != null)
                _commitButton.onClick.RemoveListener(OnCommitClicked);
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public void InitializeMatchMode(bool online, string matchId)
        {
            RetirePlay();
            PassAndPlayManager.Close();
            if (LocalNetworkManager.HasInstance)
                LocalNetworkManager.Instance.StopSession();
            if (BluetoothNetworkManager.HasInstance)
                BluetoothNetworkManager.Instance.StopSession();

            SetHandoffVisible(false);
            _botTurn = false;
            _busy = false;
            _hasSelection = false;
            _tableMode = TableMode.Bot;

            var matchNumber = 0;
            var hasMatch = online
                && int.TryParse(matchId, NumberStyles.Integer, CultureInfo.InvariantCulture, out matchNumber)
                && matchNumber > 0;
            _online = hasMatch;
            _seenMatchId = hasMatch ? -1 : 0;
        }

        private bool ActionsLocked => _waitingDisconnect;

        public void UpdateUI(Scorecard scorecard, int[] currentDice, int rollsLeft)
        {
            var canControl = _ourTurn && !_busy && !_botTurn && !ActionsLocked;
            DieSelectable.ClicksEnabled = canControl && rollsLeft > 0 && rollsLeft < RollsPerTurn;
            var sheet = scorecard ?? _scorecard;
            var dice = currentDice ?? Array.Empty<int>();
            var canScore = dice.Length == DiceGameEngine.DiceCount;
            _dice = dice;
            _rollsLeft = rollsLeft;
            if (!canScore || _botTurn || !_ourTurn || ActionsLocked)
                _hasSelection = false;

            var selectedOpen = false;
            for (var index = 0; index < scoreRows.Count; index++)
            {
                var row = scoreRows[index];
                if (row == null)
                    continue;

                int? committed = null;
                if (sheet.Scores.TryGetValue(row.category, out var stored))
                    committed = stored;

                var tutorialTarget = _tutorial
                    && CurrentTutorialStep() != null
                    && row.category == CurrentTutorialStep().Category
                    && !committed.HasValue;
                var selected = _hasSelection && row.category == _selectedCategory && !committed.HasValue;
                if (selected)
                    selectedOpen = true;

                if (committed.HasValue)
                {
                    PaintRow(row, selected, true, tutorialTarget, false);
                    WriteScore(row.scoreValueText, committed.Value, CommittedScoreColor, true);
                    if (row.rowButton != null)
                        row.rowButton.interactable = false;
                    continue;
                }

                if (canScore && (_ourTurn || _botTurn) && !ActionsLocked)
                {
                    var hint = DiceGameEngine.CalculateScore(row.category, dice, _shownSheet ?? _scorecard);
                    var valuable = hint > 0;
                    PaintRow(row, selected, false, tutorialTarget, valuable);
                    var hintColor = selected || tutorialTarget
                        ? UIThemeConfig.TextAccent
                        : (valuable ? UIThemeConfig.ScoreHint : HintScoreColor);
                    WriteScore(row.scoreValueText, hint, hintColor, false);
                    if (row.rowButton != null)
                    {
                        var canPick = canControl;
                        if (_tutorial)
                            canPick = canPick && tutorialTarget;
                        row.rowButton.interactable = canPick;
                    }
                    continue;
                }

                PaintRow(row, selected, false, tutorialTarget, false);

                WriteHint(row.scoreValueText, "–");

                if (row.rowButton != null)
                    row.rowButton.interactable = false;
            }

            if (!selectedOpen)
                _hasSelection = false;

            if (_commitLabel != null)
            {
                _commitLabel.text = _hasSelection
                    ? "Записать: " + CategoryLabel(_selectedCategory)
                    : "Записать";
            }

            if (_commitButton != null)
            {
                var showCommit = _hasSelection && canControl && canScore;
                if (_commitButton.gameObject.activeSelf != showCommit)
                    _commitButton.gameObject.SetActive(showCommit);
                _commitButton.interactable = showCommit;
            }

            if (_tutorial)
                PlaceTutorialBanner();
            RaiseCommitOverTutorialHint();

            var opponentTurn = OpponentIsPlaying() || ActionsLocked;
            if (rollsLeftText != null && !opponentTurn)
                rollsLeftText.text = RollCaption(rollsLeft, AnyDieKept());
            if (rollDiceButton != null)
            {
                if (rollDiceButton.gameObject.activeSelf == opponentTurn)
                    rollDiceButton.gameObject.SetActive(!opponentTurn);
                rollDiceButton.interactable = !opponentTurn && !_busy && rollsLeft > 0 && !ActionsLocked;
            }

            if (_opponentTurnLabel != null && _opponentTurnLabel.gameObject.activeSelf != opponentTurn)
                _opponentTurnLabel.gameObject.SetActive(opponentTurn);

            RefreshScoreboard();
            Canvas.ForceUpdateCanvases();
            FitCategoryFonts();
            FitRollButtonFont();
            if (_scoreStrip != null)
                _scoreStrip.FitText();
        }

        private void RefreshScoreboard()
        {
            if (_scoreStrip == null)
                return;

            var slots = new PlayerScoreSlot[PlayerScoreStrip.Capacity];
            if (_tableMode == TableMode.PassAndPlay && PassAndPlayManager.Active != null)
            {
                var match = PassAndPlayManager.Active;
                for (var index = 0; index < match.PlayerCount && index < slots.Length; index++)
                {
                    slots[index] = new PlayerScoreSlot
                    {
                        Name = WithTurnClock(match.NameOf(index), !match.AwaitingHandoff && !match.MatchOver && index == match.CurrentIndex),
                        Score = match.Sheets[index].GetTotalScore(),
                        Visible = true,
                        Active = !match.AwaitingHandoff && !match.MatchOver && index == match.CurrentIndex,
                        Local = index == match.CurrentIndex
                    };
                }

                _scoreStrip.Show(slots);
                return;
            }

            if (_tableMode == TableMode.Bluetooth && BluetoothNetworkManager.HasInstance && BluetoothNetworkManager.Instance.InMatch)
            {
                var radio = BluetoothNetworkManager.Instance;
                for (var index = 0; index < radio.PlayerCount && index < slots.Length; index++)
                {
                    var sheet = radio.SheetForSeat(index);
                    slots[index] = new PlayerScoreSlot
                    {
                        Name = WithTurnClock(radio.NameOf(index), radio.TurnSeat == index),
                        Score = sheet.GetTotalScore(),
                        Visible = true,
                        Active = radio.TurnSeat == index,
                        Local = index == radio.LocalSeat
                    };
                }

                _scoreStrip.Show(slots);
                return;
            }

            if (_tableMode == TableMode.LocalNetwork && LocalNetworkManager.HasInstance && LocalNetworkManager.Instance.InMatch)
            {
                var table = LocalNetworkManager.Instance;
                for (var index = 0; index < table.PlayerCount && index < slots.Length; index++)
                {
                    var sheet = table.SheetForSeat(index);
                    slots[index] = new PlayerScoreSlot
                    {
                        Name = WithTurnClock(table.NameOf(index), table.TurnSeat == index),
                        Score = sheet.GetTotalScore(),
                        Visible = true,
                        Active = table.TurnSeat == index,
                        Local = index == table.LocalSeat
                    };
                }

                _scoreStrip.Show(slots);
                return;
            }

            if (!_online)
            {
                var playerTurn = _ourTurn && !_botTurn;
                slots[0] = new PlayerScoreSlot
                {
                    Name = WithTurnClock("Вы", playerTurn),
                    Score = _scorecard.GetTotalScore(),
                    Visible = true,
                    Active = playerTurn,
                    Local = true
                };
                slots[1] = new PlayerScoreSlot
                {
                    Name = WithTurnClock(_tutorial ? "Тренер" : "Бот", _botTurn),
                    Score = _botScorecard.GetTotalScore(),
                    Visible = true,
                    Active = _botTurn,
                    Bot = true
                };
                _scoreStrip.Show(slots);
                return;
            }

            if (_trackedCount == 0)
            {
                slots[0] = new PlayerScoreSlot
                {
                    Name = WithTurnClock("Вы", _ourTurn),
                    Score = _scorecard.GetTotalScore(),
                    Visible = true,
                    Active = _ourTurn,
                    Local = true
                };
            }
            else
            {
                for (var index = 0; index < _trackedCount; index++)
                {
                    var seat = _tracked[index];
                    var turn = IsSeatTurn(seat);
                    slots[index] = new PlayerScoreSlot
                    {
                        Name = WithTurnClock(SeatName(seat), turn),
                        Score = seat.IsLocal ? _scorecard.GetTotalScore() : seat.Score,
                        Visible = true,
                        Active = turn,
                        Local = seat.IsLocal,
                        Bot = seat.IsBot
                    };
                }
            }

            _scoreStrip.Show(slots);
        }

        private bool IsSeatTurn(TrackedSeat seat)
        {
            if (_currentTurnUserId.HasValue)
                return seat.UserId == _currentTurnUserId;

            return seat.IsLocal && _ourTurn;
        }

        private static string SeatName(TrackedSeat seat)
        {
            if (!string.IsNullOrWhiteSpace(seat.Name))
                return seat.Name;

            if (seat.IsLocal)
                return "Вы";

            return seat.IsBot ? "Бот" : "Игрок";
        }

        private string WithTurnClock(string name, bool active)
        {
            if (!active || _clockShown < 0)
                return name;

            var nick = string.IsNullOrWhiteSpace(name) ? "Игрок" : name.Trim();
            var clock = _clockShown.ToString(CultureInfo.InvariantCulture);
            // Keep seconds visible on narrow strip cards — shorten nick, never the timer.
            const int maxNick = 12;
            if (nick.Length > maxNick)
                nick = nick.Substring(0, maxNick - 1) + "…";

            return nick + " " + clock;
        }

        private void BindRows()
        {
            if (_rowsBound)
                return;

            _rowsBound = true;
            for (var index = 0; index < scoreRows.Count; index++)
            {
                var row = scoreRows[index];
                if (row == null || row.rowButton == null)
                    continue;

                var category = row.category;
                row.rowButton.onClick.AddListener(() => OnRowClicked(category));
            }
        }

        private void BeginPassAndPlay()
        {
            var match = PassAndPlayManager.Active;
            _online = false;
            _tableMode = TableMode.PassAndPlay;
            _botTurn = false;
            _shownSheet = null;
            HideDecision();
            HideAbsence();
            StartTurnClock(null);
            if (_seenPassSession == match.SessionId)
                return;

            _seenPassSession = match.SessionId;
            _busy = false;
            _hasSelection = false;
            _ourTurn = false;
            _rollsLeft = 0;
            _dice = Array.Empty<int>();
            ClearPhysicalDice();
            _blankSheet.Reset();
            PresentHandoff(match.CurrentName, false);
        }

        private void BeginBluetoothTable()
        {
            var radio = BluetoothNetworkManager.Instance;
            _online = false;
            _tableMode = TableMode.Bluetooth;
            _botTurn = false;
            _shownSheet = null;
            HideDecision();
            HideAbsence();
            StartTurnClock(null);
            SetHandoffVisible(false);
            if (_seenBluetoothSession == radio.SessionId)
                return;

            _seenBluetoothSession = radio.SessionId;
            _busy = false;
            _hasSelection = false;
            _ourTurn = radio.IsLocalTurn;
            _rollsLeft = _ourTurn ? radio.RollsLeft : 0;
            _dice = radio.CurrentDice;
            if (_dice.Length == 0)
                ClearPhysicalDice();
        }

        private void RollOverBluetooth()
        {
            var radio = BluetoothNetworkManager.Instance;
            if (!radio.IsLocalTurn || _rollsLeft <= 0)
                return;

            var keep = HeldMask();
            var throwing = 0;
            for (var index = 0; index < keep.Length; index++)
            {
                if (!keep[index])
                    throwing++;
            }

            if (throwing == 0 && _dice != null && _dice.Length == DiceGameEngine.DiceCount)
                return;

            _busy = true;
            UpdateUI(ShownSheet, _dice, _rollsLeft);
            radio.SubmitRoll(keep);
        }

        private void CommitBluetooth(YahtzeeCategory category)
        {
            var radio = BluetoothNetworkManager.Instance;
            if (!radio.IsLocalTurn)
                return;

            _busy = true;
            _hasSelection = false;
            UpdateUI(ShownSheet, _dice, _rollsLeft);
            if (radio.IsHost && !radio.SubmitCategory(category))
            {
                _busy = false;
                UpdateUI(ShownSheet, _dice, _rollsLeft);
            }
            else if (!radio.IsHost)
            {
                radio.SubmitCategory(category);
            }
        }

        private async void HandleBluetoothDice(BluetoothDiceRolled rolled)
        {
            if (!isActiveAndEnabled || _tableMode != TableMode.Bluetooth || rolled == null)
                return;

            var radio = BluetoothNetworkManager.Instance;
            var rolls = radio.IsLocalTurn ? rolled.RollsLeft : 0;
            try
            {
                _ourTurn = radio.IsLocalTurn;
                RememberRemoteHold(rolled.Held);
                await PublishDice(rolled.Dice, rolls, true);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Yahtzee bluetooth roll: " + exception.Message);
            }
            finally
            {
                _busy = false;
                if (this != null)
                    UpdateUI(ShownSheet, _dice, _rollsLeft);
            }
        }

        private void HandleBluetoothScore(BluetoothCategorySelected selected)
        {
            if (!isActiveAndEnabled || _tableMode != TableMode.Bluetooth || selected == null)
                return;

            var radio = BluetoothNetworkManager.Instance;
            _busy = false;
            _hasSelection = false;
            _ourTurn = radio.IsLocalTurn;
            _dice = radio.CurrentDice;
            _rollsLeft = _ourTurn ? radio.RollsLeft : 0;
            if (_dice.Length == 0)
                ClearPhysicalDice();

            if (selected.NextSeat < 0)
            {
                UpdateUI(ShownSheet, _dice, 0);
                PresentLocalFinish();
                return;
            }

            SetHandoffVisible(false);
            UpdateUI(ShownSheet, _dice, _rollsLeft);
        }

        private void HandleBluetoothHold(int seat, bool[] keep)
        {
            if (!isActiveAndEnabled || _tableMode != TableMode.Bluetooth)
                return;

            var radio = BluetoothNetworkManager.Instance;
            if (radio.IsLocalTurn || seat == radio.LocalSeat)
                return;

            RememberRemoteHold(keep);
            if (!_diceFlying)
                PaintRemoteHold();
        }

        private void BeginLocalTable()
        {
            var table = LocalNetworkManager.Instance;
            _online = false;
            _tableMode = TableMode.LocalNetwork;
            _botTurn = false;
            _shownSheet = null;
            HideDecision();
            HideAbsence();
            StartTurnClock(null);
            SetHandoffVisible(false);
            if (_seenLanSession == table.SessionId)
                return;

            _seenLanSession = table.SessionId;
            _busy = false;
            _hasSelection = false;
            _ourTurn = table.IsLocalTurn;
            _rollsLeft = _ourTurn ? table.RollsLeft : 0;
            _dice = table.CurrentDice;
            if (_dice.Length == 0)
                ClearPhysicalDice();
        }

        private void RollOverNetwork()
        {
            if (!LocalNetworkManager.HasInstance)
                return;

            var table = LocalNetworkManager.Instance;
            if (!table.IsLocalTurn || _rollsLeft <= 0)
                return;

            var keep = HeldMask();
            var throwing = 0;
            for (var index = 0; index < keep.Length; index++)
            {
                if (!keep[index])
                    throwing++;
            }

            if (throwing == 0 && _dice != null && _dice.Length == DiceGameEngine.DiceCount)
                return;

            _busy = true;
            UpdateUI(ShownSheet, _dice, _rollsLeft);
            table.SubmitRoll(keep);
        }

        private void CommitPassAndPlay(YahtzeeCategory category)
        {
            var match = PassAndPlayManager.Active;
            if (match == null || !match.TryScore(category, _dice))
                return;

            PlayScoreFeedback(category, DiceGameEngine.CalculateScore(category, _dice, _scorecard));
            _hasSelection = false;
            _dice = Array.Empty<int>();
            ClearPhysicalDice();
            _ourTurn = false;
            _rollsLeft = 0;
            _blankSheet.Reset();
            if (match.MatchOver)
            {
                UpdateUI(match.CurrentSheet, _dice, 0);
                PresentPassAndPlayResults(match);
                return;
            }

            UpdateUI(_blankSheet, _dice, 0);
            PresentHandoff(match.CurrentName, false);
        }

        private void CommitLocalNetwork(YahtzeeCategory category)
        {
            if (!LocalNetworkManager.HasInstance)
                return;

            var table = LocalNetworkManager.Instance;
            if (!table.IsLocalTurn)
                return;

            _busy = true;
            _hasSelection = false;
            UpdateUI(ShownSheet, _dice, _rollsLeft);
            if (table.IsHost && !table.SubmitCategory(category))
            {
                _busy = false;
                UpdateUI(ShownSheet, _dice, _rollsLeft);
            }
            else if (!table.IsHost)
            {
                table.SubmitCategory(category);
            }
        }

        private async void HandleLocalDice(LocalDiceRolled rolled)
        {
            if (!isActiveAndEnabled || _tableMode != TableMode.LocalNetwork || rolled == null)
                return;

            var table = LocalNetworkManager.Instance;
            var rolls = table.IsLocalTurn ? rolled.RollsLeft : 0;
            try
            {
                _ourTurn = table.IsLocalTurn;
                RememberRemoteHold(rolled.Held);
                await PublishDice(rolled.Dice, rolls, true);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Yahtzee local roll: " + exception.Message);
            }
            finally
            {
                _busy = false;
                if (this != null)
                    UpdateUI(ShownSheet, _dice, _rollsLeft);
            }
        }

        private void HandleLocalScore(LocalCategorySelected selected)
        {
            if (!isActiveAndEnabled || _tableMode != TableMode.LocalNetwork || selected == null)
                return;

            var table = LocalNetworkManager.Instance;
            _busy = false;
            _hasSelection = false;
            _ourTurn = table.IsLocalTurn;
            _dice = table.CurrentDice;
            _rollsLeft = _ourTurn ? table.RollsLeft : 0;
            if (_dice.Length == 0)
                ClearPhysicalDice();

            if (selected.NextSeat < 0)
            {
                UpdateUI(ShownSheet, _dice, 0);
                PresentLocalFinish();
                return;
            }

            SetHandoffVisible(false);
            UpdateUI(ShownSheet, _dice, _rollsLeft);
        }

        private void PresentHandoff(string playerName, bool matchOver)
        {
            _handoffReturnsToMenu = matchOver;
            _ourTurn = false;
            var body = matchOver
                ? "Партия окончена\n" + PassAndPlayManager.Active.StandingsText()
                : "Ход игрока " + playerName + ".\nПередайте устройство!";
            SetHandoff(body, matchOver ? "В меню" : "Я готов, поехали!", true);
        }

        private void PresentLocalFinish()
        {
            var rows = new System.Collections.Generic.List<MatchResultRow>();
            if (_tableMode == TableMode.Bluetooth && BluetoothNetworkManager.HasInstance)
            {
                var radio = BluetoothNetworkManager.Instance;
                for (var index = 0; index < radio.PlayerCount; index++)
                {
                    rows.Add(new MatchResultRow
                    {
                        Nickname = radio.NameOf(index),
                        Score = radio.SheetForSeat(index).GetTotalScore(),
                        IsLocal = index == radio.LocalSeat
                    });
                }

                BluetoothNetworkManager.Instance.StopSession();
            }
            else if (LocalNetworkManager.HasInstance)
            {
                var table = LocalNetworkManager.Instance;
                for (var index = 0; index < table.PlayerCount; index++)
                {
                    rows.Add(new MatchResultRow
                    {
                        Nickname = table.NameOf(index),
                        Score = table.SheetForSeat(index).GetTotalScore(),
                        IsLocal = index == table.LocalSeat
                    });
                }

                LocalNetworkManager.Instance.StopSession();
            }

            rows.Sort((a, b) => b.Score.CompareTo(a.Score));
            MatchResultsBridge.Present(rows, "Партия окончена", false);
        }

        private void PresentPassAndPlayResults(PassAndPlayManager match)
        {
            var rows = new System.Collections.Generic.List<MatchResultRow>();
            for (var index = 0; index < match.PlayerCount; index++)
            {
                rows.Add(new MatchResultRow
                {
                    Nickname = match.NameOf(index),
                    Score = match.Sheets[index].GetTotalScore(),
                    IsLocal = true
                });
            }

            rows.Sort((a, b) => b.Score.CompareTo(a.Score));
            PassAndPlayManager.Close();
            MatchResultsBridge.Present(rows, "Партия окончена", false);
        }

        private void PresentBotResults()
        {
            var playerScore = _scorecard.GetTotalScore();
            var botScore = _botScorecard.GetTotalScore();
            var won = playerScore >= botScore;
            var rows = new System.Collections.Generic.List<MatchResultRow>
            {
                new MatchResultRow
                {
                    Nickname = "Вы",
                    Score = playerScore,
                    IsLocal = true
                },
                new MatchResultRow
                {
                    Nickname = "Бот",
                    Score = botScore,
                    IsBot = true
                }
            };
            rows.Sort((a, b) => b.Score.CompareTo(a.Score));
            MatchResultsBridge.Present(rows, won ? "Победа!" : "Партия окончена", true, () =>
            {
                PrepareBotChoice();
                if (UIManager.Instance != null)
                    UIManager.Instance.SwitchToScreen(ScreenType.Gameplay);
            });
        }

        private void EnsureHandoffOverlay()
        {
            if (_handoffRoot != null)
                return;

            _handoffRoot = new GameObject("Handoff", typeof(RectTransform), typeof(Image));
            _handoffRoot.layer = gameObject.layer;
            _handoffRoot.transform.SetParent(transform, false);
            var image = _handoffRoot.GetComponent<Image>();
            image.color = new Color(0.1f, 0.14f, 0.22f, 0.42f);
            image.raycastTarget = true;
            var rect = _handoffRoot.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;

            var label = UiFactory.CreateLabel(_handoffRoot.transform, "Prompt", "Ход игрока", 32, TextAnchor.MiddleCenter);
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 18;
            label.resizeTextMaxSize = 40;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            UiFactory.Place(label.rectTransform, 0.08f, 0.42f, 0.92f, 0.78f);
            _handoffLabel = label;

            var button = UiFactory.CreateButton(_handoffRoot.transform, "Я готов, поехали!");
            UiFactory.StyleAccent(button);
            UiFactory.Place(button.GetComponent<RectTransform>(), 0.12f, 0.18f, 0.88f, 0.34f);
            _handoffButtonLabel = button.GetComponentInChildren<Text>();
            button.onClick.AddListener(OnHandoffClicked);
            _handoffRoot.SetActive(false);
        }

        private void SetHandoff(string message, string button, bool visible)
        {
            EnsureHandoffOverlay();
            if (_handoffLabel != null)
                _handoffLabel.text = message;
            if (_handoffButtonLabel != null)
                _handoffButtonLabel.text = button;

            SetHandoffVisible(visible);
        }

        private void SetHandoffVisible(bool visible)
        {
            if (_handoffRoot == null)
                return;

            _handoffRoot.SetActive(visible);
            if (visible)
                _handoffRoot.transform.SetAsLastSibling();
        }

        private void OnHandoffClicked()
        {
            if (_handoffReturnsToMenu)
            {
                DiscardLocalSessions();
                if (NetworkManager.Instance != null)
                    NetworkManager.Instance.ForgetLocalMatch();

                SetHandoffVisible(false);
                if (UIManager.Instance != null)
                    UIManager.Instance.SwitchToScreen(ScreenType.MainMenu);
                return;
            }

            var match = PassAndPlayManager.Active;
            if (match == null)
                return;

            match.ConfirmReady();
            _handoffReturnsToMenu = false;
            _ourTurn = true;
            _busy = false;
            _rollsLeft = RollsPerTurn;
            _dice = Array.Empty<int>();
            ClearPhysicalDice();
            SetHandoffVisible(false);
            UpdateUI(match.CurrentSheet, _dice, _rollsLeft);
        }

        public static void PrepareBotChoice()
        {
            _pendingBotChoice = true;
        }

        public static void PrepareTutorial()
        {
            TutorialCampaign.Prepare();
            DiscardLocalSessions();
        }

        private void BeginSession()
        {
            RetirePlay();
            _onlineResultsShown = false;
            var chooseBot = _pendingBotChoice;
            _pendingBotChoice = false;
            if (!chooseBot)
                SetDifficultyVisible(false);

            if (PassAndPlayManager.Active != null)
            {
                if (PassAndPlayManager.Active.MatchOver)
                    PassAndPlayManager.Close();
                else
                {
                    BeginPassAndPlay();
                    return;
                }
            }

            if (BluetoothNetworkManager.HasInstance && BluetoothNetworkManager.Instance.InMatch)
            {
                if (BluetoothNetworkManager.Instance.TurnSeat < 0)
                    BluetoothNetworkManager.Instance.StopSession();
                else
                {
                    BeginBluetoothTable();
                    return;
                }
            }

            if (LocalNetworkManager.HasInstance && LocalNetworkManager.Instance.InMatch)
            {
                if (LocalNetworkManager.Instance.TurnSeat < 0)
                    LocalNetworkManager.Instance.StopSession();
                else
                {
                    BeginLocalTable();
                    return;
                }
            }

            _tableMode = TableMode.Bot;
            SetHandoffVisible(false);
            var network = NetworkManager.Instance;
            _online = network != null && network.IsAuthenticated && network.ActiveMatchId.HasValue;
            if (_online && network.ResumeState != null && network.ResumeState.Match != null
                && (network.ResumeState.Match.Status == "finished" || network.ResumeState.Match.Status == "cancelled"))
            {
                network.ForgetLocalMatch();
                _online = false;
            }

            if (_online && network.ResumeState != null && network.ResumeState.Match != null)
            {
                var resume = MatchReconnectResponseDto.From(network.ResumeState);
                network.ClearResumeState();
                _ = RestoreReconnectedMatch(resume, true);
                return;
            }

            if (_online && network.ActiveMatchId.Value != _seenMatchId)
            {
                _seenMatchId = network.ActiveMatchId.Value;
                _scorecard.Reset();
                _shownSheet = _scorecard;
                _botTurn = false;
                _dice = Array.Empty<int>();
                ClearPhysicalDice();
                var userId = network.CurrentUser != null ? (int?)network.CurrentUser.Id : null;
                var match = network.LastQuickMatch != null ? network.LastQuickMatch.Match : null;
                RememberMatch(match, userId);
                ApplySeat(match, userId);
                _ourTurn = userId.HasValue && match != null && match.CurrentPlayerId == userId;
                _rollsLeft = _ourTurn ? RollsPerTurn : 0;
                if (network.LastMatchStarted != null && network.LastMatchStarted.MatchId == _seenMatchId)
                    StartTurnClock(network.LastMatchStarted.TurnExpiresAt);
                UpdateUI(_scorecard, _dice, _rollsLeft);
                _ = SyncTurnClockFromServer();
                return;
            }

            if (_online)
                return;

            _seenMatchId = 0;
            _scorecard.Reset();
            _botScorecard.Reset();
            _shownSheet = _scorecard;
            _botTurn = false;
            _trackedCount = 0;
            _currentTurnUserId = null;
            _dice = Array.Empty<int>();
            ClearPhysicalDice();
            _ourTurn = true;
            _rollsLeft = RollsPerTurn;
            if (TutorialCampaign.Pending)
            {
                TutorialCampaign.ConsumePending();
                StartTutorialLesson();
                return;
            }

            _tutorial = false;
            SetTutorialBannerVisible(false);
            if (chooseBot)
            {
                _ourTurn = false;
                _rollsLeft = 0;
                _busy = true;
                ShowBotDifficulty();
                return;
            }

            _opponent.SetDifficulty(AIOpponent.RequestedDifficulty);
        }

        private void StartTutorialLesson()
        {
            _tutorial = true;
            _tutorialStep = 0;
            _tutorialRollIndex = 0;
            _busy = false;
            _ourTurn = true;
            _botTurn = false;
            _hasSelection = false;
            _opponent.SetDifficulty(AIDifficulty.Beginner);
            _rollsLeft = RollsPerTurn;
            EnsureTutorialBanner();
            RefreshTutorialHint();
            UpdateUI(_scorecard, _dice, _rollsLeft);
        }

        private void ShowBotDifficulty()
        {
            EnsureBotDifficulty();
            _difficultyRoot.SetActive(true);
            _difficultyRoot.transform.SetAsLastSibling();
        }

        private void EnsureBotDifficulty()
        {
            if (_difficultyRoot != null)
                return;

            _difficultyRoot = new GameObject("BotDifficulty", typeof(RectTransform), typeof(Image));
            _difficultyRoot.layer = gameObject.layer;
            _difficultyRoot.transform.SetParent(transform, false);
            var shade = _difficultyRoot.GetComponent<Image>();
            shade.color = new Color(0.1f, 0.14f, 0.22f, 0.42f);
            shade.raycastTarget = true;
            var root = _difficultyRoot.GetComponent<RectTransform>();
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            var card = new GameObject("Card", typeof(RectTransform), typeof(Image));
            card.layer = gameObject.layer;
            card.transform.SetParent(_difficultyRoot.transform, false);
            card.GetComponent<Image>().color = UIThemeConfig.PanelBgSolid;
            UiFactory.EnsureSoftShadow(card);
            UiFactory.Place(card.GetComponent<RectTransform>(), 0.16f, 0.22f, 0.84f, 0.78f);

            var title = UiFactory.CreateLabel(card.transform, "Title", "Сложность бота", 36, TextAnchor.MiddleCenter);
            UiFactory.Place(title.rectTransform, 0.08f, 0.78f, 0.92f, 0.94f);
            var hint = UiFactory.CreateLabel(card.transform, "Hint", "Выберите, с кем играть", 22, TextAnchor.MiddleCenter);
            UiFactory.Place(hint.rectTransform, 0.08f, 0.64f, 0.92f, 0.76f);

            var beginner = UiFactory.CreateButton(card.transform, "Новичок");
            UiFactory.Place(beginner.GetComponent<RectTransform>(), 0.1f, 0.42f, 0.9f, 0.58f);
            UiFactory.StyleAccent(beginner);
            beginner.onClick.AddListener(() => StartBotMatch(AIDifficulty.Beginner));

            var professional = UiFactory.CreateButton(card.transform, "Профессионал");
            UiFactory.Place(professional.GetComponent<RectTransform>(), 0.1f, 0.24f, 0.9f, 0.4f);
            UiFactory.StyleAccent(professional);
            professional.onClick.AddListener(() => StartBotMatch(AIDifficulty.Professional));

            var back = UiFactory.CreateButton(card.transform, "Назад");
            UiFactory.Place(back.GetComponent<RectTransform>(), 0.1f, 0.06f, 0.9f, 0.2f);
            UiFactory.StyleGlass(back);
            back.onClick.AddListener(LeaveBotChoice);
            _difficultyRoot.SetActive(false);
        }

        private void StartBotMatch(AIDifficulty difficulty)
        {
            _pendingBotChoice = false;
            _busy = false;
            _ourTurn = true;
            _rollsLeft = RollsPerTurn;
            AIOpponent.ChooseForNextMatch(difficulty);
            if (_opponent != null)
                _opponent.SetDifficulty(difficulty);
            SetDifficultyVisible(false);
            UpdateUI(ShownSheet, _dice, _rollsLeft);
        }

        private void LeaveBotChoice()
        {
            _pendingBotChoice = false;
            _busy = false;
            SetDifficultyVisible(false);
            ReturnToMainMenu();
        }

        private void EnsureSessionButtons()
        {
            if (_closeToMenuButton == null)
            {
                _closeToMenuButton = UiFactory.CreateButton(transform, "В меню");
                UiFactory.StyleGlass(_closeToMenuButton);
                _closeToMenuButton.onClick.AddListener(OnCloseToMenuClicked);
            }

            if (_resignRoot != null)
                return;

            _resignRoot = new GameObject("ResignConfirm", typeof(RectTransform), typeof(Image));
            _resignRoot.layer = gameObject.layer;
            _resignRoot.transform.SetParent(transform, false);
            var shade = _resignRoot.GetComponent<Image>();
            shade.color = new Color(0.1f, 0.14f, 0.22f, 0.42f);
            shade.raycastTarget = true;
            var root = _resignRoot.GetComponent<RectTransform>();
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            var card = new GameObject("Card", typeof(RectTransform), typeof(Image));
            card.layer = gameObject.layer;
            card.transform.SetParent(_resignRoot.transform, false);
            card.GetComponent<Image>().color = UIThemeConfig.PanelBgSolid;
            UiFactory.EnsureSoftShadow(card);
            UiFactory.Place(card.GetComponent<RectTransform>(), 0.12f, 0.28f, 0.88f, 0.74f);

            var prompt = UiFactory.CreateLabel(
                card.transform,
                "Prompt",
                "Вы уверены, что хотите выйти? Вам будет засчитано поражение",
                28,
                TextAnchor.MiddleCenter);
            prompt.horizontalOverflow = HorizontalWrapMode.Wrap;
            prompt.verticalOverflow = VerticalWrapMode.Overflow;
            UiFactory.Place(prompt.rectTransform, 0.08f, 0.42f, 0.92f, 0.9f);

            var yes = UiFactory.CreateButton(card.transform, "Да");
            UiFactory.Place(yes.GetComponent<RectTransform>(), 0.1f, 0.22f, 0.48f, 0.38f);
            UiFactory.StyleDestructive(yes);
            yes.onClick.AddListener(ConfirmResign);

            var no = UiFactory.CreateButton(card.transform, "Нет");
            UiFactory.Place(no.GetComponent<RectTransform>(), 0.52f, 0.22f, 0.9f, 0.38f);
            UiFactory.StyleGlass(no);
            no.onClick.AddListener(DismissResign);
            _resignRoot.SetActive(false);
        }

        private void PlaceSessionButtons()
        {
            if (_closeToMenuButton == null)
                return;

            var menu = _closeToMenuButton.GetComponent<RectTransform>();
            var bottomPad = SafeBottomNorm();
            var rollBottom = (_layoutLandscape ? 0.018f : 0.014f) + bottomPad;
            var rollTop = (_layoutLandscape ? 0.1f : 0.09f) + bottomPad;
            var menuRight = Mathf.Min(_playLeft + (_layoutLandscape ? 0.17f : 0.21f), _playRight - 0.3f);
            UiFactory.Place(menu, _playLeft, rollBottom, menuRight, rollTop);
        }

        private static float SafeBottomNorm()
        {
            if (Screen.height <= 0)
                return 0f;

            return Mathf.Clamp01(Screen.safeArea.yMin / Screen.height);
        }

        private static void ExpandGameplayCanvas(Canvas canvas)
        {
            if (canvas == null)
                return;

            var fit = canvas.GetComponent<SafeAreaFit>();
            if (fit != null)
                fit.enabled = false;

            var root = canvas.transform as RectTransform;
            if (root == null)
                return;

            // Skip no-op writes — they still fire OnRectTransformDimensionsChange and recurse.
            var zero = Vector2.zero;
            var one = Vector2.one;
            var pivot = new Vector2(0.5f, 0.5f);
            if (root.anchorMin == zero
                && root.anchorMax == one
                && root.pivot == pivot
                && root.offsetMin == zero
                && root.offsetMax == zero)
                return;

            root.anchorMin = zero;
            root.anchorMax = one;
            root.pivot = pivot;
            root.offsetMin = zero;
            root.offsetMax = zero;
        }

        private static void RestoreCanvasSafeArea(Canvas canvas)
        {
            if (canvas == null)
                return;

            var fit = canvas.GetComponent<SafeAreaFit>();
            if (fit == null)
                return;

            fit.enabled = true;
            fit.ForceApply();
        }

        private void OnCloseToMenuClicked()
        {
            if (OnlineMatchOpen())
            {
                ShowResignConfirm();
                return;
            }

            ReturnToMainMenu();
        }

        private bool OnlineMatchOpen()
        {
            return _online
                && NetworkManager.Instance != null
                && NetworkManager.Instance.ActiveMatchId.HasValue;
        }

        private void ShowResignConfirm()
        {
            EnsureSessionButtons();
            _resignRoot.SetActive(true);
            _resignRoot.transform.SetAsLastSibling();
        }

        private void DismissResign()
        {
            if (_resignRoot != null)
                _resignRoot.SetActive(false);
        }

        private async void ConfirmResign()
        {
            if (_resigning)
                return;

            _resigning = true;
            DismissResign();
            try
            {
                if (NetworkManager.Instance != null && NetworkManager.Instance.ActiveMatchId.HasValue)
                    await NetworkManager.Instance.ResignMatch();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Yahtzee resign: " + exception.Message);
                _resigning = false;
                return;
            }

            _resigning = false;
            ReturnToMainMenu();
        }

        private void ReturnToMainMenu()
        {
            if (_tutorial)
                TutorialCampaign.MarkSeen();
            _tutorial = false;
            SetTutorialBannerVisible(false);
            _pendingBotChoice = false;
            _busy = false;
            SetDifficultyVisible(false);
            DismissResign();
            DiscardLocalSessions();
            if (NetworkManager.Instance != null)
                NetworkManager.Instance.ForgetLocalMatch();
            if (UIManager.Instance != null)
                UIManager.Instance.SwitchToScreen(ScreenType.MainMenu);
        }

        private void SetDifficultyVisible(bool visible)
        {
            if (_difficultyRoot != null)
                _difficultyRoot.SetActive(visible);
        }

        private async void OnRollClicked()
        {
            if (_busy || !_ourTurn || _rollsLeft <= 0 || ActionsLocked)
                return;

            _hasSelection = false;

            if (_tableMode == TableMode.Bluetooth)
            {
                RollOverBluetooth();
                return;
            }

            if (_tableMode == TableMode.LocalNetwork)
            {
                RollOverNetwork();
                return;
            }

            if (!_online)
            {
                if (_tutorial)
                {
                    var lesson = CurrentTutorialStep();
                    if (lesson == null || _tutorialRollIndex >= lesson.Rolls.Length)
                        return;
                }

                var epoch = _playEpoch;
                _busy = true;
                try
                {
                    await RollLocal();
                }
                finally
                {
                    if (this != null && epoch == _playEpoch)
                    {
                        _busy = false;
                        UpdateUI(ShownSheet, _dice, _rollsLeft);
                    }
                }

                return;
            }

            if (NetworkManager.Instance == null)
                return;

            var reroll = RerollIndexes();
            if (reroll.Length == 0)
                return;

            _busy = true;
            _animateNextDice = true;
            UpdateUI(_scorecard, _dice, _rollsLeft);
            try
            {
                var state = await NetworkManager.Instance.RollDice(reroll);
                if (this != null)
                    await ApplyGameState(state);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Yahtzee roll: " + exception.Message);
            }
            finally
            {
                _animateNextDice = false;
                _busy = false;
                if (this != null)
                    UpdateUI(_scorecard, _dice, _rollsLeft);
            }
        }

        private void PaintRow(ScoreRowUI row, bool selected, bool committed, bool tutorialTarget = false, bool valuableHint = false)
        {
            if (row.rowBackground == null)
                return;

            var idle = row.stripe % 2 == 0 ? RowStripeA : RowStripeB;
            var outline = row.rowButton != null ? row.rowButton.GetComponent<Outline>() : null;

            if (committed)
            {
                if (outline != null)
                    outline.enabled = false;
                if (row.scoreValueText != null)
                    row.scoreValueText.fontStyle = FontStyles.Bold;
                if (!row.settledVisual)
                {
                    row.settledVisual = true;
                    StartCoroutine(FadeRow(row.rowBackground, RowCommittedColor));
                }

                return;
            }

            row.settledVisual = false;
            var highlight = selected || tutorialTarget || valuableHint;
            if (selected)
                row.rowBackground.color = RowSelectedColor;
            else if (tutorialTarget || valuableHint)
                row.rowBackground.color = RowTutorialColor;
            else
                row.rowBackground.color = idle;

            if (highlight)
            {
                if (outline == null && row.rowButton != null)
                    outline = row.rowButton.gameObject.AddComponent<Outline>();
                if (outline != null)
                {
                    outline.effectColor = UIThemeConfig.RowHintBorder;
                    outline.effectDistance = new Vector2(1.5f, -1.5f);
                    outline.useGraphicAlpha = true;
                    outline.enabled = true;
                }

                if (row.scoreValueText != null)
                    row.scoreValueText.fontStyle = FontStyles.Bold;
            }
            else
            {
                if (outline != null)
                    outline.enabled = false;
                if (row.scoreValueText != null)
                    row.scoreValueText.fontStyle = FontStyles.Normal;
            }
        }

        private static IEnumerator FadeRow(Image image, Color target)
        {
            var start = image != null ? image.color : target;
            const float duration = 0.28f;
            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                if (image == null)
                    yield break;

                image.color = Color.Lerp(start, target, Mathf.Clamp01(elapsed / duration));
                yield return null;
            }

            if (image != null)
                image.color = target;
        }

        private void OnRowClicked(YahtzeeCategory category)
        {
            if (_busy || !_ourTurn || _botTurn || ActionsLocked || _dice == null || _dice.Length != DiceGameEngine.DiceCount)
                return;

            if (ShownSheet.Scores.TryGetValue(category, out var existing) && existing.HasValue)
                return;

            var lesson = CurrentTutorialStep();
            if (_tutorial && lesson != null && category != lesson.Category)
            {
                RefreshTutorialHint("Сейчас учим «" + TutorialCampaign.CategoryTitle(lesson.Category) + "». Выберите подсвеченную строку.");
                return;
            }

            if (_hasSelection && _selectedCategory == category)
                _hasSelection = false;
            else
            {
                _hasSelection = true;
                _selectedCategory = category;
            }

            UpdateUI(ShownSheet, _dice, _rollsLeft);
        }

        private void OnCommitClicked()
        {
            if (!_hasSelection)
                return;

            if (_commitButton != null)
                UiMotion.Punch(_commitButton.transform);
            var category = _selectedCategory;
            _hasSelection = false;
            CommitCategory(category);
        }

        private static void PlayScoreFeedback(YahtzeeCategory category, int score)
        {
            if (category == YahtzeeCategory.Yahtzee && score > 0)
                GameAudio.PlayYahtzee();
            else
                GameAudio.PlayScoreWrite();
        }

        private async void CommitCategory(YahtzeeCategory category)
        {
            if (_busy || !_ourTurn || ActionsLocked || _dice == null || _dice.Length != DiceGameEngine.DiceCount)
                return;

            if (_scorecard.Scores.TryGetValue(category, out var existing) && existing.HasValue)
                return;

            if (_tableMode == TableMode.PassAndPlay)
            {
                CommitPassAndPlay(category);
                return;
            }

            if (_tableMode == TableMode.Bluetooth)
            {
                CommitBluetooth(category);
                return;
            }

            if (_tableMode == TableMode.LocalNetwork)
            {
                CommitLocalNetwork(category);
                return;
            }

            var required = DiceGameEngine.RequiredUpper(_dice, _scorecard);
            if (required.HasValue && required.Value != category)
            {
                Debug.LogWarning("Yahtzee classic: must score " + required.Value);
                return;
            }

            var score = DiceGameEngine.CalculateScore(category, _dice, _scorecard);
            PlayScoreFeedback(category, score);
            if (!_online)
            {
                if (_tutorial)
                {
                    CommitTutorialCategory(category, score);
                    return;
                }

                if (!_scorecard.RegisterScore(category, score, _dice))
                    return;

                _dice = Array.Empty<int>();
                ClearPhysicalDice();
                _busy = true;
                _ourTurn = false;
                _botTurn = !_botScorecard.IsFullyCompleted();
                UpdateUI(_scorecard, _dice, 0);
                var epoch = _playEpoch;
                try
                {
                    if (!_botScorecard.IsFullyCompleted())
                        await _opponent.PlayTurn(this, dicePhysics, _botScorecard, epoch);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("Yahtzee bot: " + exception.Message);
                }
                finally
                {
                    if (this != null && epoch == _playEpoch && _tableMode == TableMode.Bot)
                    {
                        _botTurn = false;
                        _shownSheet = _scorecard;
                        _busy = false;
                        _dice = Array.Empty<int>();
                        var finished = _scorecard.IsFullyCompleted() && _botScorecard.IsFullyCompleted();
                        _ourTurn = !finished;
                        _rollsLeft = finished ? 0 : RollsPerTurn;
                        ClearPhysicalDice();
                        UpdateUI(_scorecard, _dice, _rollsLeft);
                        if (finished)
                            PresentBotResults();
                    }
                }

                return;
            }

            if (NetworkManager.Instance == null)
                return;

            _busy = true;
            UpdateUI(_scorecard, _dice, _rollsLeft);
            try
            {
                var state = await NetworkManager.Instance.SelectCategory(ToApiName(category), score);
                if (this != null)
                    await ApplyGameState(state);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Yahtzee score: " + exception.Message);
            }
            finally
            {
                _busy = false;
                if (this != null)
                    UpdateUI(_scorecard, _dice, _rollsLeft);
            }
        }

        private async Task RollLocal()
        {
            if (_tutorial)
            {
                await RollTutorial();
                return;
            }

            var count = DiceGameEngine.DiceCount;
            if (_dice == null || _dice.Length != count)
                _dice = new int[count];

            var keep = HeldMask();
            var throwing = 0;
            for (var index = 0; index < count; index++)
            {
                if (keep[index])
                    continue;

                throwing++;
                _dice[index] = UnityEngine.Random.Range(1, 7);
            }

            if (throwing == 0)
                return;

            _rollsLeft--;
            await PublishDice(_dice, _rollsLeft, true);
        }

        private TutorialStep CurrentTutorialStep()
        {
            if (!_tutorial || _tutorialStep < 0 || _tutorialStep >= TutorialCampaign.Steps.Length)
                return null;

            return TutorialCampaign.Steps[_tutorialStep];
        }

        private async Task RollTutorial()
        {
            var step = CurrentTutorialStep();
            if (step == null || _tutorialRollIndex >= step.Rolls.Length)
                return;

            var faces = step.Rolls[_tutorialRollIndex];
            if (faces == null || faces.Length != DiceGameEngine.DiceCount)
                return;

            _dice = (int[])faces.Clone();
            _tutorialRollIndex++;
            _rollsLeft = _tutorialRollIndex >= step.Rolls.Length
                ? 0
                : Mathf.Max(1, RollsPerTurn - _tutorialRollIndex);
            await PublishDice(_dice, _rollsLeft, true);
            SuggestTutorialHolds(step);
            RefreshTutorialHint();
        }

        private void SuggestTutorialHolds(TutorialStep step)
        {
            if (dicePhysics == null || step == null || _tutorialRollIndex >= step.Rolls.Length)
                return;

            var next = step.Rolls[_tutorialRollIndex];
            if (next == null || _dice == null || next.Length != _dice.Length)
                return;

            var keep = new System.Collections.Generic.List<int>(DiceGameEngine.DiceCount);
            for (var index = 0; index < _dice.Length; index++)
            {
                if (_dice[index] == next[index])
                    keep.Add(index);
            }

            dicePhysics.SetKeptDice(keep.ToArray());
        }

        private void CommitTutorialCategory(YahtzeeCategory category, int score)
        {
            var step = CurrentTutorialStep();
            if (step == null)
                return;

            if (category != step.Category)
            {
                RefreshTutorialHint("Сейчас учим «" + TutorialCampaign.CategoryTitle(step.Category) + "». Выберите подсвеченную строку.");
                return;
            }

            if (!_scorecard.RegisterScore(category, score))
                return;

            _dice = Array.Empty<int>();
            ClearPhysicalDice();
            _hasSelection = false;
            _tutorialStep++;
            _tutorialRollIndex = 0;

            if (_tutorialStep >= TutorialCampaign.Steps.Length)
            {
                PresentTutorialComplete();
                return;
            }

            _ourTurn = true;
            _botTurn = false;
            _busy = false;
            _rollsLeft = RollsPerTurn;
            RefreshTutorialHint();
            UpdateUI(_scorecard, _dice, _rollsLeft);
        }

        private void PresentTutorialComplete()
        {
            TutorialCampaign.MarkSeen();
            _tutorial = false;
            SetTutorialBannerVisible(false);
            var rows = new System.Collections.Generic.List<MatchResultRow>
            {
                new MatchResultRow
                {
                    Nickname = "Вы",
                    Score = _scorecard.GetTotalScore(),
                    IsLocal = true
                },
                new MatchResultRow
                {
                    Nickname = "Тренер",
                    Score = 0,
                    IsBot = true
                }
            };
            MatchResultsBridge.Present(rows, "Обучение пройдено!", true, () =>
            {
                PrepareBotChoice();
                if (UIManager.Instance != null)
                    UIManager.Instance.SwitchToScreen(ScreenType.Gameplay);
            }, "С ботом");
        }

        private void EnsureTutorialBanner()
        {
            if (_tutorialBanner != null)
            {
                // Rebuild if an older broken banner is still parented to the sheet.
                if (_tutorialBanner.transform.parent == _sheetRect)
                {
                    Destroy(_tutorialBanner);
                    _tutorialBanner = null;
                    _tutorialTitle = null;
                    _tutorialBody = null;
                }
                else
                {
                    SetTutorialBannerVisible(true);
                    PlaceTutorialBanner();
                    return;
                }
            }

            _tutorialBanner = new GameObject(
                "TutorialBanner",
                typeof(RectTransform),
                typeof(Image),
                typeof(VerticalLayoutGroup));
            _tutorialBanner.layer = gameObject.layer;
            _tutorialBanner.transform.SetParent(transform, false);

            var image = _tutorialBanner.GetComponent<Image>();
            image.sprite = UiFactory.SolidSprite;
            image.type = Image.Type.Simple;
            image.color = UIThemeConfig.PanelBgSolid;
            image.raycastTarget = true;
            UiFactory.ClearChromeEffects(_tutorialBanner);
            var border = _tutorialBanner.AddComponent<Outline>();
            border.effectColor = UIThemeConfig.BorderSubtle;
            border.effectDistance = new Vector2(2f, -2f);
            border.useGraphicAlpha = true;

            var stack = _tutorialBanner.GetComponent<VerticalLayoutGroup>();
            stack.padding = new RectOffset(16, 16, 12, 12);
            stack.spacing = 8f;
            stack.childAlignment = TextAnchor.UpperLeft;
            stack.childControlWidth = true;
            stack.childControlHeight = true;
            stack.childForceExpandWidth = true;
            stack.childForceExpandHeight = false;

            var header = new GameObject("Header", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            header.layer = gameObject.layer;
            header.transform.SetParent(_tutorialBanner.transform, false);
            var headerLayout = header.GetComponent<HorizontalLayoutGroup>();
            headerLayout.spacing = 10f;
            headerLayout.childAlignment = TextAnchor.MiddleCenter;
            headerLayout.childControlWidth = true;
            headerLayout.childControlHeight = true;
            headerLayout.childForceExpandWidth = true;
            headerLayout.childForceExpandHeight = true;
            var headerElement = header.GetComponent<LayoutElement>();
            headerElement.minHeight = 44f;
            headerElement.preferredHeight = 48f;
            headerElement.flexibleWidth = 1f;

            _tutorialTitle = UiFactory.CreateLabel(header.transform, "Title", "Обучение", 26, TextAnchor.MiddleLeft);
            _tutorialTitle.fontStyle = FontStyle.Bold;
            _tutorialTitle.color = UIThemeConfig.TextAccent;
            _tutorialTitle.raycastTarget = false;
            var titleElement = _tutorialTitle.gameObject.AddComponent<LayoutElement>();
            titleElement.flexibleWidth = 1f;
            titleElement.minWidth = 80f;

            var skip = UiFactory.CreateButton(header.transform, "Закончить");
            UiFactory.StyleGlass(skip);
            var skipElement = skip.gameObject.AddComponent<LayoutElement>();
            skipElement.preferredWidth = 168f;
            skipElement.minWidth = 140f;
            skipElement.flexibleWidth = 0f;
            skipElement.minHeight = 40f;
            skipElement.preferredHeight = 44f;
            skip.onClick.AddListener(GameAudio.PlayUiClick);
            skip.onClick.AddListener(SkipTutorial);

            _tutorialBody = UiFactory.CreateLabel(_tutorialBanner.transform, "Body", string.Empty, 24, TextAnchor.UpperLeft);
            _tutorialBody.horizontalOverflow = HorizontalWrapMode.Wrap;
            _tutorialBody.verticalOverflow = VerticalWrapMode.Overflow;
            _tutorialBody.color = UIThemeConfig.TextPrimary;
            _tutorialBody.raycastTarget = false;
            var bodyElement = _tutorialBody.gameObject.AddComponent<LayoutElement>();
            bodyElement.minHeight = 56f;
            bodyElement.flexibleHeight = 1f;
            bodyElement.flexibleWidth = 1f;

            PlaceTutorialBanner();
            SetTutorialBannerVisible(true);
        }

        private bool TutorialTargetNeedsLowerHint()
        {
            var step = CurrentTutorialStep();
            if (step == null)
                return true;

            // Цель вверху/середине → подсказка ниже таблицы.
            // Цель внизу таблицы → подсказка у верхнего края экрана.
            switch (step.Category)
            {
                case YahtzeeCategory.SmallStraight:
                case YahtzeeCategory.LargeStraight:
                case YahtzeeCategory.Chance:
                case YahtzeeCategory.Yahtzee:
                    return false;
                default:
                    return true;
            }
        }

        private void PlaceTutorialBanner()
        {
            if (_tutorialBanner == null || !_tutorial)
                return;

            var parent = transform as RectTransform;
            var banner = _tutorialBanner.GetComponent<RectTransform>();
            if (parent == null || banner == null)
                return;

            if (banner.parent != parent)
                banner.SetParent(parent, false);

            var landscape = Screen.width > Screen.height;
            if (landscape)
            {
                // Left column (dice side), keep clear of the right-hand score sheet.
                if (TutorialTargetNeedsLowerHint())
                    UiFactory.Place(banner, 0.04f, 0.10f, 0.58f, 0.30f);
                else
                    UiFactory.Place(banner, 0.04f, 0.70f, 0.58f, 0.90f);
            }
            else
            {
                // Portrait: sit between sheet and dice tray / at the top edge.
                if (TutorialTargetNeedsLowerHint())
                    UiFactory.Place(banner, 0.04f, 0.36f, 0.96f, 0.50f);
                else
                    UiFactory.Place(banner, 0.04f, 0.88f, 0.96f, 0.98f);
            }

            banner.offsetMin = Vector2.zero;
            banner.offsetMax = Vector2.zero;
            banner.SetAsLastSibling();
            RaiseCommitOverTutorialHint();
        }

        private void RaiseCommitOverTutorialHint()
        {
            if (!_tutorial || _commitButton == null || _commitRect == null)
                return;
            if (!_commitButton.gameObject.activeSelf)
                return;

            var screen = transform as RectTransform;
            if (screen == null)
                return;

            // Commit must share the gameplay root with the hint and sit above it.
            if (_commitRect.parent != screen)
                _commitRect.SetParent(screen, false);

            if (_layoutLandscape)
                UiFactory.Place(_commitRect, 0.66f, 0.02f, 0.98f, 0.13f);
            else
                UiFactory.Place(_commitRect, 0.08f, 0.40f, 0.92f, 0.50f);

            _commitRect.offsetMin = Vector2.zero;
            _commitRect.offsetMax = Vector2.zero;
            _commitRect.SetAsLastSibling();
        }

        private void SkipTutorial()
        {
            if (!_tutorial)
                return;

            ReturnToMainMenu();
        }

        private void SetTutorialBannerVisible(bool visible)
        {
            if (_tutorialBanner != null)
                _tutorialBanner.SetActive(visible);
            if (visible)
                PlaceTutorialBanner();
        }

        private void RefreshTutorialHint(string overrideBody = null)
        {
            EnsureTutorialBanner();
            var step = CurrentTutorialStep();
            if (step == null)
            {
                SetTutorialBannerVisible(false);
                return;
            }

            var lesson = (_tutorialStep + 1).ToString(CultureInfo.InvariantCulture)
                + "/" + TutorialCampaign.Steps.Length.ToString(CultureInfo.InvariantCulture);
            if (_tutorialTitle != null)
                _tutorialTitle.text = "Обучение · " + lesson + " · " + step.Title;

            if (_tutorialBody != null)
            {
                if (!string.IsNullOrEmpty(overrideBody))
                    _tutorialBody.text = overrideBody;
                else if (_tutorialRollIndex == 0)
                    _tutorialBody.text = step.Hint + " Нажмите «Бросок».";
                else if (_tutorialRollIndex < step.Rolls.Length)
                    _tutorialBody.text = step.HoldHint;
                else
                    _tutorialBody.text = TutorialCampaign.ScoreHint(step);
            }

            PlaceTutorialBanner();
        }

        private bool ListensToOnlineMatch()
        {
            return _online && _tableMode == TableMode.Bot;
        }

        private void HandleDiceRolled(DiceRolledEvent rolled)
        {
            if (!isActiveAndEnabled || !ListensToOnlineMatch() || rolled == null)
                return;

            var userId = LocalUserId();
            if (userId.HasValue && rolled.UserId == userId)
                return;

            var faces = rolled.Dice;
            if (faces == null || faces.Length != DiceGameEngine.DiceCount)
                return;

            _ourTurn = false;
            _dice = faces;
            _rollsLeft = rolled.RollsRemaining;
            if (!string.IsNullOrEmpty(rolled.TurnExpiresAt))
                StartTurnClock(rolled.TurnExpiresAt);
            RememberRemoteHold(rolled.Held);
            _ = PublishDice(_dice, _rollsLeft, true);
        }

        private void HandleDiceHeld(DiceHeldEvent held)
        {
            if (!isActiveAndEnabled || !ListensToOnlineMatch() || held == null)
                return;

            var userId = LocalUserId();
            if (userId.HasValue && held.UserId == userId)
                return;

            RememberRemoteHold(held.Held);
            if (!_diceFlying)
                PaintRemoteHold();
        }

        private void HandleLocalHold(int seat, bool[] keep)
        {
            if (!isActiveAndEnabled || _tableMode != TableMode.LocalNetwork)
                return;

            var table = LocalNetworkManager.Instance;
            if (table.IsLocalTurn || seat == table.LocalSeat)
                return;

            RememberRemoteHold(keep);
            if (!_diceFlying)
                PaintRemoteHold();
        }

        private void OnDieSelectionChanged()
        {
            if (!_applyingRemoteHold && rollsLeftText != null && !OpponentIsPlaying())
                rollsLeftText.text = RollCaption(_rollsLeft, AnyDieKept());

            if (_applyingRemoteHold || _busy || !_ourTurn || _botTurn || ActionsLocked)
                return;

            if (_tableMode != TableMode.LocalNetwork && _tableMode != TableMode.Bluetooth && !_online)
                return;

            var ticket = ++_holdTicket;
            _ = SendHoldSoon(ticket);
        }

        private async Task SendHoldSoon(int ticket)
        {
            await Task.Delay(160);
            if (ticket != _holdTicket || this == null || !_ourTurn || ActionsLocked)
                return;

            var mask = dicePhysics != null ? dicePhysics.HeldMask() : Array.Empty<bool>();
            if (_tableMode == TableMode.LocalNetwork)
            {
                LocalNetworkManager.Instance.SubmitHold(mask);
                return;
            }

            if (_tableMode == TableMode.Bluetooth)
            {
                BluetoothNetworkManager.Instance.SubmitHold(mask);
                return;
            }

            if (!_online || NetworkManager.Instance == null)
                return;

            try
            {
                await NetworkManager.Instance.HoldDice(mask);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Yahtzee hold: " + exception.Message);
            }
        }

        private void RememberRemoteHold(bool[] held)
        {
            var copy = new bool[DiceGameEngine.DiceCount];
            if (held != null)
            {
                var length = held.Length < copy.Length ? held.Length : copy.Length;
                Array.Copy(held, copy, length);
            }

            _remoteHold = copy;
        }

        private void PaintRemoteHold()
        {
            if (_ourTurn || _botTurn || dicePhysics == null || _remoteHold == null)
                return;

            _applyingRemoteHold = true;
            try
            {
                dicePhysics.SetKeptDice(HeldIndexes(_remoteHold));
            }
            finally
            {
                _applyingRemoteHold = false;
            }
        }

        private void HandleTurnChanged(TurnChangedEvent turn)
        {
            if (!isActiveAndEnabled || !ListensToOnlineMatch() || turn == null)
                return;

            var userId = LocalUserId();
            if (userId.HasValue && turn.ScoredUserId == userId && turn.ScoreSheet != null)
                ApplySheet(_scorecard, turn.ScoreSheet);

            RememberScored(turn.ScoredUserId, turn.ScoredMatchPlayerId, turn.ScoreSheet);
            _currentTurnUserId = turn.CurrentPlayerId;
            _ourTurn = userId.HasValue && turn.CurrentPlayerId == userId;
            _dice = Array.Empty<int>();
            _remoteHold = null;
            ClearPhysicalDice();
            _rollsLeft = _ourTurn ? RollsPerTurn : 0;
            StartTurnClock(turn.TurnExpiresAt);
            UpdateUI(_scorecard, _dice, _rollsLeft);
        }

        private void HandleMatchStarted(MatchStartedEvent started)
        {
            if (!isActiveAndEnabled || !ListensToOnlineMatch() || started == null)
                return;

            _scorecard.Reset();
            _dice = Array.Empty<int>();
            ClearPhysicalDice();
            var userId = LocalUserId();
            RememberStarted(started.Players, userId, started.CurrentPlayerId);
            _ourTurn = userId.HasValue && started.CurrentPlayerId == userId;
            _rollsLeft = _ourTurn ? RollsPerTurn : 0;
            StartTurnClock(started.TurnExpiresAt);
            UpdateUI(_scorecard, _dice, _rollsLeft);
        }

        private async Task SyncTurnClockFromServer()
        {
            if (!_online || NetworkManager.Instance == null || !NetworkManager.Instance.ActiveMatchId.HasValue)
                return;

            try
            {
                var state = await NetworkManager.Instance.ExpireTurn();
                if (this == null || !isActiveAndEnabled || !_online)
                    return;

                if (state == null || state.Match == null)
                    return;

                if (state.Match.Status == "finished" || state.Match.Status == "cancelled")
                {
                    await PresentOnlineFinishFromMatch(state.Match, state.Match.Status);
                    return;
                }

                StartTurnClock(state.Turn != null ? state.Turn.ExpiresAt : null);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Yahtzee turn sync: " + exception.Message);
            }
        }

        private void HandleMatchState(GameStateResponse state)
        {
            if (!isActiveAndEnabled || !ListensToOnlineMatch() || state == null || state.Match == null)
                return;

            _ = RestoreReconnectedMatch(MatchReconnectResponseDto.From(state), false);
        }

        private async Task ApplyGameState(GameStateResponse state)
        {
            if (state == null || state.Match == null)
                return;

            if (state.Match.Status == "cancelled")
            {
                LeaveClosedMatch();
                return;
            }

            if (state.Match.Status == "finished")
            {
                await PresentOnlineFinishFromMatch(state.Match, "completed");
                return;
            }

            var userId = LocalUserId();
            RememberMatch(state.Match, userId);
            ApplySeat(state.Match, userId);
            RefreshAbsence(state.Match);
            StartTurnClock(state.Turn != null ? state.Turn.ExpiresAt : null);
            _ourTurn = userId.HasValue && state.Match.CurrentPlayerId == userId;
            var faces = state.Turn != null ? ConcreteFaces(state.Turn.Dice) : Array.Empty<int>();
            if (faces.Length == DiceGameEngine.DiceCount)
            {
                _dice = faces;
                _rollsLeft = state.Turn.RollsRemaining;
                await PublishDice(_dice, _rollsLeft);
                return;
            }

            _dice = Array.Empty<int>();
            ClearPhysicalDice();
            _rollsLeft = _ourTurn ? RollsPerTurn : 0;
            UpdateUI(_scorecard, _dice, _rollsLeft);
        }

        private bool[] HeldMask()
        {
            var count = DiceGameEngine.DiceCount;
            var keep = new bool[count];
            if (dicePhysics == null)
                return keep;

            var held = dicePhysics.GetSelectedDiceIndices();
            for (var index = 0; index < held.Length; index++)
            {
                var slot = held[index];
                if (slot >= 0 && slot < count)
                    keep[slot] = true;
            }

            return keep;
        }

        private int[] RerollIndexes()
        {
            var keep = HeldMask();
            var count = 0;
            for (var index = 0; index < keep.Length; index++)
            {
                if (!keep[index])
                    count++;
            }

            var reroll = new int[count];
            var write = 0;
            for (var index = 0; index < keep.Length; index++)
            {
                if (keep[index])
                    continue;

                reroll[write] = index;
                write++;
            }

            return reroll;
        }

        public static void DiscardLocalSessions()
        {
            if (Instance != null)
                Instance.RetirePlay();

            PassAndPlayManager.Close();
            if (LocalNetworkManager.HasInstance)
                LocalNetworkManager.Instance.StopSession();
            if (BluetoothNetworkManager.HasInstance)
                BluetoothNetworkManager.Instance.StopSession();
        }

        private void RetirePlay()
        {
            _playEpoch++;
            _settleId++;
            _botTurn = false;
            ClearPhysicalDice();
        }

        public bool BotStillPlaying(int epoch)
        {
            return epoch == _playEpoch && _tableMode == TableMode.Bot;
        }

        private void ClearPhysicalDice()
        {
            _settledValues = Array.Empty<int>();
            if (dicePhysics != null)
                dicePhysics.ClearTable();
        }

        private async Task PublishDice(int[] dice, int rollsLeft, bool forceRoll = false)
        {
            dice ??= Array.Empty<int>();
            var rollAgain = forceRoll || _animateNextDice;
            _animateNextDice = false;
            if (dice.Length == DiceGameEngine.DiceCount && (rollAgain || !SameDice(_settledValues, dice)))
            {
                var id = ++_settleId;
                var copy = new int[dice.Length];
                Array.Copy(dice, copy, dice.Length);
                _settledValues = copy;
                _diceFlying = true;
                GameAudio.PlayDiceRoll();

                if (dicePhysics != null)
                {
                    try
                    {
                        FrameDiceZone(reapplyCamera: false);
                        _settling = dicePhysics.RollDicePhysical(copy);
                        await _settling;
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning("Yahtzee dice: " + exception.Message);
                    }
                }
                else
                {
                    Debug.LogWarning("DicePhysicsController не назначен на GameplayScreen.");
                }

                _diceFlying = false;
                if (id != _settleId || this == null)
                    return;
            }
            else if (dice.Length == DiceGameEngine.DiceCount)
            {
                try
                {
                    await _settling;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("Yahtzee dice: " + exception.Message);
                }

                if (this == null)
                    return;
            }

            _dice = dice;
            _rollsLeft = rollsLeft;
            UpdateUI(ShownSheet, _dice, _rollsLeft);
            PaintRemoteHold();
        }

        public async Task PresentBotRoll(Scorecard botSheet, int[] dice, int rollsLeft)
        {
            _shownSheet = botSheet ?? _botScorecard;
            _botTurn = true;
            _ourTurn = false;
            _busy = true;
            await PublishDice(dice, rollsLeft, true);
        }

        public void ShowBotResult(Scorecard botSheet)
        {
            _shownSheet = botSheet ?? _botScorecard;
            _botTurn = true;
            _ourTurn = false;
            _busy = true;
            _dice = Array.Empty<int>();
            _rollsLeft = 0;
            UpdateUI(_shownSheet, _dice, _rollsLeft);
        }

        private Scorecard ShownSheet
        {
            get
            {
                if (_tableMode == TableMode.PassAndPlay || _tableMode == TableMode.LocalNetwork || _tableMode == TableMode.Bluetooth)
                    return PlayingSheet;

                return _shownSheet ?? _scorecard;
            }
        }

        private Scorecard PlayingSheet
        {
            get
            {
                if (_tableMode == TableMode.PassAndPlay)
                {
                    var match = PassAndPlayManager.Active;
                    if (match == null || match.AwaitingHandoff)
                        return _blankSheet;

                    return match.CurrentSheet;
                }

                if (_tableMode == TableMode.LocalNetwork && LocalNetworkManager.HasInstance)
                    return LocalNetworkManager.Instance.LocalSheet ?? _scorecard;

                if (_tableMode == TableMode.Bluetooth && BluetoothNetworkManager.HasInstance)
                    return BluetoothNetworkManager.Instance.LocalSheet ?? _scorecard;

                return _scorecard;
            }
        }

        private static bool SameDice(int[] left, int[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
                return false;

            for (var index = 0; index < left.Length; index++)
            {
                if (left[index] != right[index])
                    return false;
            }

            return true;
        }

        private void RememberMatch(MatchState match, int? localUserId)
        {
            _currentTurnUserId = match != null ? match.CurrentPlayerId : null;
            _matchType = match != null ? match.Type : null;
            if (match == null || match.Players == null)
            {
                _trackedCount = 0;
                return;
            }

            var order = OrderedIndexes(match.Players.Length, index => match.Players[index] != null ? match.Players[index].TurnOrder : int.MaxValue, index => match.Players[index] != null);
            _trackedCount = 0;
            for (var index = 0; index < order.Length && _trackedCount < PlayerScoreStrip.Capacity; index++)
            {
                var seat = match.Players[order[index]];
                _tracked[_trackedCount++] = new TrackedSeat
                {
                    UserId = seat.UserId,
                    MatchPlayerId = 0,
                    Name = seat.Nickname,
                    IsBot = seat.IsBot,
                    IsLocal = localUserId.HasValue && seat.UserId == localUserId,
                    Score = seat.FinalScore ?? SheetTotal(seat.ScoreSheet)
                };
            }
        }

        private void RememberStarted(StartedPlayer[] players, int? localUserId, int? currentPlayerId)
        {
            _currentTurnUserId = currentPlayerId;
            if (players == null)
            {
                _trackedCount = 0;
                return;
            }

            var order = OrderedIndexes(players.Length, index => players[index] != null ? players[index].TurnOrder : int.MaxValue, index => players[index] != null);
            var next = new TrackedSeat[PlayerScoreStrip.Capacity];
            var count = 0;
            for (var index = 0; index < order.Length && count < PlayerScoreStrip.Capacity; index++)
            {
                var seat = players[order[index]];
                next[count++] = new TrackedSeat
                {
                    UserId = seat.UserId,
                    MatchPlayerId = seat.MatchPlayerId,
                    Name = seat.Nickname,
                    IsBot = seat.IsBot,
                    IsLocal = localUserId.HasValue && seat.UserId == localUserId,
                    Score = FindTracked(seat.UserId, seat.MatchPlayerId)
                };
            }

            for (var index = 0; index < count; index++)
                _tracked[index] = next[index];

            _trackedCount = count;
        }

        private int FindTracked(int? userId, int matchPlayerId)
        {
            for (var index = 0; index < _trackedCount; index++)
            {
                var seat = _tracked[index];
                if (matchPlayerId != 0 && seat.MatchPlayerId == matchPlayerId)
                    return seat.Score;

                if (userId.HasValue && seat.UserId == userId)
                    return seat.Score;
            }

            return 0;
        }

        private void RememberScored(int? userId, int matchPlayerId, ScoreSheet sheet)
        {
            if (sheet == null)
                return;

            var total = SheetTotal(sheet);
            for (var index = 0; index < _trackedCount; index++)
            {
                var seat = _tracked[index];
                var samePlayer = matchPlayerId != 0 && seat.MatchPlayerId == matchPlayerId;
                var sameUser = userId.HasValue && seat.UserId == userId;
                if (!samePlayer && !sameUser)
                    continue;

                seat.Score = total;
                if (matchPlayerId != 0)
                    seat.MatchPlayerId = matchPlayerId;
                _tracked[index] = seat;
                return;
            }
        }

        private static int[] OrderedIndexes(int length, Func<int, int> turnOrder, Func<int, bool> present)
        {
            var count = 0;
            var indexes = new int[length];
            for (var index = 0; index < length; index++)
            {
                if (!present(index))
                    continue;

                indexes[count++] = index;
            }

            for (var index = 1; index < count; index++)
            {
                var key = indexes[index];
                var cursor = index - 1;
                while (cursor >= 0 && turnOrder(indexes[cursor]) > turnOrder(key))
                {
                    indexes[cursor + 1] = indexes[cursor];
                    cursor--;
                }

                indexes[cursor + 1] = key;
            }

            if (count == length)
                return indexes;

            var trimmed = new int[count];
            Array.Copy(indexes, trimmed, count);
            return trimmed;
        }

        private static int SheetTotal(ScoreSheet sheet)
        {
            if (sheet == null)
                return 0;

            var upper = Value(sheet.Ones) + Value(sheet.Twos) + Value(sheet.Threes) + Value(sheet.Fours) + Value(sheet.Fives) + Value(sheet.Sixes);
            var lower = Value(sheet.ThreeOfAKind) + Value(sheet.FourOfAKind) + Value(sheet.FullHouse) + Value(sheet.SmallStraight) + Value(sheet.LargeStraight) + Value(sheet.Yahtzee) + Value(sheet.Chance);
            var upperBonus = upper >= Scorecard.UpperBonusThreshold ? Scorecard.UpperBonusValue : 0;
            var yahtzeeBonus = sheet.YahtzeeBonusCount * Scorecard.YahtzeeBonusPoints;
            return upper + upperBonus + lower + yahtzeeBonus;
        }

        private static int Value(int? score)
        {
            return score ?? 0;
        }

        private sealed class TrackedSeat
        {
            public int? UserId;
            public int MatchPlayerId;
            public string Name;
            public bool IsBot;
            public bool IsLocal;
            public int Score;
        }

        private void ApplySeat(MatchState match, int? userId)
        {
            if (match == null || match.Players == null || !userId.HasValue)
                return;

            for (var index = 0; index < match.Players.Length; index++)
            {
                var seat = match.Players[index];
                if (seat == null || seat.UserId != userId)
                    continue;

                ApplySheet(_scorecard, seat.ScoreSheet);
                return;
            }
        }

        private static void ApplySheet(Scorecard card, ScoreSheet sheet)
        {
            card.Scores[YahtzeeCategory.Ones] = sheet != null ? sheet.Ones : null;
            card.Scores[YahtzeeCategory.Twos] = sheet != null ? sheet.Twos : null;
            card.Scores[YahtzeeCategory.Threes] = sheet != null ? sheet.Threes : null;
            card.Scores[YahtzeeCategory.Fours] = sheet != null ? sheet.Fours : null;
            card.Scores[YahtzeeCategory.Fives] = sheet != null ? sheet.Fives : null;
            card.Scores[YahtzeeCategory.Sixes] = sheet != null ? sheet.Sixes : null;
            card.Scores[YahtzeeCategory.ThreeOfAKind] = sheet != null ? sheet.ThreeOfAKind : null;
            card.Scores[YahtzeeCategory.FourOfAKind] = sheet != null ? sheet.FourOfAKind : null;
            card.Scores[YahtzeeCategory.FullHouse] = sheet != null ? sheet.FullHouse : null;
            card.Scores[YahtzeeCategory.SmallStraight] = sheet != null ? sheet.SmallStraight : null;
            card.Scores[YahtzeeCategory.LargeStraight] = sheet != null ? sheet.LargeStraight : null;
            card.Scores[YahtzeeCategory.Yahtzee] = sheet != null ? sheet.Yahtzee : null;
            card.Scores[YahtzeeCategory.Chance] = sheet != null ? sheet.Chance : null;
            card.SetYahtzeeBonusCount(sheet != null ? sheet.YahtzeeBonusCount : 0);
        }

        private static int[] HeldIndexes(bool[] held)
        {
            if (held == null || held.Length == 0)
                return Array.Empty<int>();

            var count = 0;
            for (var index = 0; index < held.Length; index++)
            {
                if (held[index])
                    count++;
            }

            var selected = new int[count];
            var write = 0;
            for (var index = 0; index < held.Length; index++)
            {
                if (!held[index])
                    continue;

                selected[write++] = index;
            }

            return selected;
        }

        private static int[] ConcreteFaces(int?[] faces)
        {
            if (faces == null || faces.Length != DiceGameEngine.DiceCount)
                return Array.Empty<int>();

            var copy = new int[DiceGameEngine.DiceCount];
            for (var index = 0; index < copy.Length; index++)
            {
                if (!faces[index].HasValue)
                    return Array.Empty<int>();

                copy[index] = faces[index].Value;
            }

            return copy;
        }

        private static int? LocalUserId()
        {
            var user = NetworkManager.Instance != null ? NetworkManager.Instance.CurrentUser : null;
            return user != null ? user.Id : (int?)null;
        }

        private void FitRollButtonFont()
        {
            if (rollsLeftText == null || _rollRect == null)
                return;

            var width = _rollRect.rect.width - 28f;
            var height = _rollRect.rect.height - 10f;
            if (width < 40f || height < 16f)
                return;

            const string sample = "Перебросить оставшиеся (3/3)";
            var best = 16;
            var low = 16;
            var high = Mathf.Clamp(Mathf.FloorToInt(height), 16, 34);
            for (var step = 0; step < 8; step++)
            {
                var mid = (low + high) / 2;
                if (mid <= low)
                    break;

                if (PreferredTextWidth(rollsLeftText, sample, mid) <= width && mid <= height)
                {
                    best = mid;
                    low = mid;
                }
                else
                {
                    high = mid;
                }
            }

            rollsLeftText.resizeTextForBestFit = false;
            rollsLeftText.fontSize = best;
            if (_opponentTurnLabel != null)
            {
                _opponentTurnLabel.resizeTextForBestFit = false;
                _opponentTurnLabel.fontSize = best;
            }

            MatchCommitToRollButton();
        }

        private void MatchCommitToRollButton()
        {
            if (_commitRect == null)
                return;

            if (_tutorial && _commitButton != null && _commitButton.gameObject.activeSelf)
            {
                RaiseCommitOverTutorialHint();
                if (_commitLabel != null)
                {
                    _commitLabel.resizeTextForBestFit = false;
                    _commitLabel.fontSize = rollsLeftText != null ? rollsLeftText.fontSize : 26;
                    _commitLabel.alignment = TextAnchor.MiddleCenter;
                }

                return;
            }

            if (!_layoutLandscape)
            {
                if (_commitRect.parent != transform)
                    _commitRect.SetParent(transform, false);

                UiFactory.Place(_commitRect, 0.08f, 0.4f, 0.92f, 0.488f);
                if (_commitLabel != null)
                {
                    _commitLabel.resizeTextForBestFit = false;
                    _commitLabel.fontSize = rollsLeftText != null ? rollsLeftText.fontSize : 26;
                    _commitLabel.alignment = TextAnchor.MiddleCenter;
                }

                return;
            }

            if (_rollRect == null)
                return;

            if (_sheetRect != null && _commitRect.parent != _sheetRect)
                _commitRect.SetParent(_sheetRect, false);

            var rollHeight = _rollRect.rect.height;
            var rollWidth = _rollRect.rect.width;
            if (rollHeight < 8f || rollWidth < 8f)
                return;

            var width = rollWidth;
            if (_sheetRect != null)
                width = Mathf.Min(width, Mathf.Max(8f, _sheetRect.rect.width - 20f));

            _commitRect.anchorMin = new Vector2(0.5f, 0f);
            _commitRect.anchorMax = new Vector2(0.5f, 0f);
            _commitRect.pivot = new Vector2(0.5f, 0f);
            _commitRect.sizeDelta = new Vector2(width, rollHeight);
            _commitRect.anchoredPosition = new Vector2(0f, 8f);

            if (_commitLabel == null || rollsLeftText == null)
                return;

            _commitLabel.resizeTextForBestFit = false;
            _commitLabel.fontSize = rollsLeftText.fontSize;
            _commitLabel.alignment = TextAnchor.MiddleCenter;
        }

        private static float PreferredTextWidth(Text label, string value, int size)
        {
            var settings = label.GetGenerationSettings(Vector2.zero);
            settings.fontSize = size;
            settings.resizeTextForBestFit = false;
            settings.horizontalOverflow = HorizontalWrapMode.Overflow;
            settings.verticalOverflow = VerticalWrapMode.Overflow;
            settings.scaleFactor = 1f;
            return label.cachedTextGeneratorForLayout.GetPreferredWidth(value, settings) / label.pixelsPerUnit;
        }

        private const float CategoryFontMin = 16f;
        private const float CategoryFontMax = 48f;
        private const string WidestCategoryLabel = "Большой стрит";
        private const string WidestScoreLabel = "50";
        private float _categoryFontSize = 28f;

        private void FitCategoryFonts()
        {
            if (scoreRows == null || scoreRows.Count == 0 || scoreRows[0].rowButton == null)
                return;

            var rowRect = scoreRows[0].rowButton.GetComponent<RectTransform>();
            var rowLayout = scoreRows[0].rowButton.GetComponent<HorizontalLayoutGroup>();
            var probe = scoreRows[0].rowButton.transform.Find("Name").GetComponent<TextMeshProUGUI>();
            if (rowRect == null || rowLayout == null || probe == null)
                return;

            var innerWidth = rowRect.rect.width - rowLayout.padding.left - rowLayout.padding.right - rowLayout.spacing;
            var innerHeight = rowRect.rect.height - rowLayout.padding.top - rowLayout.padding.bottom;
            if (innerWidth < 48f || innerHeight < CategoryFontMin)
                return;

            var best = CategoryFontMin;
            var low = CategoryFontMin;
            var high = Mathf.Min(CategoryFontMax, innerHeight);
            for (var step = 0; step < 10; step++)
            {
                var mid = (low + high) * 0.5f;
                var measured = Measure(probe, WidestCategoryLabel, mid);
                var score = Measure(probe, WidestScoreLabel, mid);
                if (measured.x + score.x <= innerWidth - 4f && measured.y <= innerHeight)
                {
                    best = mid;
                    low = mid;
                }
                else
                {
                    high = mid;
                }
            }

            _categoryFontSize = best;
            var scoreWidth = Measure(probe, WidestScoreLabel, best).x;
            for (var index = 0; index < scoreRows.Count; index++)
            {
                var row = scoreRows[index];
                if (row == null || row.rowButton == null)
                    continue;

                var nameTransform = row.rowButton.transform.Find("Name");
                if (nameTransform == null)
                    continue;

                var name = nameTransform.GetComponent<TextMeshProUGUI>();
                if (name == null)
                    continue;

                LockCategoryFont(name);
                LockCategoryFont(row.scoreValueText);
                var nameElement = name.GetComponent<LayoutElement>();
                if (nameElement != null)
                {
                    var nameWidth = Measure(name, name.text, best).x;
                    nameElement.minWidth = nameWidth;
                    nameElement.preferredWidth = nameWidth;
                    nameElement.flexibleWidth = 0f;
                }

                var valueElement = row.scoreValueText.GetComponent<LayoutElement>();
                if (valueElement != null)
                {
                    valueElement.minWidth = scoreWidth;
                    valueElement.preferredWidth = scoreWidth;
                    valueElement.flexibleWidth = 0f;
                }
            }
        }

        private static Vector2 Measure(TextMeshProUGUI label, string text, float size)
        {
            var sizeBefore = label.fontSize;
            var autoBefore = label.enableAutoSizing;
            label.enableAutoSizing = false;
            label.fontSize = size;
            var values = label.GetPreferredValues(text);
            label.fontSize = sizeBefore;
            label.enableAutoSizing = autoBefore;
            return values;
        }

        private void LockCategoryFont(TextMeshProUGUI label)
        {
            if (label == null)
                return;

            UIThemeConfig.ApplyFont(label);
            label.enableAutoSizing = false;
            label.fontSize = _categoryFontSize;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            var fallback = label.GetComponentInChildren<Text>(true);
            if (fallback == null)
                return;

            fallback.resizeTextForBestFit = false;
            fallback.fontSize = Mathf.RoundToInt(_categoryFontSize);
        }

        private void WriteScore(TextMeshProUGUI label, int score, Color color, bool committed)
        {
            UiFactory.ApplyAutoText(label, score.ToString(), color);
            if (label == null)
                return;

            LockCategoryFont(label);
            var size = committed ? _categoryFontSize : _categoryFontSize * 0.78f;
            label.fontSize = size;
            label.fontStyle = committed ? FontStyles.Bold : FontStyles.Normal;
            KeepScoreAlignment(label);
            var fallback = label.GetComponentInChildren<Text>(true);
            if (fallback == null)
                return;

            fallback.fontStyle = committed ? FontStyle.Bold : FontStyle.Normal;
            fallback.fontSize = Mathf.Max(12, Mathf.RoundToInt(size));
        }

        private void WriteHint(TextMeshProUGUI label, string value)
        {
            UiFactory.ApplyAutoText(label, value, HintScoreColor);
            if (label == null)
                return;

            LockCategoryFont(label);
            label.fontSize = _categoryFontSize * 0.78f;
            label.fontStyle = FontStyles.Normal;
            KeepScoreAlignment(label);
            var fallback = label.GetComponentInChildren<Text>(true);
            if (fallback == null)
                return;

            fallback.fontStyle = FontStyle.Normal;
            fallback.fontSize = Mathf.Max(12, Mathf.RoundToInt(_categoryFontSize * 0.78f));
        }

        private void KeepScoreAlignment(TextMeshProUGUI label)
        {
            if (label == null)
                return;

            var rightAligned = true;
            for (var index = 0; index < scoreRows.Count; index++)
            {
                var row = scoreRows[index];
                if (row == null || row.scoreValueText != label)
                    continue;

                rightAligned = row.category != YahtzeeCategory.Yahtzee;
                break;
            }

            label.alignment = rightAligned ? TextAlignmentOptions.Right : TextAlignmentOptions.Center;
            label.enableAutoSizing = false;
            var fallback = label.GetComponentInChildren<Text>(true);
            if (fallback != null)
                fallback.alignment = rightAligned ? TextAnchor.MiddleRight : TextAnchor.MiddleCenter;
        }

        private bool OpponentIsPlaying()
        {
            if (_ourTurn && !_botTurn)
                return false;

            if (_tableMode == TableMode.PassAndPlay)
                return false;

            return _online || _botTurn || _tableMode == TableMode.LocalNetwork || _tableMode == TableMode.Bluetooth;
        }

        private bool AnyDieKept()
        {
            if (dicePhysics == null)
                return false;

            var mask = dicePhysics.HeldMask();
            for (var index = 0; index < mask.Length; index++)
            {
                if (mask[index])
                    return true;
            }

            return false;
        }

        private static string RollCaption(int rollsLeft, bool anyKept)
        {
            var total = Mathf.Max(1, RollsPerTurn);
            var left = Mathf.Clamp(rollsLeft, 0, total);
            var count = " ("
                + left.ToString(CultureInfo.InvariantCulture)
                + "/"
                + total.ToString(CultureInfo.InvariantCulture)
                + ")";

            if (rollsLeft >= total)
                return "Бросок" + count;

            return (anyKept ? "Перебросить оставшиеся" : "Перебросить все") + count;
        }

        private void StartTurnClock(string expiresAt)
        {
            _clockGeneration++;
            _expireSent = false;
            _turnWarningSent = false;
            if (string.IsNullOrEmpty(expiresAt))
            {
                WriteClock(-1);
                return;
            }

            if (!DateTimeOffset.TryParse(
                    expiresAt,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var expires))
            {
                WriteClock(-1);
                return;
            }

            var generation = _clockGeneration;
            _ = RunTurnClock(generation, expires.UtcDateTime);
        }

        private async Task RunTurnClock(int generation, DateTime expiresUtc)
        {
            while (generation == _clockGeneration && this != null)
            {
                var left = (int)Math.Ceiling((expiresUtc - DateTime.UtcNow).TotalSeconds);
                if (left < 0)
                    left = 0;

                WriteClock(left);
                if (!_turnWarningSent && _online && _ourTurn && left > 0 && left <= 30)
                {
                    _turnWarningSent = true;
                    PlayTurnAttention();
                }

                if (left == 0)
                {
                    if (_online && !_expireSent && NetworkManager.Instance != null)
                    {
                        _expireSent = true;
                        try
                        {
                            var state = await NetworkManager.Instance.ExpireTurn();
                            if (generation != _clockGeneration || this == null)
                                return;

                            if (state != null && state.Match != null && state.Match.Status == "cancelled")
                                LeaveClosedMatch();
                            else if (state != null && state.Match != null && state.Match.Status != "finished")
                                await ApplyGameState(state);
                        }
                        catch (Exception exception)
                        {
                            Debug.LogWarning("Yahtzee turn clock: " + exception.Message);
                        }
                    }

                    return;
                }

                try
                {
                    await Task.Delay(250);
                }
                catch (Exception)
                {
                    return;
                }
            }
        }

        private void WriteClock(int seconds)
        {
            if (seconds == _clockShown)
                return;

            _clockShown = seconds;
            if (isActiveAndEnabled)
                RefreshScoreboard();
        }

        private void HandleOpponentDisconnected(OpponentDisconnectedEvent notice)
        {
            if (!ListensToOnlineMatch() || notice == null)
                return;

            var me = LocalUserId();
            var seconds = Mathf.Max(1, notice.ReconnectSeconds > 0 ? notice.ReconnectSeconds : 30);
            var name = string.IsNullOrWhiteSpace(notice.Nickname) ? "Игрок" : notice.Nickname;

            // Local player lost connection — MainMenu shows «Переподключиться»; keep a prompt if still here.
            if (me.HasValue && notice.UserId == me)
            {
                if (!isActiveAndEnabled)
                    return;

                HideAbsence();
                ShowReturnPrompt(GameText.Format(
                    "ui.gameplay.link_lost_prompt",
                    seconds.ToString(CultureInfo.InvariantCulture)));
                return;
            }

            if (!isActiveAndEnabled)
                return;

            HideDecision();
            StartDisconnectGrace(seconds, name);
        }

        private void HandleReplacedByBot(PlayerReplacedByBotEvent replaced)
        {
            if (!isActiveAndEnabled || !ListensToOnlineMatch() || replaced == null)
                return;

            HideDecision();
            var me = LocalUserId();
            if (me.HasValue && replaced.ReplacedUserId == me)
            {
                ShowAbsence(GameText.Get("ui.gameplay.bot_for_you"), true);
                return;
            }

            ShowAbsence(GameText.Get("ui.gameplay.bot_for_player"), false);
        }

        private bool _onlineResultsShown;

        private void HandleMatchOver(MatchOverEvent over)
        {
            if (!isActiveAndEnabled || !ListensToOnlineMatch() || over == null)
                return;

            if (_onlineResultsShown)
                return;

            HideDecision();
            HideAbsence();
            _waitingDisconnect = false;
            _graceGeneration++;
            WriteClock(-1);
            DiscardLocalSessions();
            if (NetworkManager.Instance != null)
                NetworkManager.Instance.ForgetLocalMatch();

            if (over.Standings != null && over.Standings.Length > 0)
            {
                _onlineResultsShown = true;
                MatchResultsBridge.PresentFromStandings(
                    over.Standings,
                    LocalUserId(),
                    over.Reason,
                    over.WinnerUserIds);
                return;
            }

            LeaveClosedMatch();
        }

        private async Task PresentOnlineFinishFromMatch(MatchState match, string reason)
        {
            if (_onlineResultsShown || match == null)
                return;

            _onlineResultsShown = true;
            HideDecision();
            HideAbsence();
            WriteClock(-1);
            DiscardLocalSessions();
            if (NetworkManager.Instance != null)
                NetworkManager.Instance.ForgetLocalMatch();

            if (match.Players == null || match.Players.Length == 0)
            {
                LeaveClosedMatch();
                return;
            }

            var localId = LocalUserId();
            var rows = new List<MatchResultRow>();
            var top = 0;
            for (var index = 0; index < match.Players.Length; index++)
            {
                var seat = match.Players[index];
                if (seat == null)
                    continue;

                var score = seat.FinalScore ?? 0;
                if (score > top)
                    top = score;
                rows.Add(new MatchResultRow
                {
                    Nickname = string.IsNullOrWhiteSpace(seat.Nickname) ? (seat.IsBot ? "Бот" : "Игрок") : seat.Nickname,
                    Score = score,
                    IsLocal = localId.HasValue && seat.UserId == localId,
                    IsBot = seat.IsBot
                });
            }

            rows.Sort((a, b) => b.Score.CompareTo(a.Score));
            var won = string.Equals(reason, "forfeit", StringComparison.OrdinalIgnoreCase);
            if (!won)
            {
                for (var index = 0; index < rows.Count; index++)
                {
                    if (rows[index].IsLocal && rows[index].Score >= top)
                        won = true;
                }
            }
            else
            {
                for (var index = 0; index < rows.Count; index++)
                {
                    if (rows[index].IsLocal)
                    {
                        won = true;
                        break;
                    }
                }
            }

            MatchResultsBridge.Present(rows, won ? "Победа!" : "Партия окончена", false);
            await Task.CompletedTask;
        }

        private void LeaveClosedMatch()
        {
            HideDecision();
            HideAbsence();
            WriteClock(-1);
            DiscardLocalSessions();
            if (NetworkManager.Instance != null)
                NetworkManager.Instance.ForgetLocalMatch();
            if (UIManager.Instance != null)
                UIManager.Instance.SwitchToScreen(ScreenType.MainMenu);
        }

        private void HandlePlayerReconnected(PlayerReconnectedEvent returned)
        {
            if (!isActiveAndEnabled || !ListensToOnlineMatch() || returned == null)
                return;

            var me = LocalUserId();
            // Ignore own reconnect echo; only clear the waiting banner when the opponent is back.
            if (me.HasValue && returned.UserId == me)
                return;

            _graceGeneration++;
            _waitingDisconnect = false;
            HideDecision();
            HideAbsence();
            StartTurnClock(returned.TurnExpiresAt);
            UpdateUI(_scorecard, _dice, _rollsLeft);
        }

        /// <summary>
        /// 3–4p table: a seat left permanently (frozen sheet, no bot). Refresh authoritative state.
        /// </summary>
        private async void HandlePlayerLeft(PlayerLeftEvent left)
        {
            if (!isActiveAndEnabled || !ListensToOnlineMatch() || left == null || NetworkManager.Instance == null)
                return;

            if (NetworkManager.Instance.ActiveMatchId.HasValue
                && left.MatchId != NetworkManager.Instance.ActiveMatchId.Value)
                return;

            var me = LocalUserId();
            if (me.HasValue && left.UserId == me)
                return;

            _graceGeneration++;
            _waitingDisconnect = false;
            HideDecision();
            HideAbsence();

            var nick = string.IsNullOrWhiteSpace(left.Nickname)
                ? GameText.Get("ui.gameplay.player_default")
                : left.Nickname;
            PlayerFeedback.Report(new FeedbackEvent(
                "PLAYER_LEFT",
                GameText.Format("ui.gameplay.player_left", nick),
                FeedbackSeverity.Toast,
                ApiErrorCategory.Gameplay,
                false));

            try
            {
                var state = await NetworkManager.Instance.ExpireTurn();
                if (this == null || state == null)
                    return;
                await ApplyGameState(state);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Yahtzee player left catch-up: " + exception.Message);
            }
        }

        private void RefreshAbsence(MatchState match)
        {
            if (match == null)
                return;

            var missing = DisconnectedSeat(match);
            var me = LocalUserId();
            if (missing != null)
            {
                if (me.HasValue && missing.UserId == me)
                {
                    HideAbsence();
                    var left = RemainingReconnectSeconds(match);
                    ShowReturnPrompt(
                        "Связь потеряна.\nПереподключитесь, пока матч не завершён\n("
                        + left.ToString(CultureInfo.InvariantCulture)
                        + " с).");
                    return;
                }

                HideDecision();
                if (!_waitingDisconnect)
                {
                    var nick = string.IsNullOrWhiteSpace(missing.Nickname) ? "Игрок" : missing.Nickname;
                    StartDisconnectGrace(RemainingReconnectSeconds(match), nick);
                }

                return;
            }

            if (!string.IsNullOrEmpty(match.ReconnectDeadlineAt))
            {
                HideDecision();
                if (!_waitingDisconnect)
                    StartDisconnectGrace(RemainingReconnectSeconds(match), "Игрок");
                return;
            }

            HideDecision();
            // Keep the waiting banner until PlayerReconnected — don't clear it on unrelated state sync.
            if (_waitingDisconnect)
                return;

            if (match.Players == null)
            {
                HideAbsence();
                return;
            }

            var local = LocalUserId();
            for (var index = 0; index < match.Players.Length; index++)
            {
                var seat = match.Players[index];
                if (seat == null || !seat.IsBot || !seat.OriginUserId.HasValue)
                    continue;

                if (local.HasValue && seat.OriginUserId == local)
                    ShowAbsence(GameText.Get("ui.gameplay.bot_for_you"), true);
                else
                    ShowAbsence(GameText.Get("ui.gameplay.bot_for_player"), false);

                return;
            }

            HideAbsence();
        }

        private static int RemainingReconnectSeconds(MatchState match)
        {
            if (match == null || string.IsNullOrWhiteSpace(match.ReconnectDeadlineAt))
                return 30;

            if (!DateTimeOffset.TryParse(
                    match.ReconnectDeadlineAt,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var until))
                return 30;

            return Mathf.Max(1, (int)Math.Ceiling((until - DateTimeOffset.UtcNow).TotalSeconds));
        }

        private string _waitingDisconnectName = "Игрок";

        private void StartDisconnectGrace(int seconds, string opponentName)
        {
            if (_waitingDisconnect)
                return;

            _waitingDisconnectName = string.IsNullOrWhiteSpace(opponentName) ? "Игрок" : opponentName;
            var left = Mathf.Max(1, seconds);
            _waitingDisconnect = true;
            _hasSelection = false;
            WriteClock(-1);
            var generation = ++_graceGeneration;
            ShowWaitingDisconnect(left);
            // Freeze roll / hold / score while waiting for the opponent to return.
            UpdateUI(_scorecard, _dice, _rollsLeft);
            _ = WaitDisconnectGrace(generation, left);
        }

        private void ShowWaitingDisconnect(int secondsLeft)
        {
            ShowAbsence(
                GameText.Format(
                    "ui.gameplay.waiting_named",
                    _waitingDisconnectName,
                    secondsLeft.ToString(CultureInfo.InvariantCulture)),
                false);
        }

        private async Task WaitDisconnectGrace(int generation, int seconds)
        {
            var left = Mathf.Max(1, seconds);
            try
            {
                while (left > 0)
                {
                    if (generation != _graceGeneration || this == null || !isActiveAndEnabled)
                        return;

                    ShowWaitingDisconnect(left);
                    await Task.Delay(TimeSpan.FromSeconds(1));
                    left--;
                }
            }
            catch (Exception)
            {
                return;
            }

            if (generation != _graceGeneration || this == null || !isActiveAndEnabled || NetworkManager.Instance == null)
            {
                _waitingDisconnect = false;
                return;
            }

            ShowWaitingDisconnect(0);
            _waitingDisconnect = false;

            try
            {
                // Queues resolve via reconnect deadline → forfeit. Custom 1v1 keeps waiting (no claim/bot).
                var state = await NetworkManager.Instance.ExpireTurn();
                if (generation != _graceGeneration || this == null)
                    return;

                if (state?.Match == null)
                    return;

                if (state.Match.Status == "finished")
                {
                    await PresentOnlineFinishFromMatch(state.Match, "forfeit");
                    return;
                }

                if (state.Match.Status == "cancelled")
                {
                    LeaveClosedMatch();
                    return;
                }

                await ApplyGameState(state);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Yahtzee disconnect grace: " + exception.Message);
            }
        }

        private void PlayTurnAttention()
        {
#if UNITY_ANDROID || UNITY_IOS
            if (Application.platform == RuntimePlatform.Android || Application.platform == RuntimePlatform.IPhonePlayer)
            {
                Handheld.Vibrate();
                return;
            }
#endif
            PlayTurnChime();
        }

        private void PlayTurnChime()
        {
            if (_turnChime == null)
                _turnChime = BuildTurnChime();

            var source = GetComponent<AudioSource>();
            if (source == null)
                source = gameObject.AddComponent<AudioSource>();

            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.PlayOneShot(_turnChime, 0.35f);
        }

        private static AudioClip BuildTurnChime()
        {
            const int sampleRate = 44100;
            const float duration = 0.16f;
            var count = Mathf.CeilToInt(sampleRate * duration);
            var data = new float[count];
            for (var index = 0; index < count; index++)
            {
                var time = index / (float)sampleRate;
                var envelope = Mathf.Sin(Mathf.PI * index / count);
                data[index] = Mathf.Sin(2f * Mathf.PI * 880f * time) * envelope * 0.22f;
            }

            var clip = AudioClip.Create("TurnWarning", count, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static MatchSeat DisconnectedSeat(MatchState match)
        {
            if (match == null || match.Players == null)
                return null;

            for (var index = 0; index < match.Players.Length; index++)
            {
                var seat = match.Players[index];
                if (seat != null && seat.ConnectionStatus == "disconnected")
                    return seat;
            }

            return null;
        }

        private void ShowReturnPrompt(string message)
        {
            EnsureDecision();
            if (_decisionLabel != null)
            {
                _decisionLabel.text = message;
                WrapInside(_decisionLabel);
            }

            if (_decisionRoot != null)
                _decisionRoot.SetActive(true);
        }

        private void HideDecision()
        {
            if (_decisionRoot != null)
                _decisionRoot.SetActive(false);
        }

        private void ShowAbsence(string message, bool canReturn)
        {
            EnsureAbsence(canReturn);
            if (_absenceLabel != null)
            {
                _absenceLabel.text = message;
                WrapInside(_absenceLabel);
            }

            if (_absenceRoot != null)
                _absenceRoot.SetActive(true);
        }

        private void HideAbsence()
        {
            if (_absenceRoot != null)
                _absenceRoot.SetActive(false);
        }

        private void EnsureDecision()
        {
            if (_decisionRoot == null)
            {
                _decisionRoot = new GameObject("TurnDecision", typeof(RectTransform), typeof(Image));
                _decisionRoot.layer = gameObject.layer;
                _decisionRoot.transform.SetParent(transform, false);
                var image = _decisionRoot.GetComponent<Image>();
                image.color = UIThemeConfig.PanelBgSolid;
                UiFactory.EnsureSoftShadow(_decisionRoot);
                var rect = _decisionRoot.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.08f, 0.28f);
                rect.anchorMax = new Vector2(0.92f, 0.72f);
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                _decisionLabel = UiFactory.CreateLabel(_decisionRoot.transform, "Text", string.Empty, 28, TextAnchor.MiddleCenter);
                WrapInside(_decisionLabel);
                UiFactory.Place(_decisionLabel.rectTransform, 0.06f, 0.5f, 0.94f, 0.94f);
            }

            _decisionRoot.transform.SetAsLastSibling();
            // Queues: reconnect window then W/L. Custom 1v1: wait indefinitely. No claim/bot choice UI.
            var back = FindOrCreateDecisionButton(
                "ReturnTurn",
                GameText.Get("ui.connection.reconnect"),
                0.18f,
                0.16f,
                0.82f,
                0.36f,
                OnReturnToTurn);
            SetButtonCaption(back, GameText.Get("ui.connection.reconnect"));
            back.gameObject.SetActive(true);
            var staleWait = _decisionRoot.transform.Find("WaitBot");
            if (staleWait != null)
                staleWait.gameObject.SetActive(false);
            var staleClaim = _decisionRoot.transform.Find("ClaimWin");
            if (staleClaim != null)
                staleClaim.gameObject.SetActive(false);
            WrapInside(back.GetComponentInChildren<Text>());
        }

        private static void WrapInside(Text label)
        {
            if (label == null)
                return;

            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.alignByGeometry = true;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 16;
            label.resizeTextMaxSize = Mathf.Max(16, label.fontSize);
        }

        private Button FindOrCreateDecisionButton(string name, string caption, float xMin, float yMin, float xMax, float yMax, UnityEngine.Events.UnityAction action)
        {
            var existing = _decisionRoot.transform.Find(name);
            Button button;
            if (existing == null)
            {
                button = UiFactory.CreateButton(_decisionRoot.transform, caption);
                button.name = name;
                button.onClick.AddListener(action);
            }
            else
            {
                button = existing.GetComponent<Button>();
            }

            UiFactory.Place(button.GetComponent<RectTransform>(), xMin, yMin, xMax, yMax);
            return button;
        }

        private static void SetButtonCaption(Button button, string caption)
        {
            if (button == null)
                return;

            var label = button.GetComponentInChildren<Text>(true);
            if (label != null)
                label.text = caption;
        }

        private void EnsureAbsence(bool canReturn)
        {
            if (_absenceRoot == null)
            {
                _absenceRoot = new GameObject("AbsenceNotice", typeof(RectTransform), typeof(Image));
                _absenceRoot.layer = gameObject.layer;
                _absenceRoot.transform.SetParent(transform, false);
                var image = _absenceRoot.GetComponent<Image>();
                image.color = UIThemeConfig.PanelBgSolid;
                UiFactory.EnsureSoftShadow(_absenceRoot);
                var rect = _absenceRoot.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.1f, 0.34f);
                rect.anchorMax = new Vector2(0.9f, 0.72f);
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                _absenceLabel = UiFactory.CreateLabel(_absenceRoot.transform, "Text", string.Empty, 28, TextAnchor.MiddleCenter);
                WrapInside(_absenceLabel);
                UiFactory.Place(_absenceLabel.rectTransform, 0.06f, 0.12f, 0.94f, 0.88f);
                var back = UiFactory.CreateButton(_absenceRoot.transform, "Вернуться в игру");
                back.name = "ReturnMatch";
                UiFactory.StyleAccent(back);
                UiFactory.Place(back.GetComponent<RectTransform>(), 0.25f, 0.08f, 0.75f, 0.42f);
                back.onClick.AddListener(OnReturnToMatch);
            }

            var returnButton = _absenceRoot.transform.Find("ReturnMatch");
            if (returnButton != null)
                returnButton.gameObject.SetActive(canReturn);

            _absenceRoot.transform.SetAsLastSibling();
        }

        private async void OnReturnToTurn()
        {
            await ReturnToMatch();
        }

        private async void OnReturnToMatch()
        {
            await ReturnToMatch();
        }

        private async Task ReturnToMatch()
        {
            var network = NetworkManager.Instance;
            if (network == null || !network.ActiveMatchId.HasValue)
                return;

            try
            {
                var snapshot = await network.SendReconnectRequest(
                    network.ActiveMatchId.Value.ToString(CultureInfo.InvariantCulture));
                if (this == null)
                    return;

                await RestoreReconnectedMatch(snapshot, true);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Yahtzee reconnect: " + exception.Message);
            }
        }

        public async Task RestoreReconnectedMatch(MatchReconnectResponseDto snapshot, bool openSocket)
        {
            if (snapshot == null || snapshot.Match == null || snapshot.Match.Status != "playing")
                return;

            var matchKey = snapshot.Match.Id.ToString(CultureInfo.InvariantCulture);
            InitializeMatchMode(true, matchKey);
            _online = true;
            _tableMode = TableMode.Bot;
            _seenMatchId = snapshot.Match.Id;
            _botTurn = false;
            _busy = false;
            _hasSelection = false;
            HideDecision();
            HideAbsence();
            SetHandoffVisible(false);

            _scorecard.Reset();
            _botScorecard.Reset();
            var userId = LocalUserId();
            ApplySeat(snapshot.Match, userId);
            ApplyOpponentSheet(snapshot.Match, userId);
            RememberMatch(snapshot.Match, userId);
            _shownSheet = _scorecard;
            _ourTurn = userId.HasValue && snapshot.CurrentTurnPlayerId == userId.Value;
            _rollsLeft = snapshot.RollsLeft;
            var faces = snapshot.Turn != null ? ConcreteFaces(snapshot.Turn.Dice) : Array.Empty<int>();
            if (faces.Length == DiceGameEngine.DiceCount)
            {
                _dice = faces;
                var settled = new int[faces.Length];
                Array.Copy(faces, settled, faces.Length);
                _settledValues = settled;
                if (dicePhysics != null)
                {
                    dicePhysics.ShowSettledDice(faces);
                    dicePhysics.SetKeptDice(HeldIndexes(snapshot.Turn.Held));
                }
            }
            else
            {
                _dice = Array.Empty<int>();
                ClearPhysicalDice();
                if (_ourTurn && _rollsLeft <= 0)
                    _rollsLeft = RollsPerTurn;
            }

            StartTurnClock(snapshot.TurnExpiresAt);
            UpdateUI(_scorecard, _dice, _rollsLeft);
            RefreshScoreboard();

            if (!openSocket || NetworkManager.Instance == null || !NetworkManager.Instance.ActiveMatchId.HasValue)
                return;

            try
            {
                await NetworkManager.Instance.ConnectToMatchWebSocket(matchKey);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Yahtzee reconnect socket: " + exception.Message);
            }
        }

        private void ApplyOpponentSheet(MatchState match, int? userId)
        {
            if (match == null || match.Players == null)
                return;

            for (var index = 0; index < match.Players.Length; index++)
            {
                var seat = match.Players[index];
                if (seat == null || (userId.HasValue && seat.UserId == userId))
                    continue;

                ApplySheet(_botScorecard, seat.ScoreSheet);
                return;
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (!_online || NetworkManager.Instance == null || !NetworkManager.Instance.ActiveMatchId.HasValue)
                return;

            if (paused)
                _ = LeaveMatchQuietly();
            else
                _ = ReturnToMatch();
        }

        private async Task LeaveMatchQuietly()
        {
            try
            {
                if (NetworkManager.Instance != null)
                    await NetworkManager.Instance.DisconnectSelf();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Yahtzee disconnect: " + exception.Message);
            }
        }

        private static string ToApiName(YahtzeeCategory category)
        {
            switch (category)
            {
                case YahtzeeCategory.Ones:
                    return "ones";
                case YahtzeeCategory.Twos:
                    return "twos";
                case YahtzeeCategory.Threes:
                    return "threes";
                case YahtzeeCategory.Fours:
                    return "fours";
                case YahtzeeCategory.Fives:
                    return "fives";
                case YahtzeeCategory.Sixes:
                    return "sixes";
                case YahtzeeCategory.ThreeOfAKind:
                    return "threeOfAKind";
                case YahtzeeCategory.FourOfAKind:
                    return "fourOfAKind";
                case YahtzeeCategory.FullHouse:
                    return "fullHouse";
                case YahtzeeCategory.SmallStraight:
                    return "smallStraight";
                case YahtzeeCategory.LargeStraight:
                    return "largeStraight";
                case YahtzeeCategory.Yahtzee:
                    return "yahtzee";
                case YahtzeeCategory.Chance:
                    return "chance";
                default:
                    throw new ArgumentOutOfRangeException(nameof(category), category, "Неизвестная категория Yahtzee.");
            }
        }
    }
}
