using UnityEngine;
using Yahtzee.Core;

namespace Yahtzee.UI
{
    /// <summary>
    /// Keeps <see cref="ProceduralDiceTray"/> viewport margins in sync with the score sheet RectTransform
    /// so the 3D tray never sits under the Yahtzee UI panel.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TrayUIBinder : MonoBehaviour
    {
        [SerializeField] private RectTransform uiPanel;
        [SerializeField] private ProceduralDiceTray tray;
        [SerializeField] private Camera eventCamera;
        [SerializeField] private float edgePad = 0.01f;
        [SerializeField] private float bottomReserve = 0.12f;
        // Tiny top gap so the tray rim clears the score sheet (large values leave an empty strip).
        [SerializeField] private float sheetClearance = 0.018f;
        [SerializeField] private float stripClearance = 0.04f;

        private readonly Vector3[] _corners = new Vector3[4];
        private int _lastW = -1;
        private int _lastH = -1;
        private bool _lastLandscape;
        private float _lastBottom = -1f;
        private float _lastSheetMidX;
        private float _lastSheetMidY;
        private bool _gameplayDriven;

        public void Bind(RectTransform panel, ProceduralDiceTray diceTray, bool syncNow = true)
        {
            uiPanel = panel;
            tray = diceTray;
            if (syncNow && !_gameplayDriven)
                SyncNow();
        }

        /// <summary>
        /// When true, GameplayScreen owns tray viewport rects — LateUpdate must not override them.
        /// </summary>
        public void SetGameplayDriven(bool driven)
        {
            _gameplayDriven = driven;
        }

        /// <summary>Normalized Y of pedestal top — tray south edge stops here (not at screen bottom).</summary>
        public void SetShelfTop(float shelfTopNorm)
        {
            var next = Mathf.Clamp(shelfTopNorm + 0.006f, 0.05f, 0.45f);
            if (Mathf.Abs(next - bottomReserve) < 0.001f)
                return;

            bottomReserve = next;
            _lastW = -1;
            if (!_gameplayDriven)
                SyncNow();
        }

        private void LateUpdate()
        {
            if (_gameplayDriven)
                return;

            if (uiPanel == null || tray == null)
                TryAutoBind();

            if (uiPanel == null || tray == null)
                return;

            var landscape = Screen.width > Screen.height;
            // Re-sync when the sheet moves (layout / safe-area) even if resolution is unchanged.
            uiPanel.GetWorldCorners(_corners);
            var sheetMoved = false;
            if (_lastW > 0)
            {
                var midX = (_corners[0].x + _corners[2].x) * 0.5f;
                var midY = (_corners[0].y + _corners[2].y) * 0.5f;
                sheetMoved = Mathf.Abs(midX - _lastSheetMidX) > 0.5f
                    || Mathf.Abs(midY - _lastSheetMidY) > 0.5f;
            }

            if (Screen.width == _lastW
                && Screen.height == _lastH
                && landscape == _lastLandscape
                && Mathf.Abs(bottomReserve - _lastBottom) < 0.0005f
                && !sheetMoved)
                return;

            SyncNow();
        }

        public void SyncNow()
        {
            if (uiPanel == null || tray == null || Screen.width < 8 || Screen.height < 8)
                return;

            Canvas.ForceUpdateCanvases();
            uiPanel.GetWorldCorners(_corners);
            var canvas = uiPanel.GetComponentInParent<Canvas>();
            Camera cam = null;
            if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                cam = eventCamera != null ? eventCamera : (canvas.worldCamera != null ? canvas.worldCamera : Camera.main);

            var minX = float.PositiveInfinity;
            var maxX = float.NegativeInfinity;
            var minY = float.PositiveInfinity;
            var maxY = float.NegativeInfinity;
            for (var i = 0; i < 4; i++)
            {
                var screen = RectTransformUtility.WorldToScreenPoint(cam, _corners[i]);
                minX = Mathf.Min(minX, screen.x);
                maxX = Mathf.Max(maxX, screen.x);
                minY = Mathf.Min(minY, screen.y);
                maxY = Mathf.Max(maxY, screen.y);
            }

            var nx0 = Mathf.Clamp01(minX / Screen.width);
            var nx1 = Mathf.Clamp01(maxX / Screen.width);
            var ny0 = Mathf.Clamp01(minY / Screen.height);
            var ny1 = Mathf.Clamp01(maxY / Screen.height);

            var landscape = Screen.width > Screen.height;
            Rect visible;
            if (landscape)
            {
                // Screen left → score sheet (same rule as GameplayScreen.TryComputePlayZones).
                var top = Mathf.Clamp01(1f - Mathf.Max(stripClearance, 0.08f));
                var right = Mathf.Clamp01(nx0 - Mathf.Max(edgePad, 0.018f));
                var left = 0.012f;
                if (right < left + 0.3f)
                    right = Mathf.Min(0.72f, left + 0.4f);
                var bottom = Mathf.Clamp(bottomReserve, 0.14f, 0.4f);
                if (top - bottom < 0.2f)
                    top = Mathf.Min(0.92f, bottom + 0.28f);
                visible = Rect.MinMaxRect(left, bottom, right, top);
            }
            else
            {
                // Sheet on top — tray between pedestal and sheet, with side padding.
                visible = Rect.MinMaxRect(
                    0.03f,
                    Mathf.Clamp(bottomReserve, 0.12f, 0.4f),
                    0.97f,
                    Mathf.Clamp01(ny0 - edgePad - sheetClearance));
            }

            if (visible.width < 0.15f || visible.height < 0.12f)
                return;

            tray.ApplyNormalizedVisibleRect(visible);
            _lastW = Screen.width;
            _lastH = Screen.height;
            _lastLandscape = landscape;
            _lastBottom = bottomReserve;
            _lastSheetMidX = (_corners[0].x + _corners[2].x) * 0.5f;
            _lastSheetMidY = (_corners[0].y + _corners[2].y) * 0.5f;
        }

        private void TryAutoBind()
        {
            if (uiPanel == null)
            {
                var sheet = GameObject.Find("ScoreSheet");
                if (sheet != null)
                    uiPanel = sheet.GetComponent<RectTransform>();
            }

            if (tray == null)
                tray = Object.FindAnyObjectByType<ProceduralDiceTray>();
        }
    }
}
