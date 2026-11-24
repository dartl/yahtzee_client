using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Yahtzee.Core
{
    /// <summary>
    /// Studio lighting for Classic Clean (soft shadows, bright ambient, no neon Bloom).
    /// Kept type name for existing call sites (UIManager).
    /// </summary>
    public static class NeonVisualBootstrap
    {
        private const string VolumeName = "YahtzeeGlobalVolume";
        private const string FillLightName = "YahtzeeFillLight";
        private const string RimLightName = "YahtzeeRimLight";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBootstrap()
        {
            Ensure();
        }

        public static void Ensure()
        {
            var board = BoardSkinConfig.Active;
            ConfigureCamera(board);
            ConfigureKeyLight(board);
            EnsureFillLights(board);
            ConfigurePostProcessing(board);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = board.ambientLight;
            RenderSettings.fog = false;
        }

        private static void ConfigureCamera(BoardSkinConfig board)
        {
            var camera = Camera.main;
            if (camera == null)
                return;

            camera.backgroundColor = board.cameraBackground;
            camera.clearFlags = CameraClearFlags.SolidColor;
            var data = camera.GetComponent<UniversalAdditionalCameraData>();
            if (data != null)
                data.renderPostProcessing = true;
        }

        private static void ConfigureKeyLight(BoardSkinConfig board)
        {
            var light = Object.FindAnyObjectByType<Light>();
            if (light == null || light.type != LightType.Directional)
            {
                var go = GameObject.Find("Directional Light");
                if (go != null)
                    light = go.GetComponent<Light>();
            }

            if (light == null)
                return;

            light.color = board.keyLightColor;
            light.intensity = board.keyLightIntensity;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.55f;
            light.transform.rotation = Quaternion.Euler(48f, -28f, 0f);
        }

        private static void EnsureFillLights(BoardSkinConfig board)
        {
            EnsurePoint(FillLightName, new Vector3(0.8f, 2.6f, -1.0f), board.fillLightColor, board.fillLightIntensity, 10f);
            // Soft cool bounce — not neon rim.
            EnsurePoint(RimLightName, new Vector3(-1.4f, 2.0f, 1.0f), new Color(0.92f, 0.94f, 1f, 1f), 0.22f, 9f);
        }

        private static void EnsurePoint(string name, Vector3 pos, Color color, float intensity, float range)
        {
            var existing = GameObject.Find(name);
            GameObject go;
            if (existing == null)
            {
                go = new GameObject(name);
                go.AddComponent<Light>();
            }
            else
            {
                go = existing;
            }

            go.transform.position = pos;
            var light = go.GetComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
        }

        private static void ConfigurePostProcessing(BoardSkinConfig board)
        {
            var volumeGo = GameObject.Find(VolumeName);
            if (volumeGo == null)
            {
                volumeGo = new GameObject(VolumeName);
                var volume = volumeGo.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.priority = 1f;
                var profile = ScriptableObject.CreateInstance<VolumeProfile>();
                profile.name = "YahtzeeCleanProfile";
                volume.profile = profile;
                if (!profile.TryGet(out Bloom bloom))
                    bloom = profile.Add<Bloom>(true);
                bloom.active = board.enableBloom;
                bloom.threshold.Override(1.2f);
                bloom.intensity.Override(board.enableBloom ? 0.15f : 0f);
            }
            else
            {
                var volume = volumeGo.GetComponent<Volume>();
                if (volume != null && volume.profile != null && volume.profile.TryGet(out Bloom bloom))
                {
                    bloom.active = board.enableBloom;
                    bloom.intensity.Override(board.enableBloom ? 0.15f : 0f);
                    bloom.threshold.Override(1.2f);
                }
            }
        }
    }
}
