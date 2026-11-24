using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Yahtzee.Core
{
    /// <summary>
    /// Procedural adaptive dice tray: matte felt floor + solid rails (Classic Clean), colliders, viewport fit.
    /// Works in Play Mode and Edit Mode ([ExecuteAlways]).
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class ProceduralDiceTray : MonoBehaviour
    {
        [System.Serializable]
        public struct ViewportMargin
        {
            [Range(0f, 0.9f)] public float left;
            [Range(0f, 0.9f)] public float right;
            [Range(0f, 0.9f)] public float top;
            [Range(0f, 0.9f)] public float bottom;

            public static ViewportMargin PortraitMobile => new ViewportMargin
            {
                left = 0f,
                right = 0f,
                top = 0.60f,
                bottom = 0.10f
            };

            public static ViewportMargin LandscapeDesktop => new ViewportMargin
            {
                left = 0.012f,
                right = 0.38f,
                top = 0.12f,
                bottom = 0.18f
            };
        }

        [Header("Viewport Fit")]
        [SerializeField] private Camera targetCamera;
        [SerializeField] private ViewportMargin portraitMargins = ViewportMargin.PortraitMobile;
        [SerializeField] private ViewportMargin landscapeMargins = ViewportMargin.LandscapeDesktop;
        [SerializeField] private bool autoSwitchMarginsByOrientation = true;
        [SerializeField] private ViewportMargin customMargins = ViewportMargin.PortraitMobile;
        [SerializeField] private bool useCustomMargins;

        [Header("Tray Dimensions")]
        [SerializeField] private float floorY = 0f;
        [SerializeField] private float wallHeight = 0.14f;
        [SerializeField] private float wallThickness = 0.07f;
        [SerializeField] [Range(0f, 0.2f)] private float bevelWidth;
        [SerializeField] [Range(0f, 1f)] private float neonHeightFactor = 0.55f;
        [SerializeField] private float neonThickness = 0.012f;
        [SerializeField] private bool showRimBand;
        [SerializeField] private float lidHeight = 3.5f;

        [Header("Physics")]
        [SerializeField] private float bounciness = 0.45f;
        [SerializeField] private float dynamicFriction = 0.4f;
        [SerializeField] private float staticFriction = 0.42f;
        [SerializeField] private string dieTag = "Dice";
        [SerializeField] private bool pushDiceOnRebuild = true;

        [Header("Debug")]
        [SerializeField] private bool drawGizmos = true;

        private MeshFilter _floorFilter;
        private MeshRenderer _floorRenderer;
        private MeshFilter _wallsFilter;
        private MeshRenderer _wallsRenderer;
        private MeshFilter _neonFilter;
        private MeshRenderer _neonRenderer;

        private Mesh _floorMesh;
        private Mesh _wallsMesh;
        private Mesh _neonMesh;

        private BoxCollider _floorCollider;
        private readonly BoxCollider[] _wallColliders = new BoxCollider[4];
        private BoxCollider _lidCollider;
        private PhysicsMaterial _physicsMaterial;

        private Material _floorMat;
        private Material _wallMat;
        private Material _neonMat;

        private int _lastWidth = -1;
        private int _lastHeight = -1;
        private bool _lastLandscape;
        private Bounds _innerBounds;
        private Bounds _outerBounds;
        private bool _hasBounds;
        private FloorQuad _lastFloor;
        private bool _hasFloor;

        public Bounds InnerBounds => _innerBounds;
        public Bounds OuterBounds => _outerBounds;
        public bool HasBounds => _hasBounds;
        public float FloorY => floorY;

        private void OnEnable()
        {
            // Prefab/inspector can keep stale landscape margins from older right-side layouts.
            landscapeMargins = ViewportMargin.LandscapeDesktop;
            portraitMargins = ViewportMargin.PortraitMobile;
            bevelWidth = 0f;
            showRimBand = true;
            wallHeight = 0.16f;
            wallThickness = 0.085f;
            neonThickness = 0.028f;
            neonHeightFactor = 1f;
            EnsureHierarchy();
            Rebuild();
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return;
#endif
        }

        private void OnDestroy()
        {
            ReleaseMeshes();
            ReleaseMaterials();
        }

        private void Update()
        {
            if (Screen.width == _lastWidth && Screen.height == _lastHeight)
            {
                var landscape = Screen.width > Screen.height;
                if (landscape == _lastLandscape)
                    return;
            }

            Rebuild();
        }

        /// <summary>
        /// Drive tray from a normalized screen rect (x/y 0..1, y=0 at bottom).
        /// Used by gameplay UI so the tray matches score-sheet / button layout.
        /// </summary>
        public void ApplyNormalizedVisibleRect(Rect visibleNorm)
        {
            useCustomMargins = true;
            autoSwitchMarginsByOrientation = false;
            customMargins = new ViewportMargin
            {
                left = Mathf.Clamp01(visibleNorm.xMin),
                right = Mathf.Clamp01(1f - visibleNorm.xMax),
                bottom = Mathf.Clamp01(visibleNorm.yMin),
                top = Mathf.Clamp01(1f - visibleNorm.yMax)
            };
            Rebuild();
        }

        /// <summary>
        /// Drop UI-driven margins so orientation defaults apply (main menu / no gameplay).
        /// </summary>
        public void ClearCustomMargins()
        {
            useCustomMargins = false;
            autoSwitchMarginsByOrientation = true;
            Rebuild();
        }

        /// <summary>Re-apply the active board skin and rebuild geometry (all game modes).</summary>
        public void ApplyActiveSkin()
        {
            BoardSkinConfig.ResolveFromResources();
            var skin = BoardSkinConfig.Active;
            if (skin != null && skin.skinId == "classic-clean")
                skin.ApplyClassicCleanDefaults();

            // Recreate materials so Unlit/flat Classic Clean always wins over stale Lit mats.
            ReleaseMaterials();
            _lastWidth = -1;
            Rebuild();
        }

        [ContextMenu("Rebuild Dice Tray")]
        public void Rebuild()
        {
            EnsureHierarchy();
            EnsureMaterials();
            EnsurePhysicsMaterial();

            if (!TryComputeFloorQuad(out var quad))
            {
                _lastWidth = Screen.width;
                _lastHeight = Screen.height;
                _lastLandscape = Screen.width > Screen.height;
                return;
            }

            _lastFloor = quad;
            _hasFloor = true;
            BuildGeometry(quad);
            BuildColliders(quad);
            CacheBounds(quad);

            if (Application.isPlaying && pushDiceOnRebuild)
                PushDiceInside();

            _lastWidth = Screen.width;
            _lastHeight = Screen.height;
            _lastLandscape = Screen.width > Screen.height;
        }

        private struct FloorQuad
        {
            public Vector3 Bl;
            public Vector3 Br;
            public Vector3 Tr;
            public Vector3 Tl;

            public Vector3 Center
            {
                get
                {
                    var c = (Bl + Br + Tr + Tl) * 0.25f;
                    c.y = Bl.y;
                    return c;
                }
            }
        }

        public bool TryGetInnerArea(out Vector3 min, out Vector3 max)
        {
            min = default;
            max = default;
            if (!_hasBounds)
                return false;

            min = _innerBounds.min;
            max = _innerBounds.max;
            return true;
        }

        public bool TryGetCenter(out Vector3 center)
        {
            center = default;
            if (!_hasBounds)
                return false;

            center = _innerBounds.center;
            center.y = floorY;
            return true;
        }

        /// <summary>Outer floor corners in order BL, BR, TR, TL (y = floor).</summary>
        public bool TryGetFloorCorners(Vector3[] corners)
        {
            if (!_hasFloor || corners == null || corners.Length < 4)
                return false;

            corners[0] = _lastFloor.Bl;
            corners[1] = _lastFloor.Br;
            corners[2] = _lastFloor.Tr;
            corners[3] = _lastFloor.Tl;
            return true;
        }

        private ViewportMargin ActiveMargins()
        {
            if (useCustomMargins)
                return customMargins;

            if (!autoSwitchMarginsByOrientation)
                return portraitMargins;

            return Screen.width > Screen.height ? landscapeMargins : portraitMargins;
        }

        private bool TryComputeFloorQuad(out FloorQuad quad)
        {
            quad = default;
            var camera = targetCamera != null ? targetCamera : Camera.main;
            if (camera == null)
                return false;

            var margin = ActiveMargins();
            var x0 = Mathf.Clamp01(margin.left);
            var x1 = Mathf.Clamp01(1f - margin.right);
            var y0 = Mathf.Clamp01(margin.bottom);
            var y1 = Mathf.Clamp01(1f - margin.top);
            if (x1 - x0 < 0.08f || y1 - y0 < 0.08f)
                return false;

            // Project viewport corners, then take the INSCRIBED AABB so perspective does not
            // push the crate past the left/top of the Game view (outer AABB overshoots).
            var raw = new FloorQuad
            {
                Bl = ViewportToFloor(camera, x0, y0),
                Br = ViewportToFloor(camera, x1, y0),
                Tr = ViewportToFloor(camera, x1, y1),
                Tl = ViewportToFloor(camera, x0, y1)
            };
            raw.Bl.y = floorY;
            raw.Br.y = floorY;
            raw.Tr.y = floorY;
            raw.Tl.y = floorY;
            quad = ToInscribedAxisAlignedBox(raw);
            quad = InsetAxisAligned(quad, Mathf.Max(0.02f, wallThickness * 0.65f));

            var width = Mathf.Abs(quad.Br.x - quad.Bl.x);
            var depth = Mathf.Abs(quad.Tl.z - quad.Bl.z);
            return width > 0.25f && depth > 0.25f;
        }

        private FloorQuad ToAxisAlignedBox(FloorQuad q)
        {
            return ToInscribedAxisAlignedBox(q);
        }

        /// <summary>
        /// Axis-aligned box inside the projected trapezoid (intersection of near/far X spans).
        /// </summary>
        private FloorQuad ToInscribedAxisAlignedBox(FloorQuad q)
        {
            var botMinX = Mathf.Min(q.Bl.x, q.Br.x);
            var botMaxX = Mathf.Max(q.Bl.x, q.Br.x);
            var topMinX = Mathf.Min(q.Tl.x, q.Tr.x);
            var topMaxX = Mathf.Max(q.Tl.x, q.Tr.x);

            var minX = Mathf.Max(botMinX, topMinX);
            var maxX = Mathf.Min(botMaxX, topMaxX);
            if (maxX - minX < 0.2f)
            {
                minX = botMinX;
                maxX = botMaxX;
            }

            var minZ = Mathf.Min(q.Bl.z, q.Br.z);
            var maxZ = Mathf.Min(q.Tl.z, q.Tr.z);
            if (maxZ <= minZ + 0.15f)
                maxZ = Mathf.Max(q.Tl.z, q.Tr.z);

            return new FloorQuad
            {
                Bl = new Vector3(minX, floorY, minZ),
                Br = new Vector3(maxX, floorY, minZ),
                Tr = new Vector3(maxX, floorY, maxZ),
                Tl = new Vector3(minX, floorY, maxZ)
            };
        }

        private FloorQuad InsetAxisAligned(FloorQuad q, float inset)
        {
            var minX = Mathf.Min(q.Bl.x, q.Br.x) + inset;
            var maxX = Mathf.Max(q.Bl.x, q.Br.x) - inset;
            var minZ = Mathf.Min(q.Bl.z, q.Tl.z) + inset;
            var maxZ = Mathf.Max(q.Bl.z, q.Tl.z) - inset;
            if (maxX <= minX + 0.1f || maxZ <= minZ + 0.1f)
                return q;

            return new FloorQuad
            {
                Bl = new Vector3(minX, floorY, minZ),
                Br = new Vector3(maxX, floorY, minZ),
                Tr = new Vector3(maxX, floorY, maxZ),
                Tl = new Vector3(minX, floorY, maxZ)
            };
        }

        private Vector3 ViewportToFloor(Camera camera, float vx, float vy)
        {
            var ray = camera.ViewportPointToRay(new Vector3(vx, vy, 0f));
            var plane = new Plane(Vector3.up, new Vector3(0f, floorY, 0f));
            if (plane.Raycast(ray, out var enter))
                return ray.GetPoint(enter);

            var fallback = camera.ViewportToWorldPoint(new Vector3(vx, vy, Mathf.Abs(camera.transform.position.y - floorY)));
            fallback.y = floorY;
            return fallback;
        }

        private void CacheBounds(FloorQuad quad)
        {
            var inset = Mathf.Max(0.04f, wallThickness);
            var inner = InsetQuad(quad, inset);
            var outerMin = new Vector3(
                Mathf.Min(Mathf.Min(quad.Bl.x, quad.Br.x), Mathf.Min(quad.Tr.x, quad.Tl.x)),
                floorY,
                Mathf.Min(Mathf.Min(quad.Bl.z, quad.Br.z), Mathf.Min(quad.Tr.z, quad.Tl.z)));
            var outerMax = new Vector3(
                Mathf.Max(Mathf.Max(quad.Bl.x, quad.Br.x), Mathf.Max(quad.Tr.x, quad.Tl.x)),
                floorY + wallHeight,
                Mathf.Max(Mathf.Max(quad.Bl.z, quad.Br.z), Mathf.Max(quad.Tr.z, quad.Tl.z)));
            var innerMin = new Vector3(
                Mathf.Min(Mathf.Min(inner.Bl.x, inner.Br.x), Mathf.Min(inner.Tr.x, inner.Tl.x)),
                floorY,
                Mathf.Min(Mathf.Min(inner.Bl.z, inner.Br.z), Mathf.Min(inner.Tr.z, inner.Tl.z)));
            var innerMax = new Vector3(
                Mathf.Max(Mathf.Max(inner.Bl.x, inner.Br.x), Mathf.Max(inner.Tr.x, inner.Tl.x)),
                floorY + wallHeight,
                Mathf.Max(Mathf.Max(inner.Bl.z, inner.Br.z), Mathf.Max(inner.Tr.z, inner.Tl.z)));

            _outerBounds = new Bounds((outerMin + outerMax) * 0.5f, outerMax - outerMin);
            _innerBounds = new Bounds((innerMin + innerMax) * 0.5f, innerMax - innerMin);
            _hasBounds = true;
        }

        private void BuildGeometry(FloorQuad quad)
        {
            var center = quad.Center;
            var t = Mathf.Max(0.06f, wallThickness);
            var h = Mathf.Max(0.12f, wallHeight);
            var lip = Mathf.Max(0.02f, neonThickness);
            var basePad = t * 0.55f;

            // Soft white pit inset slightly inside the slate frame.
            var floor = InsetAxisAligned(quad, 0.004f);
            floor.Bl.y = floorY + 0.004f;
            floor.Br.y = floorY + 0.004f;
            floor.Tr.y = floorY + 0.004f;
            floor.Tl.y = floorY + 0.004f;

            ReplaceMesh(ref _floorMesh, BuildFloorMesh(floor, center), _floorFilter);
            // Walls + outer skirt = one solid slate frame with sharp corners.
            ReplaceMesh(ref _wallsMesh, BuildMinimalBoxWalls(quad, center, h, t, basePad), _wallsFilter);
            // Darker top lip — clear border reading from any camera angle.
            ReplaceMesh(ref _neonMesh, BuildBoxTopLip(quad, center, h, t, lip), _neonFilter);

            _floorFilter.transform.SetPositionAndRotation(center, Quaternion.identity);
            _wallsFilter.transform.SetPositionAndRotation(center, Quaternion.identity);
            _neonFilter.transform.SetPositionAndRotation(center, Quaternion.identity);
            _floorRenderer.sharedMaterial = _floorMat;
            _wallsRenderer.sharedMaterial = _wallMat;
            _neonRenderer.sharedMaterial = _neonMat;
            _floorRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _floorRenderer.receiveShadows = false;
            _wallsRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _wallsRenderer.receiveShadows = false;
            _neonRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _neonRenderer.receiveShadows = false;

            _neonFilter.gameObject.SetActive(true);
            _neonRenderer.enabled = true;
        }

        private static FloorQuad InsetQuad(FloorQuad quad, float inset)
        {
            var center = quad.Center;
            return new FloorQuad
            {
                Bl = InsetPoint(quad.Bl, center, inset),
                Br = InsetPoint(quad.Br, center, inset),
                Tr = InsetPoint(quad.Tr, center, inset),
                Tl = InsetPoint(quad.Tl, center, inset)
            };
        }

        private static Vector3 InsetPoint(Vector3 point, Vector3 center, float inset)
        {
            var delta = center - point;
            delta.y = 0f;
            if (delta.sqrMagnitude < 0.0001f)
                return point;
            return point + delta.normalized * inset;
        }

        private static Vector3 LocalOf(Vector3 world, Vector3 center)
        {
            return world - center;
        }

        private static Mesh BuildFloorMesh(FloorQuad quad, Vector3 center)
        {
            var mesh = new Mesh { name = "DiceTrayFloor" };
            // Keep Y so the pit sits slightly above the frame floor plane (no z-fight stripes).
            var bl = LocalOf(quad.Bl, center);
            var br = LocalOf(quad.Br, center);
            var tr = LocalOf(quad.Tr, center);
            var tl = LocalOf(quad.Tl, center);
            mesh.vertices = new[] { bl, br, tr, tl };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f)
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Minimalist rectangular crate: outer skirt + four AABB walls that meet at every corner.
        /// </summary>
        private Mesh BuildMinimalBoxWalls(FloorQuad quad, Vector3 center, float height, float thickness, float basePad)
        {
            var mesh = new Mesh { name = "DiceTrayBoxWalls" };
            var verts = new List<Vector3>(160);
            var tris = new List<int>(240);

            var minX = Mathf.Min(Mathf.Min(quad.Bl.x, quad.Br.x), Mathf.Min(quad.Tr.x, quad.Tl.x)) - center.x;
            var maxX = Mathf.Max(Mathf.Max(quad.Bl.x, quad.Br.x), Mathf.Max(quad.Tr.x, quad.Tl.x)) - center.x;
            var minZ = Mathf.Min(Mathf.Min(quad.Bl.z, quad.Br.z), Mathf.Min(quad.Tr.z, quad.Tl.z)) - center.z;
            var maxZ = Mathf.Max(Mathf.Max(quad.Bl.z, quad.Br.z), Mathf.Max(quad.Tr.z, quad.Tl.z)) - center.z;
            var t = thickness;
            var pad = Mathf.Max(0.02f, basePad);
            var skirtH = Mathf.Max(0.04f, height * 0.32f);
            var outerMinX = minX - t - pad;
            var outerMaxX = maxX + t + pad;
            var outerMinZ = minZ - t - pad;
            var outerMaxZ = maxZ + t + pad;

            // Outer plinth flange (hollow ring) — box sits on the table with a clear footprint.
            AppendBox(verts, tris, outerMinX, outerMaxX, -0.002f, skirtH, outerMinZ, minZ - t); // South flange
            AppendBox(verts, tris, outerMinX, outerMaxX, -0.002f, skirtH, maxZ + t, outerMaxZ); // North flange
            AppendBox(verts, tris, outerMinX, minX - t, -0.002f, skirtH, minZ - t, maxZ + t); // West flange
            AppendBox(verts, tris, maxX + t, outerMaxX, -0.002f, skirtH, minZ - t, maxZ + t); // East flange

            // Four walls meeting at corners (S/N full width, W/E between).
            AppendBox(verts, tris, minX - t, maxX + t, 0f, height, minZ - t, minZ); // South
            AppendBox(verts, tris, minX - t, maxX + t, 0f, height, maxZ, maxZ + t); // North
            AppendBox(verts, tris, minX - t, minX, 0f, height, minZ, maxZ); // West
            AppendBox(verts, tris, maxX, maxX + t, 0f, height, minZ, maxZ); // East

            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Continuous top cap on the frame — crisp border without neon glow.
        /// </summary>
        private Mesh BuildBoxTopLip(FloorQuad quad, Vector3 center, float height, float thickness, float lip)
        {
            var mesh = new Mesh { name = "DiceTrayBoxLip" };
            var verts = new List<Vector3>(96);
            var tris = new List<int>(144);

            var minX = Mathf.Min(Mathf.Min(quad.Bl.x, quad.Br.x), Mathf.Min(quad.Tr.x, quad.Tl.x)) - center.x;
            var maxX = Mathf.Max(Mathf.Max(quad.Bl.x, quad.Br.x), Mathf.Max(quad.Tr.x, quad.Tl.x)) - center.x;
            var minZ = Mathf.Min(Mathf.Min(quad.Bl.z, quad.Br.z), Mathf.Min(quad.Tr.z, quad.Tl.z)) - center.z;
            var maxZ = Mathf.Max(Mathf.Max(quad.Bl.z, quad.Br.z), Mathf.Max(quad.Tr.z, quad.Tl.z)) - center.z;
            var t = thickness;
            var y0 = height - Mathf.Max(0.012f, lip * 0.55f);
            var y1 = height + 0.006f;
            var overhang = Mathf.Max(0.008f, lip * 0.35f);

            AppendBox(verts, tris, minX - t - overhang, maxX + t + overhang, y0, y1, minZ - t - overhang, minZ + overhang); // South
            AppendBox(verts, tris, minX - t - overhang, maxX + t + overhang, y0, y1, maxZ - overhang, maxZ + t + overhang); // North
            AppendBox(verts, tris, minX - t - overhang, minX + overhang, y0, y1, minZ + overhang, maxZ - overhang); // West
            AppendBox(verts, tris, maxX - overhang, maxX + t + overhang, y0, y1, minZ + overhang, maxZ - overhang); // East

            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AppendBox(
            List<Vector3> verts,
            List<int> tris,
            float x0,
            float x1,
            float y0,
            float y1,
            float z0,
            float z1)
        {
            var blb = new Vector3(x0, y0, z0);
            var brb = new Vector3(x1, y0, z0);
            var trb = new Vector3(x1, y0, z1);
            var tlb = new Vector3(x0, y0, z1);
            var blt = new Vector3(x0, y1, z0);
            var brt = new Vector3(x1, y1, z0);
            var trt = new Vector3(x1, y1, z1);
            var tlt = new Vector3(x0, y1, z1);

            AddQuad(verts, tris, blt, brt, trt, tlt); // top
            AddQuad(verts, tris, blb, tlb, trb, brb); // bottom
            AddQuad(verts, tris, blb, brb, brt, blt); // south
            AddQuad(verts, tris, brb, trb, trt, brt); // east
            AddQuad(verts, tris, trb, tlb, tlt, trt); // north
            AddQuad(verts, tris, tlb, blb, blt, tlt); // west
        }

        private Mesh BuildNeonRimMesh(FloorQuad quad, Vector3 center, float height, float thickness, float bevel)
        {
            var mesh = new Mesh { name = "DiceTrayNeonRim" };
            var verts = new List<Vector3>(64);
            var tris = new List<int>(96);
            var inset = thickness * 0.35f + Mathf.Max(bevel * 0.35f, 0.01f);
            var rim = new FloorQuad
            {
                Bl = LocalOf(quad.Bl, center),
                Br = LocalOf(quad.Br, center),
                Tr = LocalOf(quad.Tr, center),
                Tl = LocalOf(quad.Tl, center)
            };
            var rimCenter = (rim.Bl + rim.Br + rim.Tr + rim.Tl) * 0.25f;
            rim.Bl = InsetPoint(rim.Bl, rimCenter, inset);
            rim.Br = InsetPoint(rim.Br, rimCenter, inset);
            rim.Tr = InsetPoint(rim.Tr, rimCenter, inset);
            rim.Tl = InsetPoint(rim.Tl, rimCenter, inset);

            var y = Mathf.Clamp01(neonHeightFactor) * height;
            var t = Mathf.Max(0.004f, neonThickness);
            AppendNeonEdge(verts, tris, rim.Bl, rim.Br, y, t);
            AppendNeonEdge(verts, tris, rim.Br, rim.Tr, y, t);
            AppendNeonEdge(verts, tris, rim.Tr, rim.Tl, y, t);
            AppendNeonEdge(verts, tris, rim.Tl, rim.Bl, y, t);

            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AppendNeonEdge(List<Vector3> verts, List<int> tris, Vector3 a, Vector3 b, float y, float thickness)
        {
            var along = b - a;
            along.y = 0f;
            if (along.sqrMagnitude < 0.0001f)
                return;

            along.Normalize();
            var side = Vector3.Cross(Vector3.up, along).normalized * (thickness * 0.5f);
            var h = Vector3.up * (thickness * 0.5f);
            var p0 = new Vector3(a.x, y, a.z) - side - h;
            var p1 = new Vector3(b.x, y, b.z) - side - h;
            var p2 = new Vector3(b.x, y, b.z) + side - h;
            var p3 = new Vector3(a.x, y, a.z) + side - h;
            var p4 = p0 + h * 2f;
            var p5 = p1 + h * 2f;
            var p6 = p2 + h * 2f;
            var p7 = p3 + h * 2f;

            AddQuad(verts, tris, p0, p1, p5, p4);
            AddQuad(verts, tris, p1, p2, p6, p5);
            AddQuad(verts, tris, p2, p3, p7, p6);
            AddQuad(verts, tris, p3, p0, p4, p7);
            AddQuad(verts, tris, p4, p5, p6, p7);
            AddQuad(verts, tris, p0, p3, p2, p1);
        }

        private static void AddQuad(List<Vector3> verts, List<int> tris, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            var i = verts.Count;
            verts.Add(a);
            verts.Add(b);
            verts.Add(c);
            verts.Add(d);
            tris.Add(i);
            tris.Add(i + 1);
            tris.Add(i + 2);
            tris.Add(i);
            tris.Add(i + 2);
            tris.Add(i + 3);
        }

        private void BuildColliders(FloorQuad quad)
        {
            var t = wallThickness;
            var center = quad.Center;
            var aabbMin = new Vector3(
                Mathf.Min(Mathf.Min(quad.Bl.x, quad.Br.x), Mathf.Min(quad.Tr.x, quad.Tl.x)),
                floorY,
                Mathf.Min(Mathf.Min(quad.Bl.z, quad.Br.z), Mathf.Min(quad.Tr.z, quad.Tl.z)));
            var aabbMax = new Vector3(
                Mathf.Max(Mathf.Max(quad.Bl.x, quad.Br.x), Mathf.Max(quad.Tr.x, quad.Tl.x)),
                floorY,
                Mathf.Max(Mathf.Max(quad.Bl.z, quad.Br.z), Mathf.Max(quad.Tr.z, quad.Tl.z)));

            ConfigureBox(
                _floorCollider,
                new Vector3(center.x, floorY + 0.01f, center.z),
                Quaternion.identity,
                new Vector3(aabbMax.x - aabbMin.x, 0.02f, aabbMax.z - aabbMin.z));

            ConfigureEdgeCollider(_wallColliders[0], quad.Bl, quad.Br, t);
            ConfigureEdgeCollider(_wallColliders[1], quad.Br, quad.Tr, t);
            ConfigureEdgeCollider(_wallColliders[2], quad.Tr, quad.Tl, t);
            ConfigureEdgeCollider(_wallColliders[3], quad.Tl, quad.Bl, t);

            var inner = InsetQuad(quad, t);
            var lidMin = new Vector3(
                Mathf.Min(Mathf.Min(inner.Bl.x, inner.Br.x), Mathf.Min(inner.Tr.x, inner.Tl.x)),
                floorY + lidHeight,
                Mathf.Min(Mathf.Min(inner.Bl.z, inner.Br.z), Mathf.Min(inner.Tr.z, inner.Tl.z)));
            var lidMax = new Vector3(
                Mathf.Max(Mathf.Max(inner.Bl.x, inner.Br.x), Mathf.Max(inner.Tr.x, inner.Tl.x)),
                floorY + lidHeight,
                Mathf.Max(Mathf.Max(inner.Bl.z, inner.Br.z), Mathf.Max(inner.Tr.z, inner.Tl.z)));
            ConfigureBox(
                _lidCollider,
                new Vector3((lidMin.x + lidMax.x) * 0.5f, floorY + lidHeight, (lidMin.z + lidMax.z) * 0.5f),
                Quaternion.identity,
                new Vector3(Mathf.Max(0.2f, lidMax.x - lidMin.x), 0.08f, Mathf.Max(0.2f, lidMax.z - lidMin.z)));
            _lidCollider.isTrigger = false;
            _lidCollider.gameObject.layer = 2; // Ignore Raycast — keep dice in, never steal clicks

            ApplyPhysicsMaterial(_floorCollider);
            for (var i = 0; i < _wallColliders.Length; i++)
                ApplyPhysicsMaterial(_wallColliders[i]);
            ApplyPhysicsMaterial(_lidCollider);
        }

        private void ConfigureEdgeCollider(BoxCollider box, Vector3 a, Vector3 b, float thickness)
        {
            if (box == null)
                return;

            var delta = b - a;
            delta.y = 0f;
            var length = delta.magnitude;
            if (length < 0.01f)
            {
                box.enabled = false;
                return;
            }

            box.enabled = true;
            var along = delta / length;
            var mid = (a + b) * 0.5f;
            mid.y = floorY + wallHeight * 0.5f;
            var rotation = Quaternion.LookRotation(along, Vector3.up);
            ConfigureBox(box, mid, rotation, new Vector3(thickness, wallHeight, length));
        }

        private static void ConfigureBox(BoxCollider box, Vector3 worldCenter, Quaternion worldRotation, Vector3 worldSize)
        {
            if (box == null)
                return;

            var host = box.transform;
            host.SetPositionAndRotation(worldCenter, worldRotation);
            var lossy = host.lossyScale;
            box.center = Vector3.zero;
            box.size = new Vector3(
                worldSize.x / Mathf.Max(0.0001f, Mathf.Abs(lossy.x)),
                worldSize.y / Mathf.Max(0.0001f, Mathf.Abs(lossy.y)),
                worldSize.z / Mathf.Max(0.0001f, Mathf.Abs(lossy.z)));
        }

        private void ApplyPhysicsMaterial(BoxCollider box)
        {
            if (box != null)
                box.sharedMaterial = _physicsMaterial;
        }

        private void PushDiceInside()
        {
            if (!_hasBounds)
                return;

            var pad = 0.08f;
            var min = _innerBounds.min + new Vector3(pad, 0f, pad);
            var max = _innerBounds.max - new Vector3(pad, 0f, pad);
            var bodies = FindObjectsByType<Rigidbody>(FindObjectsSortMode.None);
            for (var i = 0; i < bodies.Length; i++)
            {
                var body = bodies[i];
                if (body == null || !IsDieBody(body))
                    continue;

                var p = body.position;
                var clamped = new Vector3(
                    Mathf.Clamp(p.x, min.x, max.x),
                    Mathf.Max(p.y, floorY + 0.05f),
                    Mathf.Clamp(p.z, min.z, max.z));
                if ((clamped - p).sqrMagnitude < 0.0001f)
                    continue;

                body.position = Vector3.Lerp(p, clamped, 0.85f);
                body.linearVelocity *= 0.35f;
                body.angularVelocity *= 0.35f;
            }
        }

        private bool IsDieBody(Rigidbody body)
        {
            if (body.GetComponent<DieSelectable>() != null)
                return true;

            if (string.IsNullOrEmpty(dieTag))
                return false;

            // CompareTag throws if the tag was never registered in TagManager.
            try
            {
                return body.CompareTag(dieTag);
            }
            catch (UnityException)
            {
                return false;
            }
        }

        private void EnsureHierarchy()
        {
            _floorFilter = EnsureChildMesh("TrayFloor", out _floorRenderer);
            _wallsFilter = EnsureChildMesh("TrayWalls", out _wallsRenderer);
            _neonFilter = EnsureChildMesh("TrayNeonRim", out _neonRenderer);

            _floorCollider = EnsureColliderHost("TrayFloorCollider", ref _floorCollider);
            _wallColliders[0] = EnsureColliderHost("TrayWall_S", ref _wallColliders[0]);
            _wallColliders[1] = EnsureColliderHost("TrayWall_N", ref _wallColliders[1]);
            _wallColliders[2] = EnsureColliderHost("TrayWall_W", ref _wallColliders[2]);
            _wallColliders[3] = EnsureColliderHost("TrayWall_E", ref _wallColliders[3]);
            _lidCollider = EnsureColliderHost("TrayLid", ref _lidCollider);

            // Hide lid visually — collider only.
            var lidRenderer = _lidCollider.GetComponent<MeshRenderer>();
            if (lidRenderer != null)
                lidRenderer.enabled = false;
        }

        private MeshFilter EnsureChildMesh(string childName, out MeshRenderer renderer)
        {
            var child = transform.Find(childName);
            if (child == null)
            {
                var go = new GameObject(childName);
                go.transform.SetParent(transform, false);
                child = go.transform;
            }

            var filter = child.GetComponent<MeshFilter>();
            if (filter == null)
                filter = child.gameObject.AddComponent<MeshFilter>();
            renderer = child.GetComponent<MeshRenderer>();
            if (renderer == null)
                renderer = child.gameObject.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            return filter;
        }

        private BoxCollider EnsureColliderHost(string childName, ref BoxCollider cache)
        {
            if (cache != null)
                return cache;

            var child = transform.Find(childName);
            if (child == null)
            {
                var go = new GameObject(childName);
                go.transform.SetParent(transform, false);
                child = go.transform;
            }

            cache = child.GetComponent<BoxCollider>();
            if (cache == null)
                cache = child.gameObject.AddComponent<BoxCollider>();
            return cache;
        }

        private void EnsurePhysicsMaterial()
        {
            if (_physicsMaterial == null)
            {
                _physicsMaterial = new PhysicsMaterial("DiceTrayPhysics")
                {
                    bounceCombine = PhysicsMaterialCombine.Maximum,
                    frictionCombine = PhysicsMaterialCombine.Average
                };
            }

            _physicsMaterial.bounciness = bounciness;
            _physicsMaterial.dynamicFriction = dynamicFriction;
            _physicsMaterial.staticFriction = staticFriction;
        }

        private void EnsureMaterials()
        {
            var skin = BoardSkinConfig.Active;
            // Keep Classic Clean palette even if an older in-memory skin was cached.
            if (skin != null && skin.skinId == "classic-clean")
                skin.ApplyClassicCleanDefaults();

            if (_floorMat == null)
                _floorMat = CreateFlatMaterial("TrayFloorMat", skin.floorColor);
            else
                PaintFlat(_floorMat, skin.floorColor);

            if (_wallMat == null)
                _wallMat = CreateFlatMaterial("TrayWallMat", skin.wallColor);
            else
                PaintFlat(_wallMat, skin.wallColor);

            if (_neonMat == null)
                _neonMat = CreateFlatMaterial("TrayRimMat", skin.rimColor);
            else
                PaintFlat(_neonMat, skin.rimColor);
        }

        private static Material CreateFlatMaterial(string materialName, Color color)
        {
            var shader = ResolveFlatShader();
            var material = new Material(shader) { name = materialName };
            PaintFlat(material, color);
            return material;
        }

        private static void PaintFlat(Material material, Color color)
        {
            if (material == null)
                return;

            color.a = 1f;
            material.color = color;
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
            // Kill specular banding if a Lit shader was resolved as fallback.
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", 0.02f);
            if (material.HasProperty("_Glossiness"))
                material.SetFloat("_Glossiness", 0.02f);
            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", 0f);
            material.DisableKeyword("_EMISSION");
            if (material.HasProperty("_EmissionColor"))
                material.SetColor("_EmissionColor", Color.black);
        }

        private static Shader ResolveFlatShader()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline != null)
            {
                var unlit = Shader.Find("Universal Render Pipeline/Unlit");
                if (unlit != null)
                    return unlit;
                var simple = Shader.Find("Universal Render Pipeline/Simple Lit");
                if (simple != null)
                    return simple;
            }

            var unlitBuiltin = Shader.Find("Unlit/Color");
            if (unlitBuiltin != null)
                return unlitBuiltin;
            return ResolveLitShader();
        }

        private static Color Hex(int rgb)
        {
            var r = ((rgb >> 16) & 255) / 255f;
            var g = ((rgb >> 8) & 255) / 255f;
            var b = (rgb & 255) / 255f;
            return new Color(r, g, b, 1f);
        }

        private static Material CreateLitMaterial(
            string materialName,
            Color color,
            float smoothness,
            float metallic,
            bool emission,
            Color emissionColor,
            float emissionIntensity)
        {
            var shader = ResolveLitShader();
            var material = new Material(shader) { name = materialName };
            PaintMaterial(material, color, smoothness, metallic, emission, emissionColor, emissionIntensity);
            return material;
        }

        private static void PaintMaterial(
            Material material,
            Color color,
            float smoothness,
            float metallic,
            bool emission,
            Color emissionColor,
            float emissionIntensity)
        {
            if (material == null)
                return;

            color.a = 1f;
            material.color = color;
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", smoothness);
            if (material.HasProperty("_Glossiness"))
                material.SetFloat("_Glossiness", smoothness);
            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", metallic);

            if (emission)
            {
                material.EnableKeyword("_EMISSION");
                if (material.HasProperty("_EmissionColor"))
                    material.SetColor("_EmissionColor", emissionColor * emissionIntensity);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                material.DisableKeyword("_EMISSION");
                if (material.HasProperty("_EmissionColor"))
                    material.SetColor("_EmissionColor", Color.black);
            }
        }

        private static Shader ResolveLitShader()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline != null)
            {
                var urp = Shader.Find("Universal Render Pipeline/Lit");
                if (urp != null)
                    return urp;
                var simple = Shader.Find("Universal Render Pipeline/Simple Lit");
                if (simple != null)
                    return simple;
            }

            var standard = Shader.Find("Standard");
            if (standard != null)
                return standard;
            return Shader.Find("Sprites/Default");
        }

        private void ReplaceMesh(ref Mesh slot, Mesh next, MeshFilter filter)
        {
            if (filter != null)
                filter.sharedMesh = next;

            if (slot != null && slot != next)
                DestroyMesh(slot);

            slot = next;
        }

        private void ReleaseMeshes()
        {
            DestroyMesh(_floorMesh);
            DestroyMesh(_wallsMesh);
            DestroyMesh(_neonMesh);
            _floorMesh = null;
            _wallsMesh = null;
            _neonMesh = null;
        }

        private void ReleaseMaterials()
        {
            DestroyAsset(_floorMat);
            DestroyAsset(_wallMat);
            DestroyAsset(_neonMat);
            _floorMat = null;
            _wallMat = null;
            _neonMat = null;

            if (_physicsMaterial != null)
            {
                DestroyAsset(_physicsMaterial);
                _physicsMaterial = null;
            }
        }

        private static void DestroyMesh(Mesh mesh)
        {
            if (mesh == null)
                return;
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                DestroyImmediate(mesh);
                return;
            }
#endif
            Destroy(mesh);
        }

        private static void DestroyAsset(Object asset)
        {
            if (asset == null)
                return;
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                DestroyImmediate(asset);
                return;
            }
#endif
            Destroy(asset);
        }

        private void OnDrawGizmos()
        {
            if (!drawGizmos || !_hasBounds)
                return;

            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.35f);
            Gizmos.DrawWireCube(_outerBounds.center, _outerBounds.size);
            Gizmos.color = new Color(0.4f, 1f, 0.5f, 0.55f);
            Gizmos.DrawWireCube(_innerBounds.center, new Vector3(_innerBounds.size.x, 0.02f, _innerBounds.size.z));
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!isActiveAndEnabled)
                return;

            EditorApplication.delayCall += () =>
            {
                if (this == null)
                    return;
                Rebuild();
            };
        }
#endif
    }
}
