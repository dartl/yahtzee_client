#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using Yahtzee.Core;

namespace Yahtzee.Editor
{
    public static class StorePackagingSetup
    {
        private const string IconPath = "Assets/_Project/Art/AppIcon.png";
        private const string SplashPath = "Assets/_Project/Art/Splash.png";

        [MenuItem("YADZY/Apply Store Packaging")]
        private static void Apply()
        {
            PlayerSettings.companyName = "Gawk";
            PlayerSettings.productName = "YADZY";
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Standalone, "com.gawk.yadzy");
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.gawk.yadzy");
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.iOS, "com.gawk.yadzy");

            if (File.Exists(IconPath))
            {
                var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
                if (icon != null)
                {
#pragma warning disable CS0618
                    PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Unknown, new[] { icon });
                    PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Android, new[] { icon });
                    PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.iOS, new[] { icon });
#pragma warning restore CS0618
                }
            }

            if (File.Exists(SplashPath))
            {
                var splash = AssetDatabase.LoadAssetAtPath<Texture2D>(SplashPath);
                if (splash != null)
                    PlayerSettings.virtualRealitySplashScreen = splash;
            }

            PlayerSettings.SplashScreen.show = true;
            PlayerSettings.SplashScreen.showUnityLogo = false;
            PlayerSettings.SplashScreen.backgroundColor = new Color(0.04f, 0.1f, 0.08f, 1f);
            AssetDatabase.SaveAssets();

            var config = Resources.Load<YahtzeeConfig>("YahtzeeConfig");
            var privacy = config != null ? config.PrivacyPolicyUrl : "(set YahtzeeConfig)";
            Debug.Log("YADZY store packaging applied. Privacy URL (current env): " + privacy
                + " | Production privacy: https://yadzy.app/privacy.html"
                + " | Open YADZY/Smoke Checklist before submit.");
        }

        [MenuItem("YADZY/Use Production Endpoints In Editor")]
        private static void EnableProductionInEditor()
        {
            SetProductionInEditor(true);
        }

        [MenuItem("YADZY/Use Development Endpoints In Editor")]
        private static void EnableDevelopmentInEditor()
        {
            SetProductionInEditor(false);
        }

        private static void SetProductionInEditor(bool production)
        {
            var guids = AssetDatabase.FindAssets("t:YahtzeeConfig");
            if (guids.Length == 0)
            {
                Debug.LogError("YahtzeeConfig asset not found.");
                return;
            }

            var path = AssetDatabase.GUIDToAssetPath(guids[0]);
            var config = AssetDatabase.LoadAssetAtPath<YahtzeeConfig>(path);
            if (config == null)
                return;

            var so = new SerializedObject(config);
            var prop = so.FindProperty("useProductionInEditor");
            if (prop == null)
            {
                Debug.LogError("useProductionInEditor missing on YahtzeeConfig.");
                return;
            }

            prop.boolValue = production;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            Debug.Log(production
                ? "Editor now uses production API/Reverb/privacy URLs."
                : "Editor now uses development (yahtzee.local) endpoints.");
        }
    }
}
#endif
