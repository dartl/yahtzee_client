using TMPro;
using UnityEngine;

namespace Yahtzee.Core
{
    /// <summary>
    /// Flat / Material UI theme with tactile button roles. Classic Clean is the default.
    /// Static color accessors keep existing call sites working.
    /// </summary>
    [CreateAssetMenu(fileName = "UIThemeConfig", menuName = "Yahtzee/UI Theme Config")]
    public sealed class UIThemeConfig : ScriptableObject
    {
        public const string ClassicCleanResource = "Themes/ClassicCleanUI";
        public const string InterFontResource = "fonts/Inter-VariableFont_opsz,wght SDF";

        [Header("Identity")]
        public string themeId = "classic-clean";
        public string themeName = "Classic Clean";

        [Header("Surfaces")]
        public Color backdrop = new Color(0.886f, 0.910f, 0.941f, 1f);      // #E2E8F0
        public Color panelBg = new Color(1f, 1f, 1f, 0.98f);                 // #FFFFFF
        public Color panelBgSolid = new Color(1f, 1f, 1f, 1f);
        public Color cardBg = new Color(1f, 1f, 1f, 1f);                      // white cards on cool backdrop
        public Color borderSubtle = new Color(0.580f, 0.639f, 0.722f, 1f);   // #94A3B8
        public Color borderStrong = new Color(0.118f, 0.161f, 0.231f, 1f);   // #1E293B

        [Header("Typography")]
        public Color textPrimary = new Color(0.059f, 0.090f, 0.165f, 1f);    // #0F172A
        public Color textMuted = new Color(0.392f, 0.455f, 0.545f, 1f);      // #64748B
        public Color textAccent = new Color(0.145f, 0.388f, 0.922f, 1f);     // #2563EB
        public Color textOnAccent = Color.white;

        [Header("Primary button (tactile)")]
        public Color buttonAccent = new Color(0.145f, 0.388f, 0.922f, 1f);   // #2563EB
        public Color buttonAccentDepth = new Color(0.114f, 0.306f, 0.847f, 1f); // #1D4ED8
        public Color buttonFill = new Color(1f, 1f, 1f, 1f);                 // secondary face
        public Color buttonAccentGlow = new Color(0.145f, 0.388f, 0.922f, 0.18f);
        [Range(3f, 8f)] public float buttonDepthPx = 5f;

        [Header("Secondary button")]
        public Color buttonSecondaryFace = Color.white;
        public Color buttonSecondaryBorder = new Color(0.580f, 0.639f, 0.722f, 1f); // #94A3B8
        public Color buttonSecondaryText = new Color(0.059f, 0.090f, 0.165f, 1f);   // #0F172A

        [Header("Destructive button")]
        public Color buttonDangerFace = new Color(0.996f, 0.886f, 0.886f, 1f); // #FEE2E2
        public Color buttonDangerBorder = new Color(0.863f, 0.149f, 0.149f, 1f); // #DC2626
        public Color buttonDangerText = new Color(0.863f, 0.149f, 0.149f, 1f);

        [Header("Score sheet")]
        public Color rowA = new Color(1f, 1f, 1f, 1f);
        public Color rowB = new Color(0.973f, 0.980f, 0.988f, 1f);           // #F8FAFC
        public Color rowHint = new Color(0.859f, 0.914f, 0.988f, 1f);        // #DBEAFE
        public Color rowHintBorder = new Color(0.145f, 0.388f, 0.922f, 1f);  // #2563EB
        public Color rowCommitted = new Color(0.941f, 0.953f, 0.965f, 0.95f);
        public Color rowSelected = new Color(0.820f, 0.898f, 1f, 1f);
        public Color scoreCommitted = new Color(0.059f, 0.090f, 0.165f, 1f);
        public Color scoreHint = new Color(0.145f, 0.388f, 0.922f, 1f);
        public Color sectionHeaderBg = new Color(0.118f, 0.161f, 0.231f, 1f); // #1E293B
        public Color sectionHeaderText = Color.white;
        public Color sectionAltBg = new Color(0.886f, 0.910f, 0.941f, 1f);   // #E2E8F0
        public Color sectionAltText = new Color(0.059f, 0.090f, 0.165f, 1f);

        [Header("Dice defaults (UI preview / fallback)")]
        public Color diceBody = new Color(0.980f, 0.980f, 0.980f, 1f);
        public Color dicePip = new Color(0.118f, 0.137f, 0.165f, 1f);
        public Color dicePipAce = new Color(0.86f, 0.22f, 0.22f, 1f);

        public Color Neon => textAccent;
        public Color NeonCyan => textAccent;
        public Color NeonRim => borderSubtle;
        public Color NeonDim => borderSubtle;

        private static UIThemeConfig _active;
        private static TMP_FontAsset _inter;
        private static bool _resourcesResolved;

        public static UIThemeConfig Active
        {
            get
            {
                if (_active != null)
                    return _active;

                _active = CreateInstance<UIThemeConfig>();
                _active.name = "ClassicCleanUI (Runtime)";
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
            var loaded = Resources.Load<UIThemeConfig>(ClassicCleanResource);
            if (loaded != null)
                _active = loaded;
            else if (_active == null)
            {
                _active = CreateInstance<UIThemeConfig>();
                _active.name = "ClassicCleanUI (Runtime)";
                _active.ApplyClassicCleanDefaults();
            }
        }

        public static void SetActive(UIThemeConfig theme)
        {
            if (theme != null)
            {
                _active = theme;
                _resourcesResolved = true;
            }
        }

        public static Color Backdrop => Active.backdrop;
        public static Color PanelBg => Active.panelBg;
        public static Color PanelBgSolid => Active.panelBgSolid;
        public static Color CardBg => Active.cardBg;
        public static Color BorderSubtle => Active.borderSubtle;
        public static Color BorderStrong => Active.borderStrong;
        public static Color TextPrimary => Active.textPrimary;
        public static Color TextMuted => Active.textMuted;
        public static Color TextAccent => Active.textAccent;
        public static Color TextOnAccent => Active.textOnAccent;
        public static Color ButtonAccent => Active.buttonAccent;
        public static Color ButtonAccentDepth => Active.buttonAccentDepth;
        public static Color ButtonFill => Active.buttonFill;
        public static Color ButtonAccentGlow => Active.buttonAccentGlow;
        public static float ButtonDepthPx => Active.buttonDepthPx;
        public static Color ButtonSecondaryFace => Active.buttonSecondaryFace;
        public static Color ButtonSecondaryBorder => Active.buttonSecondaryBorder;
        public static Color ButtonSecondaryText => Active.buttonSecondaryText;
        public static Color ButtonDangerFace => Active.buttonDangerFace;
        public static Color ButtonDangerBorder => Active.buttonDangerBorder;
        public static Color ButtonDangerText => Active.buttonDangerText;
        public static Color RowA => Active.rowA;
        public static Color RowB => Active.rowB;
        public static Color RowHint => Active.rowHint;
        public static Color RowHintBorder => Active.rowHintBorder;
        public static Color RowCommitted => Active.rowCommitted;
        public static Color RowSelected => Active.rowSelected;
        public static Color ScoreCommitted => Active.scoreCommitted;
        public static Color ScoreHint => Active.scoreHint;
        public static Color SectionHeaderBg => Active.sectionHeaderBg;
        public static Color SectionHeaderText => Active.sectionHeaderText;
        public static Color SectionAltBg => Active.sectionAltBg;
        public static Color SectionAltText => Active.sectionAltText;
        public static Color DiceBody => Active.diceBody;
        public static Color DicePip => Active.dicePip;
        public static Color DicePipAce => Active.dicePipAce;

        public static TMP_FontAsset InterFont
        {
            get
            {
                if (_inter != null)
                    return _inter;
                if (!_resourcesResolved)
                    return null;
                _inter = Resources.Load<TMP_FontAsset>(InterFontResource);
                return _inter;
            }
        }

        public static void ApplyFont(TextMeshProUGUI label)
        {
            if (label == null || InterFont == null)
                return;
            label.font = InterFont;
        }

        public void ApplyClassicCleanDefaults()
        {
            themeId = "classic-clean";
            themeName = "Classic Clean";
            backdrop = Hex(0xE2E8F0);
            panelBg = new Color(1f, 1f, 1f, 0.98f);
            panelBgSolid = Color.white;
            cardBg = Color.white;
            borderSubtle = Hex(0x94A3B8);
            borderStrong = Hex(0x1E293B);
            textPrimary = Hex(0x0F172A);
            textMuted = Hex(0x64748B);
            textAccent = Hex(0x2563EB);
            textOnAccent = Color.white;
            buttonAccent = Hex(0x2563EB);
            buttonAccentDepth = Hex(0x1D4ED8);
            buttonFill = Color.white;
            buttonAccentGlow = new Color(0.145f, 0.388f, 0.922f, 0.18f);
            buttonDepthPx = 5f;
            buttonSecondaryFace = Color.white;
            buttonSecondaryBorder = Hex(0x94A3B8);
            buttonSecondaryText = Hex(0x0F172A);
            buttonDangerFace = Hex(0xFEE2E2);
            buttonDangerBorder = Hex(0xDC2626);
            buttonDangerText = Hex(0xDC2626);
            rowA = Color.white;
            rowB = Hex(0xF8FAFC);
            rowHint = Hex(0xDBEAFE);
            rowHintBorder = Hex(0x2563EB);
            rowCommitted = new Color(0.941f, 0.953f, 0.965f, 0.95f);
            rowSelected = Hex(0xD1E4FF);
            scoreCommitted = Hex(0x0F172A);
            scoreHint = Hex(0x2563EB);
            sectionHeaderBg = Hex(0x1E293B);
            sectionHeaderText = Color.white;
            sectionAltBg = Hex(0xE2E8F0);
            sectionAltText = Hex(0x0F172A);
            diceBody = Hex(0xFAFAFA);
            dicePip = Hex(0x1E232A);
            dicePipAce = new Color(0.86f, 0.22f, 0.22f, 1f);
        }

        private static Color Hex(int rgb)
        {
            return new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);
        }
    }
}
