using UnityEngine;
using UnityEngine.Rendering;

namespace Yahtzee.Core
{
    /// <summary>
    /// Classic Clean ivory die + flat dark pips (optional red ace). No emission glow.
    /// Does not alter transform scale — DicePhysicsController owns world/held scale.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProceduralDiceVisual : MonoBehaviour
    {
        private const float PipDiameter = 0.16f;
        private const float PipHeight = 0.006f;
        private const float PipLift = 0.502f;

        private static Material _bodyMat;
        private static Material _pipMat;
        private static Material _acePipMat;
        private static Mesh _cubeMesh;
        private bool _pipsBuilt;

        public static void EnsureOn(GameObject die)
        {
            if (die == null)
                return;

            var visual = die.GetComponent<ProceduralDiceVisual>();
            if (visual == null)
                visual = die.AddComponent<ProceduralDiceVisual>();
            visual.Apply();
        }

        public void Apply()
        {
            EnsureMaterials();
            EnsureCubeMesh();

            var filter = GetComponent<MeshFilter>();
            if (filter == null)
                filter = gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = _cubeMesh;

            var renderer = GetComponent<MeshRenderer>();
            if (renderer == null)
                renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _bodyMat;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;

            StripLegacyFaces();

            if (!_pipsBuilt)
            {
                ClearOwnedPips();
                BuildPips();
                _pipsBuilt = true;
            }
        }

        public static void SetColors(Color body, Color pip)
        {
            EnsureMaterials();
            body.a = 1f;
            pip.a = 1f;
            var smooth = BoardSkinConfig.Active.diceSmoothness;
            PaintOpaque(_bodyMat, body, smooth, 0.04f, false, Color.black, 0f);
            PaintOpaque(_pipMat, pip, 0.35f, 0.02f, false, Color.black, 0f);
            PaintOpaque(_acePipMat, BoardSkinConfig.Active.dicePipAce, 0.4f, 0.02f, false, Color.black, 0f);
        }

        private void StripLegacyFaces()
        {
            for (var i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (child.name.StartsWith("Face_"))
                    DestroySafe(child.gameObject);
            }
        }

        private void ClearOwnedPips()
        {
            for (var i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (child.name.StartsWith("Pip_"))
                    DestroySafe(child.gameObject);
            }
        }

        private void BuildPips()
        {
            PlaceFacePips(1, Vector3.up, FaceOffsets(1));
            PlaceFacePips(6, Vector3.down, FaceOffsets(6));
            PlaceFacePips(2, Vector3.forward, FaceOffsets(2));
            PlaceFacePips(5, Vector3.back, FaceOffsets(5));
            PlaceFacePips(3, Vector3.right, FaceOffsets(3));
            PlaceFacePips(4, Vector3.left, FaceOffsets(4));
        }

        private void PlaceFacePips(int face, Vector3 normal, Vector2[] offsets)
        {
            var n = normal.normalized;
            var faceCenter = n * PipLift;
            var useAce = face == 1 && BoardSkinConfig.Active.redAcePip;
            for (var i = 0; i < offsets.Length; i++)
            {
                var tangential = Tangents(n);
                var local = faceCenter
                    + tangential.right * offsets[i].x
                    + tangential.up * offsets[i].y;

                var pip = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pip.name = "Pip_" + face + "_" + i;
                pip.transform.SetParent(transform, false);
                pip.transform.localPosition = local;
                pip.transform.localRotation = Quaternion.FromToRotation(Vector3.up, n);
                pip.transform.localScale = new Vector3(PipDiameter, PipHeight * 0.5f, PipDiameter);

                var col = pip.GetComponent<Collider>();
                if (col != null)
                    DestroySafe(col);

                var rend = pip.GetComponent<MeshRenderer>();
                if (rend == null)
                    continue;

                rend.sharedMaterial = useAce ? _acePipMat : _pipMat;
                rend.shadowCastingMode = ShadowCastingMode.Off;
                rend.receiveShadows = false;
            }
        }

        private static (Vector3 right, Vector3 up) Tangents(Vector3 normal)
        {
            var n = normal.normalized;
            var up = Mathf.Abs(Vector3.Dot(n, Vector3.up)) > 0.9f ? Vector3.forward : Vector3.up;
            var right = Vector3.Cross(up, n).normalized;
            up = Vector3.Cross(n, right).normalized;
            return (right, up);
        }

        private static Vector2[] FaceOffsets(int face)
        {
            const float a = 0.22f;
            switch (face)
            {
                case 1: return new[] { Vector2.zero };
                case 2: return new[] { new Vector2(-a, a), new Vector2(a, -a) };
                case 3: return new[] { new Vector2(-a, a), Vector2.zero, new Vector2(a, -a) };
                case 4: return new[] { new Vector2(-a, a), new Vector2(a, a), new Vector2(-a, -a), new Vector2(a, -a) };
                case 5: return new[] { new Vector2(-a, a), new Vector2(a, a), Vector2.zero, new Vector2(-a, -a), new Vector2(a, -a) };
                default:
                    return new[]
                    {
                        new Vector2(-a, a), new Vector2(a, a),
                        new Vector2(-a, 0f), new Vector2(a, 0f),
                        new Vector2(-a, -a), new Vector2(a, -a)
                    };
            }
        }

        private static void EnsureMaterials()
        {
            var board = BoardSkinConfig.Active;
            if (_bodyMat == null)
                _bodyMat = CreateLit("DieBodyMat", board.diceBody, board.diceSmoothness, 0.04f, false, Color.black, 0f);
            else
                PaintOpaque(_bodyMat, board.diceBody, board.diceSmoothness, 0.04f, false, Color.black, 0f);

            if (_pipMat == null)
                _pipMat = CreateLit("DiePipMat", board.dicePips, 0.35f, 0.02f, false, Color.black, 0f);
            else
                PaintOpaque(_pipMat, board.dicePips, 0.35f, 0.02f, false, Color.black, 0f);

            if (_acePipMat == null)
                _acePipMat = CreateLit("DieAcePipMat", board.dicePipAce, 0.4f, 0.02f, false, Color.black, 0f);
            else
                PaintOpaque(_acePipMat, board.dicePipAce, 0.4f, 0.02f, false, Color.black, 0f);
        }

        private static void EnsureCubeMesh()
        {
            if (_cubeMesh != null)
                return;

            var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var filter = temp.GetComponent<MeshFilter>();
            _cubeMesh = UnityEngine.Object.Instantiate(filter.sharedMesh);
            _cubeMesh.name = "DieCubeMesh";
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(temp);
            else
                UnityEngine.Object.DestroyImmediate(temp);
        }

        private static Material CreateLit(
            string name,
            Color color,
            float smoothness,
            float metallic,
            bool emission,
            Color emissionColor,
            float emissionIntensity)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");

            var mat = new Material(shader) { name = name };
            PaintOpaque(mat, color, smoothness, metallic, emission, emissionColor, emissionIntensity);
            return mat;
        }

        private static void PaintOpaque(
            Material mat,
            Color color,
            float smoothness,
            float metallic,
            bool emission,
            Color emissionColor,
            float emissionIntensity)
        {
            if (mat == null)
                return;

            color.a = 1f;
            mat.color = color;
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", color);
            if (mat.HasProperty("_Smoothness"))
                mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Glossiness"))
                mat.SetFloat("_Glossiness", smoothness);
            if (mat.HasProperty("_Metallic"))
                mat.SetFloat("_Metallic", metallic);

            if (mat.HasProperty("_Surface"))
                mat.SetFloat("_Surface", 0f);
            mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetOverrideTag("RenderType", "Opaque");
            mat.renderQueue = (int)RenderQueue.Geometry;

            if (!emission)
            {
                mat.DisableKeyword("_EMISSION");
                if (mat.HasProperty("_EmissionColor"))
                    mat.SetColor("_EmissionColor", Color.black);
                return;
            }

            mat.EnableKeyword("_EMISSION");
            if (mat.HasProperty("_EmissionColor"))
                mat.SetColor("_EmissionColor", emissionColor * emissionIntensity);
        }

        private static void DestroySafe(UnityEngine.Object obj)
        {
            if (obj == null)
                return;
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(obj);
            else
                UnityEngine.Object.DestroyImmediate(obj);
        }
    }
}
