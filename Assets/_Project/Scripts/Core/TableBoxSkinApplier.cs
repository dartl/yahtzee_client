using UnityEngine;

namespace Yahtzee.Core
{
    [DisallowMultipleComponent]
    public sealed class TableBoxSkinApplier : MonoBehaviour
    {
        public const string PrefKey = "yahtzee.skin.table";

        private Material _felt;
        private Material _rails;
        private string _appliedSlug;
        private Transform[] _corners;

        public static void ApplySaved(GameObject board)
        {
            if (board == null)
            {
                var found = GameObject.Find("Table");
                board = found;
            }

            if (board == null)
                return;

            var applier = board.GetComponent<TableBoxSkinApplier>();
            if (applier == null)
                applier = board.AddComponent<TableBoxSkinApplier>();

            applier.Apply(PlayerPrefs.GetString(PrefKey, SkinVisuals.ClassicTable));
        }

        public void Apply(string slug)
        {
            if (string.IsNullOrWhiteSpace(slug))
                slug = SkinVisuals.ClassicTable;
            var look = SkinVisuals.Table(slug);
            var board = BoardSkinConfig.Active;
            EnsureMaterials();
            Paint(_felt, look.Felt, board.floorSmoothness, 0f);
            Paint(_rails, look.Rails, board.wallSmoothness, 0.04f);
            ApplyToRenderers();
            EnsureCorners(look.GoldCorners);
            _appliedSlug = slug;
        }

        private void EnsureMaterials()
        {
            if (_felt == null)
                _felt = CreateLit("TableFelt");
            if (_rails == null)
                _rails = CreateLit("TableRails");
        }

        private void ApplyToRenderers()
        {
            var renderers = GetComponentsInChildren<MeshRenderer>(true);
            for (var index = 0; index < renderers.Length; index++)
            {
                var renderer = renderers[index];
                if (renderer == null)
                    continue;

                var objectName = renderer.gameObject.name;
                if (objectName.StartsWith("Corner_"))
                    continue;

                // ProceduralDiceTray owns TrayFloor / TrayWalls / TrayNeonRim — never overwrite.
                if (objectName.StartsWith("Tray"))
                    continue;

                if (objectName.StartsWith("Border_") || objectName.StartsWith("Keep"))
                    continue;

                if (renderer.transform == transform)
                    renderer.sharedMaterial = _felt;
            }
        }

        private void EnsureCorners(bool visible)
        {
            if (_corners == null || _corners.Length != 4)
                _corners = CreateCorners();

            var meshFilter = GetComponent<MeshFilter>();
            var bounds = meshFilter != null && meshFilter.sharedMesh != null
                ? meshFilter.sharedMesh.bounds
                : new Bounds(Vector3.zero, Vector3.one);
            var inset = Mathf.Min(bounds.extents.x, bounds.extents.z) * 0.12f;
            var y = bounds.max.y + 0.02f;
            var points = new[]
            {
                new Vector3(bounds.min.x + inset, y, bounds.min.z + inset),
                new Vector3(bounds.max.x - inset, y, bounds.min.z + inset),
                new Vector3(bounds.min.x + inset, y, bounds.max.z - inset),
                new Vector3(bounds.max.x - inset, y, bounds.max.z - inset)
            };

            for (var index = 0; index < _corners.Length; index++)
            {
                var corner = _corners[index];
                if (corner == null)
                    continue;

                corner.localPosition = points[index];
                corner.gameObject.SetActive(visible);
            }
        }

        private Transform[] CreateCorners()
        {
            var corners = new Transform[4];
            var gold = CreateLit("CornerGold");
            Paint(gold, new Color(0.86f, 0.72f, 0.34f, 1f), 0.55f, 0.85f);
            for (var index = 0; index < corners.Length; index++)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = "Corner_" + index.ToString();
                var collider = cube.GetComponent<Collider>();
                if (collider != null)
                    Destroy(collider);

                cube.transform.SetParent(transform, false);
                cube.transform.localScale = new Vector3(0.12f, 0.04f, 0.12f);
                var renderer = cube.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = gold;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                corners[index] = cube.transform;
            }

            return corners;
        }

        private static Material CreateLit(string materialName)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");

            var material = new Material(shader);
            material.name = materialName;
            return material;
        }

        private static void Paint(Material material, Color color, float smoothness, float metallic)
        {
            if (material == null)
                return;

            material.color = color;
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", smoothness);
            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", metallic);
        }
    }
}
