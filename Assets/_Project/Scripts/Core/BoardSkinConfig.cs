using UnityEngine;

namespace Yahtzee.Core
{
    /// <summary>
    /// 3D board / tray / dice look for the shop-ready skin pipeline.
    /// Classic Clean is the default runtime skin.
    /// </summary>
    [CreateAssetMenu(fileName = "BoardSkinConfig", menuName = "Yahtzee/Board Skin Config")]
    public sealed class BoardSkinConfig : ScriptableObject
    {
        public const string ClassicCleanResource = "Themes/ClassicCleanBoard";

        [Header("Identity")]
        public string skinId = "classic-clean";
        public string skinName = "Classic Clean";
        public string diceSlug = SkinVisuals.ClassicDice;
        public string tableSlug = SkinVisuals.ClassicTable;

        [Header("Tray")]
        // Minimalist box: soft white pit + slate frame (matches Classic Clean panels).
        public Color floorColor = new Color(0.973f, 0.980f, 0.988f, 1f);     // #F8FAFC
        public Color wallColor = new Color(0.204f, 0.251f, 0.329f, 1f);      // #334155
        public Color rimColor = new Color(0.118f, 0.161f, 0.231f, 1f);       // #1E293B
        [Range(0f, 1f)] public float floorSmoothness = 0.08f;
        [Range(0f, 1f)] public float wallSmoothness = 0.12f;
        [Range(0f, 1f)] public float rimSmoothness = 0.12f;
        public bool rimEmission;

        [Header("Table surround")]
        public Color tableFelt = new Color(0.886f, 0.910f, 0.941f, 1f);      // #E2E8F0
        public Color tableRails = new Color(0.118f, 0.161f, 0.231f, 1f);
        public bool goldCorners;

        [Header("Dice")]
        public Color diceBody = new Color(0.980f, 0.980f, 0.980f, 1f);
        public Color dicePips = new Color(0.118f, 0.137f, 0.165f, 1f);
        public Color dicePipAce = new Color(0.86f, 0.22f, 0.22f, 1f);
        [Range(0f, 1f)] public float diceSmoothness = 0.55f;
        public bool redAcePip = true;

        [Header("Lighting")]
        public Color cameraBackground = new Color(0.886f, 0.910f, 0.941f, 1f);
        public Color ambientLight = new Color(0.78f, 0.82f, 0.88f, 1f);
        public Color keyLightColor = new Color(1f, 0.98f, 0.95f, 1f);
        public float keyLightIntensity = 1.15f;
        public Color fillLightColor = new Color(0.85f, 0.90f, 0.98f, 1f);
        public float fillLightIntensity = 0.35f;
        public bool enableBloom;

        private static BoardSkinConfig _active;
        private static bool _resourcesResolved;

        public static BoardSkinConfig Active
        {
            get
            {
                if (_active != null)
                    return _active;

                // Never Resources.Load here — MonoBehaviour field initializers forbid it.
                _active = CreateInstance<BoardSkinConfig>();
                _active.name = "ClassicCleanBoard (Runtime)";
                _active.ApplyClassicCleanDefaults();
                return _active;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void BootstrapFromResources()
        {
            ResolveFromResources();
        }

        public static void ResolveFromResources()
        {
            if (_resourcesResolved)
                return;

            _resourcesResolved = true;
            var loaded = Resources.Load<BoardSkinConfig>(ClassicCleanResource);
            if (loaded != null)
            {
                _active = loaded;
                // Keep resource asset in sync with the canonical Classic Clean palette.
                if (_active.skinId == "classic-clean")
                    _active.ApplyClassicCleanDefaults();
            }
            else if (_active == null)
            {
                _active = CreateInstance<BoardSkinConfig>();
                _active.name = "ClassicCleanBoard (Runtime)";
                _active.ApplyClassicCleanDefaults();
            }
        }

        public static void SetActive(BoardSkinConfig skin)
        {
            if (skin != null)
            {
                _active = skin;
                _resourcesResolved = true;
            }
        }

        public void ApplyClassicCleanDefaults()
        {
            skinId = "classic-clean";
            skinName = "Classic Clean";
            diceSlug = SkinVisuals.ClassicDice;
            tableSlug = SkinVisuals.ClassicTable;
            floorColor = Hex(0xF8FAFC);
            wallColor = Hex(0x334155);
            rimColor = Hex(0x1E293B);
            floorSmoothness = 0.08f;
            wallSmoothness = 0.12f;
            rimSmoothness = 0.12f;
            rimEmission = false;
            tableFelt = Hex(0xE2E8F0);
            tableRails = Hex(0x1E293B);
            goldCorners = false;
            diceBody = Hex(0xFAFAFA);
            dicePips = Hex(0x1E232A);
            dicePipAce = new Color(0.86f, 0.22f, 0.22f, 1f);
            diceSmoothness = 0.55f;
            redAcePip = true;
            cameraBackground = Hex(0xE2E8F0);
            ambientLight = new Color(0.72f, 0.76f, 0.84f, 1f);
            keyLightColor = new Color(1f, 0.98f, 0.95f, 1f);
            keyLightIntensity = 1.2f;
            fillLightColor = new Color(0.85f, 0.90f, 0.98f, 1f);
            fillLightIntensity = 0.4f;
            enableBloom = false;
        }

        public DiceLook ToDiceLook() => new DiceLook(diceBody, dicePips);

        public TableLook ToTableLook() => new TableLook(tableFelt, tableRails, goldCorners);

        private static Color Hex(int rgb)
        {
            return new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);
        }
    }
}
