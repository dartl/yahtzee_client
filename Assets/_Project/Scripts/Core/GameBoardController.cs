using System.Collections.Generic;
using UnityEngine;

namespace Yahtzee.Core
{
    public sealed class GameBoardController : MonoBehaviour
    {
        [SerializeField] private Vector2 boardSize = new Vector2(3f, 3f);
        [SerializeField] private float borderHeight = 0.2f;
        [SerializeField] private float borderThickness = 0.14f;
        [SerializeField] private MeshRenderer bottomRenderer;
        [SerializeField] private MeshRenderer borderRenderer;
        [SerializeField] private GameBoardSkin skin;

        private readonly List<MeshRenderer> _borderRenderers = new List<MeshRenderer>(8);
        private PhysicsMaterial _surface;
        private bool _bordersReady;
        private int _viewWidth = -1;
        private int _viewHeight = -1;
        private bool _hasPit;
        private bool _hasShelf;
        private readonly Vector3[] _pitCorners = new Vector3[4];
        private readonly Vector3[] _shelfCorners = new Vector3[4];
        private float _pitMinX;
        private float _pitMaxX;
        private float _pitMinZ;
        private float _pitMaxZ;
        private int _layoutRevision;
        private readonly Vector3[] _keepSlots = new Vector3[KeepSlotCount];
        private Material _borderMaterial;
        private Material _feltMaterial;
        private Material _nestMaterial;
        private Material _keepPlatformMaterial;
        private Material _keepGlowMaterial;
        private Material _keepWellMaterial;
        private BoxCollider _shelfGate;
        private Collider _shelfRail;
        private bool _hasUiZones;
        private Rect _lastPitNorm;
        private Rect _lastShelfNorm;
        private ProceduralDiceTray _proceduralTray;
        private bool _legacyBordersHidden;

        private const float CollisionHeight = 2.8f;
        private const int GuardLayer = 2;
        private const int KeepSlotCount = 5;
        private const float ScreenInset = 0f;

        // Corner order: 0=BL, 1=BR, 2=TR, 3=TL (screen-space bottom-left origin)
        private const int Bl = 0;
        private const int Br = 1;
        private const int Tr = 2;
        private const int Tl = 3;

        private void Start()
        {
            AdjustBordersToViewport();
        }

        private void Update()
        {
            if (Screen.width == _viewWidth && Screen.height == _viewHeight)
                return;

            AdjustBordersToViewport();
        }

        public int LayoutRevision => _layoutRevision;

        /// <summary>
        /// Reprojects the last UI zones, or uses portrait/landscape defaults before UI sync.
        /// Does not re-inset stored rects (avoids tray shrinking each throw).
        /// </summary>
        public void AdjustBordersToViewport()
        {
            if (_hasUiZones)
            {
                ApplyPlayZones(_lastPitNorm, _lastShelfNorm, fromUi: false, sanitize: false);
                return;
            }

            var landscape = Screen.width > Screen.height;
            Rect pit;
            Rect shelf;
            if (landscape)
            {
                // Free column: screen left → score sheet; pedestal centered under tray.
                shelf = Rect.MinMaxRect(0.012f, 0.08f, 0.62f, 0.2f);
                pit = Rect.MinMaxRect(0.012f, 0.21f, 0.62f, 0.88f);
            }
            else
            {
                shelf = Rect.MinMaxRect(0.04f, 0.11f, 0.96f, 0.24f);
                pit = Rect.MinMaxRect(0.04f, 0.25f, 0.96f, 0.48f);
            }

            ApplyPlayZones(pit, shelf, fromUi: false, sanitize: true);
        }

        /// <summary>
        /// Normalized screen rects (x,y in 0..1, y=0 at bottom). Pit is the throw box; shelf is the keep pedestal.
        /// </summary>
        public void ApplyPlayZones(Rect pitNormalized, Rect shelfNormalized)
        {
            ApplyPlayZones(pitNormalized, shelfNormalized, fromUi: true, sanitize: true);
        }

        private void ApplyPlayZones(Rect pitNormalized, Rect shelfNormalized, bool fromUi, bool sanitize)
        {
            var camera = Camera.main;
            if (camera == null)
                return;

            CreatePhysicalBorders();
            var planeY = transform.position.y;

            var pit = sanitize ? SanitizeNormRect(pitNormalized, 0.2f, 0.16f) : pitNormalized;
            var shelf = sanitize ? SanitizeNormRect(shelfNormalized, 0.2f, 0.08f) : shelfNormalized;
            if (fromUi
                && _hasUiZones
                && Screen.width == _viewWidth
                && Screen.height == _viewHeight
                && ApproxRect(pit, _lastPitNorm)
                && ApproxRect(shelf, _lastShelfNorm))
                return;

            if (fromUi)
            {
                _hasUiZones = true;
                _lastPitNorm = pit;
                _lastShelfNorm = shelf;
            }

            if (!ProjectNormRect(camera, pit, planeY, _pitCorners)
                || !ProjectNormRect(camera, shelf, planeY, _shelfCorners))
            {
                _viewWidth = Screen.width;
                _viewHeight = Screen.height;
                return;
            }

            AlignShelfWidthToPit();
            // Shared mouth: pit south edge = shelf north edge.
            _pitCorners[Bl] = new Vector3(_shelfCorners[Tl].x, planeY, _shelfCorners[Tl].z);
            _pitCorners[Br] = new Vector3(_shelfCorners[Tr].x, planeY, _shelfCorners[Tr].z);

            CachePitBounds();
            _hasPit = (_pitMaxX - _pitMinX) > 0.25f && (_pitMaxZ - _pitMinZ) > 0.25f;

            // Visual crate comes from ProceduralDiceTray (margins driven by TrayUIBinder when present).
            var tray = EnsureProceduralTray();
            if (tray != null)
            {
                HideLegacyBorderVisuals();
                if (Object.FindAnyObjectByType<Yahtzee.UI.TrayUIBinder>() == null)
                {
                    // Pit only — pedestal sits below the crate south edge.
                    var trayRect = Rect.MinMaxRect(pit.xMin, pit.yMin, pit.xMax, pit.yMax);
                    tray.ApplyNormalizedVisibleRect(trayRect);
                }
            }
            else
            {
                PlaceLegacyCubeFrame(camera, pit, planeY);
            }

            // Keep UI-projected shelf for the pedestal (visible under the crate on screen).
            var thickness = Mathf.Max(0.08f, borderThickness);
            var height = VisualHeight();
            PlaceKeepPedestal(planeY, thickness, height);
            SetMarkerActive("PitFelt", false);

            _viewWidth = Screen.width;
            _viewHeight = Screen.height;
            _layoutRevision++;
        }

        /// <summary>
        /// Snap pit/shelf physics to the procedural crate after a tray rebuild (e.g. camera reframe).
        /// </summary>
        public void AlignPhysicsToTray()
        {
            AlignKeepBayToTray(0.16f);
        }

        /// <summary>
        /// Park the keep bay flush under the crate south lip (same width as the AABB tray).
        /// </summary>
        /// <param name="shelfHeightNorm">Normalized screen height of the keep band (fallback depth).</param>
        public void AlignKeepBayToTray(float shelfHeightNorm)
        {
            var tray = EnsureProceduralTray();
            if (tray == null)
                return;

            var planeY = transform.position.y;
            AlignPhysicsToTray(planeY, shelfHeightNorm);
            var thickness = Mathf.Max(0.08f, borderThickness);
            PlaceKeepPedestal(planeY, thickness, VisualHeight());
            _layoutRevision++;
        }

        private void AlignPhysicsToTray(float planeY, float shelfHeightNorm = 0.09f)
        {
            var tray = _proceduralTray != null ? _proceduralTray : GetComponent<ProceduralDiceTray>();
            if (tray == null || !tray.TryGetFloorCorners(_pitCorners))
                return;

            for (var i = 0; i < 4; i++)
                _pitCorners[i].y = planeY;

            // After yaw=0 + AABB tray, X edges are shared; depth from UI shelf band.
            var trayWidth = Mathf.Abs(_pitCorners[Br].x - _pitCorners[Bl].x);
            var trayDepth = Mathf.Abs(_pitCorners[Tl].z - _pitCorners[Bl].z);
            var shelfDepth = Mathf.Clamp(
                trayDepth * Mathf.Clamp(shelfHeightNorm / 0.55f, 0.18f, 0.42f),
                0.14f,
                Mathf.Max(0.16f, trayWidth * 0.28f));

            _shelfCorners[Tl] = _pitCorners[Bl];
            _shelfCorners[Tr] = _pitCorners[Br];
            _shelfCorners[Bl] = new Vector3(_pitCorners[Bl].x, planeY, _pitCorners[Bl].z - shelfDepth);
            _shelfCorners[Br] = new Vector3(_pitCorners[Br].x, planeY, _pitCorners[Br].z - shelfDepth);
            _shelfCorners[Tl].y = planeY;
            _shelfCorners[Tr].y = planeY;

            CachePitBounds();
            _hasPit = (_pitMaxX - _pitMinX) > 0.25f && (_pitMaxZ - _pitMinZ) > 0.25f;
        }

        private ProceduralDiceTray EnsureProceduralTray()
        {
            if (_proceduralTray == null)
            {
                _proceduralTray = GetComponent<ProceduralDiceTray>();
                if (_proceduralTray == null)
                    _proceduralTray = gameObject.AddComponent<ProceduralDiceTray>();
            }

            HideLegacyBorderVisuals();
            return _proceduralTray;
        }

        private string _appliedTrayStyleKey;

        /// <summary>
        /// One tray look for every mode (bot / online / pass&amp;play / bluetooth).
        /// </summary>
        public void ApplyPlayerTrayStyle(bool force = false)
        {
            var tray = EnsureProceduralTray();
            HideLegacyBorderVisuals();
            var skin = BoardSkinConfig.Active;
            // Geometry revision bumps force a rebuild after tray/pedestal redesigns.
            var key = skin != null
                ? skin.skinId + "|" + ColorUtility.ToHtmlStringRGB(skin.floorColor) + "|" + ColorUtility.ToHtmlStringRGB(skin.wallColor) + "|box-v3"
                : "default|box-v3";
            if (!force && _appliedTrayStyleKey == key && tray != null && tray.HasBounds)
            {
                HideLegacyBorderVisuals();
                return;
            }

            _appliedTrayStyleKey = key;
            if (tray != null)
                tray.ApplyActiveSkin();
            HideLegacyBorderVisuals();
            ReleaseKeepMaterials();
            if (_hasShelf)
                PlaceKeepPedestal(transform.position.y, Mathf.Max(0.08f, borderThickness), VisualHeight());
        }

        private void ReleaseKeepMaterials()
        {
            DestroyMaterial(ref _keepPlatformMaterial);
            DestroyMaterial(ref _keepGlowMaterial);
            DestroyMaterial(ref _nestMaterial);
            DestroyMaterial(ref _keepWellMaterial);
        }

        private static void DestroyMaterial(ref Material material)
        {
            if (material == null)
                return;
            if (Application.isPlaying)
                Object.Destroy(material);
            else
                Object.DestroyImmediate(material);
            material = null;
        }

        private void HideLegacyBorderVisuals()
        {
            _legacyBordersHidden = true;
            // Legacy Border_* cubes looked like black strips that stopped before corners.
            for (var index = 0; index < transform.childCount; index++)
            {
                var child = transform.GetChild(index);
                if (child == null)
                    continue;

                var name = child.name;
                if (name.StartsWith("Tray", System.StringComparison.Ordinal))
                    continue;
                if (name.StartsWith("KeepPlatform", System.StringComparison.Ordinal)
                    || name.StartsWith("KeepFrame", System.StringComparison.Ordinal)
                    || name.StartsWith("KeepNest", System.StringComparison.Ordinal)
                    || name.StartsWith("KeepWell", System.StringComparison.Ordinal)
                    || name == "ShelfGate")
                    continue;

                var isLegacy = name.StartsWith("Border_", System.StringComparison.Ordinal)
                    || name.StartsWith("Corner_", System.StringComparison.Ordinal)
                    || name == "PitFelt"
                    || name == "KeepTray"
                    || name.EndsWith("_Guard", System.StringComparison.Ordinal);
                if (!isLegacy)
                    continue;

                var renderer = child.GetComponent<MeshRenderer>();
                if (renderer != null)
                    renderer.enabled = false;
                var filter = child.GetComponent<MeshFilter>();
                if (filter != null)
                    filter.sharedMesh = null;

                // Corners / felt markers off; Border_* stay for invisible colliders only.
                if (!name.StartsWith("Border_", System.StringComparison.Ordinal))
                    child.gameObject.SetActive(false);
            }

            for (var index = 0; index < _borderRenderers.Count; index++)
            {
                if (_borderRenderers[index] != null)
                    _borderRenderers[index].enabled = false;
            }
        }

        private void SetBorderActive(string wallName, bool active)
        {
            var wall = transform.Find(wallName);
            if (wall != null)
            {
                var renderer = wall.GetComponent<MeshRenderer>();
                if (renderer != null)
                    renderer.enabled = active;
                var collider = wall.GetComponent<Collider>();
                if (collider != null)
                    collider.enabled = active;
            }

            var guard = transform.Find(wallName + "_Guard");
            if (guard != null)
                guard.gameObject.SetActive(active);
        }

        private void PlaceLegacyCubeFrame(Camera camera, Rect pit, float planeY)
        {
            var thickness = Mathf.Max(0.08f, borderThickness);
            var maxX = Mathf.Max(0f, Screen.width - 1f);
            var frameBl = ScreenPointOnTable(camera, Mathf.Lerp(0f, maxX, pit.xMin), 0f, planeY);
            var frameBr = ScreenPointOnTable(camera, Mathf.Lerp(0f, maxX, pit.xMax), 0f, planeY);
            frameBl.x = _shelfCorners[Bl].x;
            frameBr.x = _shelfCorners[Br].x;
            frameBl.y = planeY;
            frameBr.y = planeY;

            var height = VisualHeight();
            var frameCenter = (
                _pitCorners[Tl] + _pitCorners[Tr] + frameBl + frameBr) * 0.25f;
            PlaceEdgeWall("Border_North", _pitCorners[Tr], _pitCorners[Tl], height, thickness, frameCenter, 1f);
            PlaceEdgeWall("Border_South", frameBl, frameBr, height, thickness, frameCenter, 1f);
            PlaceEdgeWall("Border_West", _pitCorners[Tl], frameBl, height, thickness, frameCenter, 1f);
            PlaceEdgeWall("Border_East", _pitCorners[Tr], frameBr, height, thickness, frameCenter, 1f);
            PlaceCorner("Corner_TL", _pitCorners[Tl], planeY, thickness);
            PlaceCorner("Corner_TR", _pitCorners[Tr], planeY, thickness);
            PlaceCorner("Corner_BL", frameBl, planeY, thickness);
            PlaceCorner("Corner_BR", frameBr, planeY, thickness);
            PlacePitFelt(planeY);
        }

        private static bool ApproxRect(Rect a, Rect b)
        {
            const float eps = 0.0025f;
            return Mathf.Abs(a.xMin - b.xMin) <= eps
                && Mathf.Abs(a.xMax - b.xMax) <= eps
                && Mathf.Abs(a.yMin - b.yMin) <= eps
                && Mathf.Abs(a.yMax - b.yMax) <= eps;
        }

        public bool TryKeepSlot(int index, out Vector3 tablePoint)
        {
            if (!_hasShelf || index < 0 || index >= KeepSlotCount)
            {
                tablePoint = default;
                return false;
            }

            tablePoint = _keepSlots[index];
            return true;
        }

        public bool TryPitArea(out Vector3 min, out Vector3 max)
        {
            min = default;
            max = default;
            if (!_hasPit)
                return false;

            min = new Vector3(_pitMinX, transform.position.y, _pitMinZ);
            max = new Vector3(_pitMaxX, transform.position.y, _pitMaxZ);
            return max.x > min.x && max.z > min.z;
        }

        public bool TryPitCenter(out Vector3 tablePoint)
        {
            if (!_hasPit)
            {
                tablePoint = default;
                return false;
            }

            tablePoint = (_pitCorners[Bl] + _pitCorners[Br] + _pitCorners[Tr] + _pitCorners[Tl]) * 0.25f;
            tablePoint.y = transform.position.y;
            return true;
        }

        public bool KeepsDie(Vector3 worldPoint, float radius)
        {
            if (!_hasPit)
                return true;

            var pad = Mathf.Max(0f, radius);
            return PointInInsetQuad(worldPoint, _pitCorners, pad);
        }

        private void CachePitBounds()
        {
            _pitMinX = Mathf.Min(Mathf.Min(_pitCorners[Bl].x, _pitCorners[Br].x), Mathf.Min(_pitCorners[Tr].x, _pitCorners[Tl].x));
            _pitMaxX = Mathf.Max(Mathf.Max(_pitCorners[Bl].x, _pitCorners[Br].x), Mathf.Max(_pitCorners[Tr].x, _pitCorners[Tl].x));
            _pitMinZ = Mathf.Min(Mathf.Min(_pitCorners[Bl].z, _pitCorners[Br].z), Mathf.Min(_pitCorners[Tr].z, _pitCorners[Tl].z));
            _pitMaxZ = Mathf.Max(Mathf.Max(_pitCorners[Bl].z, _pitCorners[Br].z), Mathf.Max(_pitCorners[Tr].z, _pitCorners[Tl].z));
        }

        private void AlignShelfWidthToPit()
        {
            // Match shelf left/right rails to the pit so sides read as one continuous crate.
            _shelfCorners[Bl].x = _pitCorners[Bl].x;
            _shelfCorners[Tl].x = _pitCorners[Tl].x;
            _shelfCorners[Br].x = _pitCorners[Br].x;
            _shelfCorners[Tr].x = _pitCorners[Tr].x;
        }

        private void PlaceKeepPedestal(float planeY, float thickness, float outerRailHeight)
        {
            var shelfMinX = Mathf.Min(Mathf.Min(_shelfCorners[Bl].x, _shelfCorners[Br].x), Mathf.Min(_shelfCorners[Tr].x, _shelfCorners[Tl].x));
            var shelfMaxX = Mathf.Max(Mathf.Max(_shelfCorners[Bl].x, _shelfCorners[Br].x), Mathf.Max(_shelfCorners[Tr].x, _shelfCorners[Tl].x));
            var shelfMinZ = Mathf.Min(Mathf.Min(_shelfCorners[Bl].z, _shelfCorners[Br].z), Mathf.Min(_shelfCorners[Tr].z, _shelfCorners[Tl].z));
            var shelfMaxZ = Mathf.Max(Mathf.Max(_shelfCorners[Bl].z, _shelfCorners[Br].z), Mathf.Max(_shelfCorners[Tr].z, _shelfCorners[Tl].z));
            var width = shelfMaxX - shelfMinX;
            var depth = shelfMaxZ - shelfMinZ;
            if (width < 0.35f || depth < 0.12f)
            {
                _hasShelf = false;
                if (_shelfGate != null)
                    _shelfGate.gameObject.SetActive(false);
                SetMarkerActive("KeepTray", false);
                SetMarkerActive("KeepPlatform", false);
                SetMarkerActive("KeepPlatformGlow", false);
                SetMarkerActive("KeepFrame_S", false);
                SetMarkerActive("KeepFrame_N", false);
                SetMarkerActive("KeepFrame_W", false);
                SetMarkerActive("KeepFrame_E", false);
                SetMarkerActive("Border_Shelf", false);
                for (var index = 0; index < KeepSlotCount; index++)
                {
                    SetMarkerActive("KeepNest_" + index, false);
                    SetMarkerActive("KeepWell_" + index, false);
                }
                return;
            }

            // Pedestal centered under the throw crate (same width span).
            var pedestalHeight = 0.07f;
            var surfaceY = planeY + pedestalHeight;
            var midX = (shelfMinX + shelfMaxX) * 0.5f;
            var midZ = (shelfMinZ + shelfMaxZ) * 0.5f;
            var platformWidth = Mathf.Max(0.5f, width * 0.92f);
            var platformDepth = Mathf.Max(0.14f, depth * 0.88f);
            var slotSpan = platformWidth / KeepSlotCount;
            var slotOriginX = midX - platformWidth * 0.5f;
            for (var index = 0; index < KeepSlotCount; index++)
            {
                _keepSlots[index] = new Vector3(
                    slotOriginX + slotSpan * (index + 0.5f),
                    surfaceY,
                    midZ);
            }

            var frameCenter = (
                _pitCorners[Tl] + _pitCorners[Tr] + _shelfCorners[Bl] + _shelfCorners[Br]) * 0.25f;
            PlaceEdgeWall(
                "Border_Shelf",
                _shelfCorners[Tl],
                _shelfCorners[Tr],
                pedestalHeight,
                Mathf.Max(0.04f, thickness * 0.35f),
                frameCenter,
                0.5f);
            var shelfRail = transform.Find("Border_Shelf");
            _shelfRail = shelfRail != null ? shelfRail.GetComponent<BoxCollider>() : null;
            HideBorderMesh("Border_Shelf");

            var gateMid = (_shelfCorners[Tl] + _shelfCorners[Tr]) * 0.5f;
            var gateDelta = _shelfCorners[Tr] - _shelfCorners[Tl];
            gateDelta.y = 0f;
            PlaceShelfGate(planeY, gateMid, gateDelta.magnitude, gateDelta.normalized);

            SetMarkerActive("KeepTray", false);
            PlaceKeepPlatform(surfaceY, midX, midZ, platformWidth, platformDepth, pedestalHeight);
            PlaceKeepNests(surfaceY, platformDepth, slotSpan);
            _hasShelf = true;
        }

        private void HideBorderMesh(string wallName)
        {
            var wall = transform.Find(wallName);
            if (wall == null)
                return;

            var renderer = wall.GetComponent<MeshRenderer>();
            if (renderer != null)
                renderer.enabled = false;

            var guard = transform.Find(wallName + "_Guard");
            if (guard != null)
            {
                var guardRenderer = guard.GetComponent<MeshRenderer>();
                if (guardRenderer != null)
                    guardRenderer.enabled = false;
            }
        }

        private void PlaceKeepPlatform(float surfaceY, float midX, float midZ, float width, float depth, float height)
        {
            var h = Mathf.Max(0.06f, height);
            var rimT = 0.032f;
            var wallY = surfaceY - h * 0.5f;
            var deckH = Mathf.Max(0.02f, h * 0.35f);

            // Soft white deck inset inside the slate frame (same fill as the throw crate).
            var platform = EnsureMarker("KeepPlatform", PrimitiveType.Cube, KeepPlatformMaterial());
            FitMarker(
                platform,
                new Vector3(midX, surfaceY - deckH * 0.5f, midZ),
                new Vector3(Mathf.Max(0.2f, width - rimT * 2.2f), deckH, Mathf.Max(0.08f, depth - rimT * 2.2f)));

            // Outer slate plinth under the bay.
            var plinth = EnsureMarker("KeepPlatformGlow", PrimitiveType.Cube, KeepGlowMaterial());
            FitMarker(
                plinth,
                new Vector3(midX, surfaceY - h + 0.012f, midZ),
                new Vector3(width + 0.024f, 0.024f, depth + 0.024f));

            // Full-height slate frame — mini-box matching the throw crate.
            PlaceKeepFrameWall("KeepFrame_S", midX, wallY, midZ - depth * 0.5f + rimT * 0.5f, width, h, rimT);
            PlaceKeepFrameWall("KeepFrame_N", midX, wallY, midZ + depth * 0.5f - rimT * 0.5f, width, h, rimT);
            PlaceKeepFrameWall("KeepFrame_W", midX - width * 0.5f + rimT * 0.5f, wallY, midZ, rimT, h, depth - rimT * 2f);
            PlaceKeepFrameWall("KeepFrame_E", midX + width * 0.5f - rimT * 0.5f, wallY, midZ, rimT, h, depth - rimT * 2f);
        }

        private void PlaceKeepFrameWall(string markerName, float x, float y, float z, float sizeX, float sizeY, float sizeZ)
        {
            var wall = EnsureMarker(markerName, PrimitiveType.Cube, KeepGlowMaterial());
            FitMarker(wall, new Vector3(x, y, z), new Vector3(Mathf.Max(0.01f, sizeX), Mathf.Max(0.01f, sizeY), Mathf.Max(0.01f, sizeZ)));
        }

        private void PlaceKeepNests(float surfaceY, float depth, float slotSpan)
        {
            var nestSize = Mathf.Clamp(Mathf.Min(slotSpan * 0.7f, depth * 0.72f), 0.1f, 0.18f);
            var wellSize = nestSize * 0.8f;
            for (var index = 0; index < KeepSlotCount; index++)
            {
                var slot = _keepSlots[index];
                var ring = EnsureMarker("KeepNest_" + index, PrimitiveType.Cylinder, KeepRimMaterial());
                FitMarker(ring, new Vector3(slot.x, surfaceY + 0.002f, slot.z), new Vector3(nestSize, 0.005f, nestSize));

                var well = EnsureMarker("KeepWell_" + index, PrimitiveType.Cylinder, KeepWellMaterial());
                FitMarker(well, new Vector3(slot.x, surfaceY + 0.0045f, slot.z), new Vector3(wellSize, 0.0035f, wellSize));
            }
        }

        private void PlacePitFelt(float planeY)
        {
            if (!_hasPit)
                return;

            var felt = EnsureMarker("PitFelt", PrimitiveType.Cube, FeltMaterial());
            var center = (_pitCorners[Bl] + _pitCorners[Br] + _pitCorners[Tr] + _pitCorners[Tl]) * 0.25f;
            center.y = planeY + 0.002f;
            var width = Mathf.Max(0.2f, _pitMaxX - _pitMinX);
            var depth = Mathf.Max(0.2f, _pitMaxZ - _pitMinZ);
            FitMarker(felt, center, new Vector3(width * 0.98f, 0.004f, depth * 0.98f));
        }

        private Transform EnsureMarker(string markerName, PrimitiveType primitive, Material material)
        {
            var marker = transform.Find(markerName);
            if (marker == null)
            {
                var created = GameObject.CreatePrimitive(primitive);
                created.name = markerName;
                created.transform.SetParent(transform, false);
                var collider = created.GetComponent<Collider>();
                if (collider != null)
                    Destroy(collider);
                marker = created.transform;
            }

            marker.gameObject.SetActive(true);
            var renderer = marker.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            return marker;
        }

        private void SetMarkerActive(string markerName, bool active)
        {
            var marker = transform.Find(markerName);
            if (marker != null)
                marker.gameObject.SetActive(active);
        }

        private void FitMarker(Transform marker, Vector3 worldCenter, Vector3 worldSize)
        {
            marker.SetPositionAndRotation(worldCenter, Quaternion.identity);
            var lossy = transform.lossyScale;
            marker.localScale = new Vector3(
                worldSize.x / Mathf.Max(0.0001f, Mathf.Abs(lossy.x)),
                worldSize.y / Mathf.Max(0.0001f, Mathf.Abs(lossy.y)),
                worldSize.z / Mathf.Max(0.0001f, Mathf.Abs(lossy.z)));
        }

        private Material NestMaterial()
        {
            return KeepRimMaterial();
        }

        private Material KeepPlatformMaterial()
        {
            var board = BoardSkinConfig.Active;
            // Match tray pit — soft white slab.
            return MarkerMaterial(ref _keepPlatformMaterial, board.floorColor, board.floorSmoothness, false, Color.black, 0f);
        }

        private Material KeepGlowMaterial()
        {
            var board = BoardSkinConfig.Active;
            // Outer frame of the keep bay — same slate as tray walls.
            return MarkerMaterial(ref _keepGlowMaterial, board.wallColor, board.wallSmoothness, false, Color.black, 0f);
        }

        private Material KeepRimMaterial()
        {
            var board = BoardSkinConfig.Active;
            return MarkerMaterial(ref _nestMaterial, board.wallColor, board.wallSmoothness, false, Color.black, 0f);
        }

        private Material KeepWellMaterial()
        {
            var board = BoardSkinConfig.Active;
            var well = Color.Lerp(board.floorColor, board.wallColor, 0.12f);
            return MarkerMaterial(ref _keepWellMaterial, well, board.floorSmoothness, false, Color.black, 0f);
        }

        private Material MarkerMaterial(ref Material cache, Color color, float smoothness)
        {
            return MarkerMaterial(ref cache, color, smoothness, false, Color.black, 0f);
        }

        private Material MarkerMaterial(
            ref Material cache,
            Color color,
            float smoothness,
            bool emission,
            Color emissionColor,
            float emissionIntensity)
        {
            if (cache == null)
            {
                // Prefer flat Unlit so keep bay matches the throw crate (no specular banding).
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null)
                    shader = Shader.Find("Unlit/Color");
                if (shader == null)
                    shader = Shader.Find("Universal Render Pipeline/Simple Lit");
                if (shader == null)
                    shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                    shader = Shader.Find("Standard");
                if (shader == null)
                    shader = Shader.Find("Sprites/Default");

                cache = new Material(shader);
            }

            color.a = 1f;
            cache.color = color;
            if (cache.HasProperty("_BaseColor"))
                cache.SetColor("_BaseColor", color);
            if (cache.HasProperty("_Color"))
                cache.SetColor("_Color", color);
            if (cache.HasProperty("_Smoothness"))
                cache.SetFloat("_Smoothness", Mathf.Min(0.08f, smoothness));
            if (cache.HasProperty("_Metallic"))
                cache.SetFloat("_Metallic", 0f);
            if (emission)
            {
                cache.EnableKeyword("_EMISSION");
                if (cache.HasProperty("_EmissionColor"))
                    cache.SetColor("_EmissionColor", emissionColor * emissionIntensity);
            }
            else
            {
                cache.DisableKeyword("_EMISSION");
                if (cache.HasProperty("_EmissionColor"))
                    cache.SetColor("_EmissionColor", Color.black);
            }

            return cache;
        }

        public void LetDieCrossShelf(Collider die)
        {
            if (die == null)
                return;

            if (_shelfGate != null)
                Physics.IgnoreCollision(die, _shelfGate, true);
            if (_shelfRail != null)
                Physics.IgnoreCollision(die, _shelfRail, true);
        }

        private void PlaceShelfGate(float planeY, Vector3 midOnTable, float length, Vector3 along)
        {
            const float gateHeight = 1.8f;
            var gate = transform.Find("ShelfGate");
            if (gate == null)
            {
                var gateObject = new GameObject("ShelfGate");
                gateObject.transform.SetParent(transform, false);
                var box = gateObject.AddComponent<BoxCollider>();
                box.sharedMaterial = Surface();
                gate = gateObject.transform;
            }

            gate.gameObject.SetActive(true);
            gate.gameObject.layer = GuardLayer;
            if (along.sqrMagnitude < 0.0001f)
                along = Vector3.right;
            var rotation = Quaternion.LookRotation(along.normalized, Vector3.up);
            var center = new Vector3(midOnTable.x, planeY + gateHeight * 0.5f, midOnTable.z);
            gate.SetPositionAndRotation(center, rotation);
            var lossy = transform.lossyScale;
            gate.localScale = new Vector3(
                0.04f / Mathf.Max(0.0001f, Mathf.Abs(lossy.x)),
                gateHeight / Mathf.Max(0.0001f, Mathf.Abs(lossy.y)),
                Mathf.Max(0.2f, length) / Mathf.Max(0.0001f, Mathf.Abs(lossy.z)));
            _shelfGate = gate.GetComponent<BoxCollider>();
            if (_shelfGate == null)
                return;

            _shelfGate.size = Vector3.one;
            _shelfGate.center = Vector3.zero;
            _shelfGate.isTrigger = false;
        }

        private static Rect SanitizeNormRect(Rect rect, float minWidth, float minHeight)
        {
            var xMin = Mathf.Clamp01(Mathf.Min(rect.xMin, rect.xMax) + ScreenInset);
            var xMax = Mathf.Clamp01(Mathf.Max(rect.xMin, rect.xMax) - ScreenInset);
            var yMin = Mathf.Clamp01(Mathf.Min(rect.yMin, rect.yMax) + ScreenInset);
            var yMax = Mathf.Clamp01(Mathf.Max(rect.yMin, rect.yMax) - ScreenInset);
            if (xMax - xMin < minWidth)
            {
                var mid = (xMin + xMax) * 0.5f;
                xMin = Mathf.Clamp01(mid - minWidth * 0.5f);
                xMax = Mathf.Clamp01(mid + minWidth * 0.5f);
            }

            if (yMax - yMin < minHeight)
            {
                var mid = (yMin + yMax) * 0.5f;
                yMin = Mathf.Clamp01(mid - minHeight * 0.5f);
                yMax = Mathf.Clamp01(mid + minHeight * 0.5f);
            }

            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        private static bool ProjectNormRect(Camera camera, Rect norm, float planeY, Vector3[] corners)
        {
            if (corners == null || corners.Length < 4)
                return false;

            // Use inclusive pixel edges so 0..1 maps to the true screen border.
            var maxX = Mathf.Max(0f, Screen.width - 1f);
            var maxY = Mathf.Max(0f, Screen.height - 1f);
            corners[Bl] = ScreenPointOnTable(camera, Mathf.Lerp(0f, maxX, norm.xMin), Mathf.Lerp(0f, maxY, norm.yMin), planeY);
            corners[Br] = ScreenPointOnTable(camera, Mathf.Lerp(0f, maxX, norm.xMax), Mathf.Lerp(0f, maxY, norm.yMin), planeY);
            corners[Tr] = ScreenPointOnTable(camera, Mathf.Lerp(0f, maxX, norm.xMax), Mathf.Lerp(0f, maxY, norm.yMax), planeY);
            corners[Tl] = ScreenPointOnTable(camera, Mathf.Lerp(0f, maxX, norm.xMin), Mathf.Lerp(0f, maxY, norm.yMax), planeY);
            return true;
        }

        private static Vector3 ScreenPointOnTable(Camera camera, float screenX, float screenY, float planeY)
        {
            var ray = camera.ScreenPointToRay(new Vector3(screenX, screenY, 0f));
            var plane = new Plane(Vector3.up, new Vector3(0f, planeY, 0f));
            if (plane.Raycast(ray, out var hit))
                return ray.GetPoint(hit);

            var fallback = ray.origin + ray.direction * 5f;
            fallback.y = planeY;
            return fallback;
        }

        private static bool PointInInsetQuad(Vector3 point, Vector3[] quad, float inset)
        {
            if (quad == null || quad.Length < 4)
                return false;

            if (inset <= 0.0001f)
                return PointInQuadXz(point, quad);

            var center = (quad[Bl] + quad[Br] + quad[Tr] + quad[Tl]) * 0.25f;
            var shrunk = new Vector3[4];
            for (var index = 0; index < 4; index++)
            {
                var to = quad[index] - center;
                to.y = 0f;
                var len = to.magnitude;
                if (len < 0.0001f)
                    shrunk[index] = quad[index];
                else
                {
                    var pull = Mathf.Min(inset, len * 0.45f);
                    shrunk[index] = center + to * ((len - pull) / len);
                    shrunk[index].y = point.y;
                }
            }

            return PointInQuadXz(point, shrunk);
        }

        private static bool PointInQuadXz(Vector3 point, Vector3[] quad)
        {
            var a = CrossZ(quad[Bl], quad[Br], point);
            var b = CrossZ(quad[Br], quad[Tr], point);
            var c = CrossZ(quad[Tr], quad[Tl], point);
            var d = CrossZ(quad[Tl], quad[Bl], point);
            return (a >= 0f && b >= 0f && c >= 0f && d >= 0f)
                || (a <= 0f && b <= 0f && c <= 0f && d <= 0f);
        }

        public bool ValidatePlayLayout(out string message)
        {
            if (!_hasPit)
            {
                message = "Pit zone missing.";
                return false;
            }

            if (!TryPitCenter(out _))
            {
                message = "Pit center unavailable.";
                return false;
            }

            var pitSpanX = _pitMaxX - _pitMinX;
            var pitSpanZ = _pitMaxZ - _pitMinZ;
            if (pitSpanX < 0.3f || pitSpanZ < 0.3f)
            {
                message = "Pit too small: " + pitSpanX.ToString("0.00") + " x " + pitSpanZ.ToString("0.00");
                return false;
            }

            if (!_hasShelf)
            {
                message = "OK (pit only, shelf collapsed).";
                return true;
            }

            message = "OK pit=" + pitSpanX.ToString("0.00") + "x" + pitSpanZ.ToString("0.00")
                + " shelf slots ready.";
            return true;
        }

        private static float CrossZ(Vector3 a, Vector3 b, Vector3 p)
        {
            return (b.x - a.x) * (p.z - a.z) - (b.z - a.z) * (p.x - a.x);
        }

        private static void InsetQuadTowardCenter(Vector3[] corners, float inset)
        {
            if (corners == null || corners.Length < 4 || inset <= 0.0001f)
                return;

            var center = (corners[Bl] + corners[Br] + corners[Tr] + corners[Tl]) * 0.25f;
            center.y = corners[Bl].y;
            for (var index = 0; index < 4; index++)
            {
                var point = corners[index];
                point.y = center.y;
                var to = point - center;
                to.y = 0f;
                var length = to.magnitude;
                if (length <= inset + 0.05f)
                    continue;

                corners[index] = center + to * ((length - inset) / length);
                corners[index].y = center.y;
            }
        }

        private void PlaceEdgeWall(string wallName, Vector3 a, Vector3 b, float height, float thickness)
        {
            PlaceEdgeWall(wallName, a, b, height, thickness, (a + b) * 0.5f, 1f);
        }

        private void PlaceEdgeWall(
            string wallName,
            Vector3 a,
            Vector3 b,
            float height,
            float thickness,
            Vector3 frameCenter,
            float towardCenterSign)
        {
            EnsureBorderCube(wallName);
            var wall = transform.Find(wallName);
            if (wall == null)
                return;

            var planeY = transform.position.y;
            a.y = planeY;
            b.y = planeY;
            var delta = b - a;
            delta.y = 0f;
            var length = delta.magnitude;
            if (length < 0.05f)
            {
                wall.gameObject.SetActive(false);
                var deadGuard = transform.Find(wallName + "_Guard");
                if (deadGuard != null)
                    deadGuard.gameObject.SetActive(false);
                return;
            }

            wall.gameObject.SetActive(true);
            var mid = (a + b) * 0.5f;
            // +1 pulls rail into the crate (visible); -1 parks the south rail outside the keep bay.
            var inward = frameCenter - mid;
            inward.y = 0f;
            if (inward.sqrMagnitude > 0.0001f)
                mid += inward.normalized * (thickness * 0.5f * towardCenterSign);
            mid.y = planeY + height * 0.5f;
            var rotation = Quaternion.LookRotation(delta / length, Vector3.up);
            wall.SetPositionAndRotation(mid, rotation);
            var lossy = transform.lossyScale;
            wall.localScale = new Vector3(
                thickness / Mathf.Max(0.0001f, Mathf.Abs(lossy.x)),
                height / Mathf.Max(0.0001f, Mathf.Abs(lossy.y)),
                length / Mathf.Max(0.0001f, Mathf.Abs(lossy.z)));

            var collider = wall.GetComponent<BoxCollider>();
            if (collider != null)
            {
                collider.size = Vector3.one;
                collider.center = Vector3.zero;
                collider.sharedMaterial = Surface();
            }

            var renderer = wall.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                // Visual rails come only from ProceduralDiceTray — Border_* are physics husks.
                renderer.enabled = false;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            var filter = wall.GetComponent<MeshFilter>();
            if (filter != null)
                filter.sharedMesh = null;

            PlaceInvisibleGuard(wallName, mid, rotation, thickness, height, length);
        }

        private void PlaceCorner(string cornerName, Vector3 tablePoint, float planeY, float thickness)
        {
            var marker = EnsureMarker(cornerName, PrimitiveType.Cube, BorderMaterial());
            var size = Mathf.Max(0.08f, thickness * 1.15f);
            FitMarker(
                marker,
                new Vector3(tablePoint.x, planeY + VisualHeight() * 0.55f, tablePoint.z),
                new Vector3(size, VisualHeight() * 1.1f, size));
        }

        private void EnsureBorderCube(string wallName)
        {
            var wall = transform.Find(wallName);
            if (wall != null)
                return;

            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = wallName;
            cube.transform.SetParent(transform, false);
            var renderer = cube.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = BorderMaterial();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            var collider = cube.GetComponent<BoxCollider>();
            collider.sharedMaterial = Surface();
            _borderRenderers.Add(renderer);
            if (borderRenderer == null)
                borderRenderer = renderer;
        }

        private void PlaceInvisibleGuard(
            string wallName,
            Vector3 visualCenter,
            Quaternion rotation,
            float thickness,
            float visualHeight,
            float length)
        {
            var guardName = wallName + "_Guard";
            var guard = transform.Find(guardName);
            if (guard == null)
            {
                var guardObject = new GameObject(guardName);
                guardObject.transform.SetParent(transform, false);
                var box = guardObject.AddComponent<BoxCollider>();
                box.sharedMaterial = Surface();
                guard = guardObject.transform;
            }

            guard.gameObject.SetActive(true);
            guard.gameObject.layer = GuardLayer;
            var extra = Mathf.Max(0.4f, CollisionHeight - visualHeight);
            var center = visualCenter + Vector3.up * (visualHeight * 0.5f + extra * 0.5f);
            guard.SetPositionAndRotation(center, rotation);
            var lossy = transform.lossyScale;
            guard.localScale = new Vector3(
                thickness / Mathf.Max(0.0001f, Mathf.Abs(lossy.x)),
                extra / Mathf.Max(0.0001f, Mathf.Abs(lossy.y)),
                length / Mathf.Max(0.0001f, Mathf.Abs(lossy.z)));

            var collider = guard.GetComponent<BoxCollider>();
            if (collider == null)
                return;

            collider.size = Vector3.one;
            collider.center = Vector3.zero;
            collider.isTrigger = false;
        }

        public static GameBoardController Ensure(Collider table)
        {
            var host = table != null ? table.gameObject : null;
            if (host == null)
            {
                var found = GameObject.Find("Table");
                host = found != null ? found : new GameObject("GameBoard");
            }

            var board = host.GetComponent<GameBoardController>();
            if (board == null)
                board = host.AddComponent<GameBoardController>();

            board.BindTable(table);
            board.CreatePhysicalBorders();
            board.ApplyPremiumLook();
            TableBoxSkinApplier.ApplySaved(board.gameObject);
            board.ApplyPlayerTrayStyle();
            return board;
        }

        private void Awake()
        {
            BindTable(GetComponent<Collider>());
            CreatePhysicalBorders();
            var activeSkin = skin != null ? skin : Resources.Load<GameBoardSkin>("GameBoardSkin");
            if (activeSkin != null)
                ApplySkin(activeSkin);
            ApplyPremiumLook();
            TableBoxSkinApplier.ApplySaved(gameObject);
            ApplyPlayerTrayStyle();
        }

        public void ApplyPremiumLook()
        {
            BindTable(GetComponent<Collider>());
            if (bottomRenderer != null && (skin == null || skin.trayBottomMaterial == null))
            {
                bottomRenderer.sharedMaterial = FeltMaterial();
                bottomRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                bottomRenderer.receiveShadows = true;
            }

            if (skin != null && skin.borderMaterial != null)
                return;

            var wood = BorderMaterial();
            for (var index = 0; index < _borderRenderers.Count; index++)
            {
                if (_borderRenderers[index] == null)
                    continue;

                _borderRenderers[index].sharedMaterial = wood;
                _borderRenderers[index].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
        }

        public void ApplySkin(GameBoardSkin next)
        {
            if (next == null)
                return;

            skin = next;
            if (bottomRenderer != null && next.trayBottomMaterial != null)
                bottomRenderer.sharedMaterial = next.trayBottomMaterial;

            if (next.borderMaterial == null)
                return;

            if (borderRenderer != null)
                borderRenderer.sharedMaterial = next.borderMaterial;

            for (var index = 0; index < _borderRenderers.Count; index++)
            {
                if (_borderRenderers[index] != null)
                    _borderRenderers[index].sharedMaterial = next.borderMaterial;
            }
        }

        public void CreatePhysicalBorders()
        {
            if (_bordersReady || transform.Find("Border_North") != null)
            {
                _bordersReady = true;
                CollectExistingBorders();
                EnsureBorderCube("Border_North");
                EnsureBorderCube("Border_South");
                EnsureBorderCube("Border_East");
                EnsureBorderCube("Border_West");
                EnsureBorderCube("Border_Shelf");
                return;
            }

            var width = Mathf.Max(1f, boardSize.x);
            var depth = Mathf.Max(1f, boardSize.y);
            var height = VisualHeight();
            var thickness = Mathf.Max(0.05f, borderThickness);
            var y = height * 0.5f;
            var halfWidth = width * 0.5f + thickness * 0.5f;
            var halfDepth = depth * 0.5f + thickness * 0.5f;

            CreateWall("Border_North", new Vector3(0f, y, halfDepth), new Vector3(width + thickness * 2f, height, thickness));
            CreateWall("Border_South", new Vector3(0f, y, -halfDepth), new Vector3(width + thickness * 2f, height, thickness));
            CreateWall("Border_East", new Vector3(halfWidth, y, 0f), new Vector3(thickness, height, depth));
            CreateWall("Border_West", new Vector3(-halfWidth, y, 0f), new Vector3(thickness, height, depth));
            EnsureBorderCube("Border_Shelf");
            _bordersReady = true;

            if (skin != null)
                ApplySkin(skin);
        }

        private void BindTable(Collider table)
        {
            if (bottomRenderer != null)
                return;

            if (table == null)
                table = GetComponent<Collider>();
            if (table != null)
                bottomRenderer = table.GetComponent<MeshRenderer>();
        }

        private void CreateWall(string wallName, Vector3 localCenter, Vector3 size)
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = wallName;
            wall.transform.SetParent(transform, false);
            wall.transform.localPosition = localCenter;
            wall.transform.localRotation = Quaternion.identity;
            wall.transform.localScale = size;

            var collider = wall.GetComponent<BoxCollider>();
            collider.sharedMaterial = Surface();

            var renderer = wall.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.sharedMaterial = BorderMaterial();
            _borderRenderers.Add(renderer);
            if (borderRenderer == null)
                borderRenderer = renderer;
        }

        private void CollectExistingBorders()
        {
            if (_borderRenderers.Count > 0)
                return;

            for (var index = 0; index < transform.childCount; index++)
            {
                var child = transform.GetChild(index);
                if (!child.name.StartsWith("Border_"))
                    continue;

                var renderer = child.GetComponent<MeshRenderer>();
                if (renderer != null)
                    _borderRenderers.Add(renderer);
            }

            if (borderRenderer == null && _borderRenderers.Count > 0)
                borderRenderer = _borderRenderers[0];

            var material = BorderMaterial();
            for (var index = 0; index < _borderRenderers.Count; index++)
            {
                if (_borderRenderers[index] != null)
                    _borderRenderers[index].sharedMaterial = material;
            }
        }

        private float VisualHeight()
        {
            return Mathf.Clamp(Mathf.Max(borderHeight, 0.22f), 0.16f, 0.32f);
        }

        private Material BorderMaterial()
        {
            if (skin != null && skin.borderMaterial != null)
                return skin.borderMaterial;

            if (_borderMaterial != null)
                return _borderMaterial;

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (shader == null)
                shader = Shader.Find("Standard");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");

            var board = BoardSkinConfig.Active;
            var color = board.tableRails;
            _borderMaterial = new Material(shader);
            _borderMaterial.color = color;
            if (_borderMaterial.HasProperty("_BaseColor"))
                _borderMaterial.SetColor("_BaseColor", color);
            if (_borderMaterial.HasProperty("_Smoothness"))
                _borderMaterial.SetFloat("_Smoothness", board.wallSmoothness);
            if (_borderMaterial.HasProperty("_Metallic"))
                _borderMaterial.SetFloat("_Metallic", 0f);
            _borderMaterial.DisableKeyword("_EMISSION");

            return _borderMaterial;
        }

        private Material FeltMaterial()
        {
            if (_feltMaterial != null)
                return _feltMaterial;

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (shader == null)
                shader = Shader.Find("Standard");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");

            var board = BoardSkinConfig.Active;
            var color = board.tableFelt;
            _feltMaterial = new Material(shader);
            _feltMaterial.color = color;
            if (_feltMaterial.HasProperty("_BaseColor"))
                _feltMaterial.SetColor("_BaseColor", color);
            if (_feltMaterial.HasProperty("_Smoothness"))
                _feltMaterial.SetFloat("_Smoothness", board.floorSmoothness);
            if (_feltMaterial.HasProperty("_Metallic"))
                _feltMaterial.SetFloat("_Metallic", 0f);
            _feltMaterial.DisableKeyword("_EMISSION");
            return _feltMaterial;
        }

        private PhysicsMaterial Surface()
        {
            if (_surface == null)
            {
                _surface = new PhysicsMaterial("YahtzeeWood")
                {
                    bounceCombine = PhysicsMaterialCombine.Maximum,
                    frictionCombine = PhysicsMaterialCombine.Average
                };
            }

            _surface.bounciness = 0.42f;
            _surface.dynamicFriction = 0.38f;
            _surface.staticFriction = 0.42f;
            return _surface;
        }
    }
}
