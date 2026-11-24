using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Yahtzee.Core;

namespace Yahtzee.UI
{
    public static class UiFactory
    {
        public static Color PanelColor => UIThemeConfig.PanelBg;
        public static Color FrameColor => UIThemeConfig.BorderSubtle;
        public static Color MenuBackdropColor => UIThemeConfig.Backdrop;
        public static Color ButtonColor => UIThemeConfig.ButtonAccent;
        public static Color AccentTextColor => UIThemeConfig.TextOnAccent;
        public static Color GlassColor => UIThemeConfig.ButtonSecondaryFace;
        public static Color GlassRimColor => UIThemeConfig.ButtonSecondaryBorder;
        public static Color GlassTextColor => UIThemeConfig.ButtonSecondaryText;
        public static Color ButtonTextColor => UIThemeConfig.ButtonSecondaryText;
        public static Color LabelColor => UIThemeConfig.TextPrimary;
        public static Color TitleGold => UIThemeConfig.TextAccent;
        public static Color MutedText => UIThemeConfig.TextMuted;
        public static readonly Color CoinColor = new Color(0.92f, 0.72f, 0.2f, 1f);
        public static Color RatingColor => UIThemeConfig.TextAccent;

        private static Sprite _roundSprite;
        private static Sprite _pillSprite;
        private static Sprite _panelSprite;
        private static Sprite _solidSprite;
        private static Sprite _coinSprite;
        private static Sprite _diamondSprite;
        private static Sprite _feltSprite;
        private static Sprite _vignetteSprite;
        private static readonly Dictionary<string, Sprite> IconSprites = new Dictionary<string, Sprite>();

        private static Font _font;

        public static Font Font
        {
            get
            {
                if (_font != null)
                    return _font;

                _font = Font.CreateDynamicFontFromOSFont(new[]
                {
                    "Segoe UI",
                    "Arial",
                    "Roboto",
                    "Noto Sans",
                    "DejaVu Sans",
                    "Liberation Sans"
                }, 32);
                if (_font == null)
                    _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

                return _font;
            }
        }

        public static Sprite CoinSpritePublic => CoinSprite;

        public static void EnsureScreenFillsCanvas(RectTransform screen)
        {
            if (screen == null)
                return;

            var canvas = screen.GetComponentInParent<Canvas>();
            if (canvas != null && screen.parent != canvas.transform)
                screen.SetParent(canvas.transform, false);

            screen.SetAsLastSibling();
            screen.anchorMin = Vector2.zero;
            screen.anchorMax = Vector2.one;
            screen.pivot = new Vector2(0.5f, 0.5f);
            screen.offsetMin = Vector2.zero;
            screen.offsetMax = Vector2.zero;
            screen.sizeDelta = Vector2.zero;
            screen.anchoredPosition = Vector2.zero;
            screen.localScale = Vector3.one;
            screen.localRotation = Quaternion.identity;

            Canvas.ForceUpdateCanvases();
            if (screen.rect.width >= 480f && screen.rect.height >= 480f)
                return;

            screen.anchorMin = new Vector2(0.5f, 0.5f);
            screen.anchorMax = new Vector2(0.5f, 0.5f);
            screen.pivot = new Vector2(0.5f, 0.5f);
            screen.sizeDelta = new Vector2(1600f, 900f);
            screen.anchoredPosition = Vector2.zero;
            screen.localScale = Vector3.one;
        }

        public static void PrepareScreen(RectTransform screen, bool dimBackground)
        {
            screen.anchorMin = Vector2.zero;
            screen.anchorMax = Vector2.one;
            screen.pivot = new Vector2(0.5f, 0.5f);
            screen.offsetMin = Vector2.zero;
            screen.offsetMax = Vector2.zero;
            screen.localScale = Vector3.one;
            screen.localPosition = Vector3.zero;

            if (!dimBackground)
                return;

            ApplyMenuBackdrop(screen, MenuBackdropSprite());
        }

        public static void ClearContent(Transform screen)
        {
            if (screen == null)
                return;

            for (var index = screen.childCount - 1; index >= 0; index--)
            {
                var child = screen.GetChild(index);
                if (child == null)
                    continue;

                var name = child.name;
                if (name == "MenuBackdrop" || name == "MenuVignette")
                    continue;

                Object.Destroy(child.gameObject);
            }
        }

        public static void ApplyMenuBackdrop(RectTransform screen, Sprite sprite)
        {
            if (screen == null)
                return;

            var screenImage = screen.GetComponent<Image>();
            if (screenImage != null)
            {
                screenImage.color = Color.clear;
                screenImage.raycastTarget = false;
            }

            var existing = screen.Find("MenuBackdrop");
            Image backdrop;
            if (existing == null)
            {
                var backdropObject = new GameObject("MenuBackdrop", typeof(RectTransform), typeof(Image));
                backdropObject.layer = screen.gameObject.layer;
                backdropObject.transform.SetParent(screen, false);
                backdropObject.transform.SetAsFirstSibling();
                Stretch(backdropObject.GetComponent<RectTransform>());
                backdrop = backdropObject.GetComponent<Image>();
            }
            else
            {
                existing.SetAsFirstSibling();
                backdrop = existing.GetComponent<Image>();
            }

            if (backdrop == null)
                return;

            if (sprite != null)
            {
                backdrop.sprite = sprite;
                backdrop.color = new Color(0.35f, 0.42f, 0.5f, 1f);
            }
            else
            {
                backdrop.sprite = FeltBackdropSprite;
                backdrop.color = Color.white;
            }

            backdrop.type = Image.Type.Simple;
            backdrop.preserveAspect = false;
            backdrop.raycastTarget = true;

            // Classic Clean: no dark vignette / neon scrim.
            var vignette = screen.Find("MenuVignette");
            if (vignette != null)
                vignette.gameObject.SetActive(false);

            var scrim = screen.Find("GlassScrim");
            if (scrim != null)
                Object.Destroy(scrim.gameObject);
        }

        private static void EnsureVignette(RectTransform screen)
        {
            var existing = screen.Find("MenuVignette");
            Image vignette;
            if (existing == null)
            {
                var go = new GameObject("MenuVignette", typeof(RectTransform), typeof(Image));
                go.layer = screen.gameObject.layer;
                go.transform.SetParent(screen, false);
                go.transform.SetSiblingIndex(1);
                Stretch(go.GetComponent<RectTransform>());
                vignette = go.GetComponent<Image>();
            }
            else
            {
                existing.SetSiblingIndex(1);
                vignette = existing.GetComponent<Image>();
            }

            if (vignette == null)
                return;

            vignette.sprite = VignetteSprite;
            vignette.type = Image.Type.Simple;
            vignette.color = Color.white;
            vignette.raycastTarget = false;
            vignette.preserveAspect = false;
        }

        public static Image CreatePanel(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = PanelSprite;
            image.type = Image.Type.Sliced;
            image.color = PanelColor;
            image.raycastTarget = true;
            EnsureNeonOutline(go, GlassRimColor);
            return image;
        }

        /// <summary>Invisible layout root — no glass underlay / neon frame.</summary>
        public static RectTransform CreateContentRoot(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        /// <summary>Solid light menu backdrop — hides the 3D board / pedestal behind UI.</summary>
        public static void ApplyOpaqueMenuBackground(RectTransform screen)
        {
            if (screen == null)
                return;

            PrepareScreen(screen, true);
            var backdrop = screen.Find("MenuBackdrop");
            if (backdrop == null)
                return;

            var image = backdrop.GetComponent<Image>();
            if (image == null)
                return;

            image.sprite = SolidSprite;
            image.type = Image.Type.Simple;
            image.color = UIThemeConfig.Backdrop;
            image.raycastTarget = true;
            backdrop.SetAsFirstSibling();

            var vignette = screen.Find("MenuVignette");
            if (vignette != null)
                vignette.gameObject.SetActive(false);
        }

        /// <summary>Large mobile-friendly action height (easy thumb targets).</summary>
        public static float MenuActionHeight => Screen.width > Screen.height ? 112f : 140f;

        /// <summary>Centered column width (~70% of reference).</summary>
        public static float MenuActionWidth => Screen.width > Screen.height ? 920f : 780f;

        public static float MenuActionSpacing => Screen.width > Screen.height ? 22f : 28f;

        /// <summary>Portrait-safe scroll form: large centered buttons like the main menu.</summary>
        public static RectTransform BeginScrollForm(Transform parent, out ScrollRect scroll)
        {
            var landscape = Screen.width > Screen.height;
            var scrollRoot = new GameObject("MenuScroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            scrollRoot.layer = parent.gameObject.layer;
            scrollRoot.transform.SetParent(parent, false);
            var scrollImage = scrollRoot.GetComponent<Image>();
            scrollImage.color = Color.clear;
            scrollImage.raycastTarget = true;
            var scrollRectTransform = scrollRoot.GetComponent<RectTransform>();
            Place(scrollRectTransform, 0.05f, 0.03f, 0.95f, 0.97f);
            scrollRectTransform.offsetMin = Vector2.zero;
            scrollRectTransform.offsetMax = Vector2.zero;

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            viewport.layer = parent.gameObject.layer;
            viewport.transform.SetParent(scrollRoot.transform, false);
            var viewportImage = viewport.GetComponent<Image>();
            viewportImage.color = Color.clear;
            viewportImage.raycastTarget = true;
            Stretch(viewport.GetComponent<RectTransform>());

            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.layer = parent.gameObject.layer;
            content.transform.SetParent(viewport.transform, false);
            var contentRect = content.GetComponent<RectTransform>();
            // Start stretched so MiddleCenter works once we know sizes.
            contentRect.anchorMin = Vector2.zero;
            contentRect.anchorMax = Vector2.one;
            contentRect.pivot = new Vector2(0.5f, 0.5f);
            contentRect.offsetMin = Vector2.zero;
            contentRect.offsetMax = Vector2.zero;
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = Vector2.zero;

            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(20, 20, landscape ? 24 : 40, landscape ? 24 : 40);
            layout.spacing = MenuActionSpacing;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var fitter = content.GetComponent<ContentSizeFitter>();
            // Preferred height is measured in Finalize; keep unconstrained while stretched.
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.enabled = false;

            scroll = scrollRoot.GetComponent<ScrollRect>();
            scroll.viewport = viewport.GetComponent<RectTransform>();
            scroll.content = contentRect;
            scroll.horizontal = false;
            scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 48f;

            MenuFormFitter.Bind(scroll);
            return contentRect;
        }

        /// <summary>
        /// Center the menu stack vertically when it fits; otherwise top-align and enable scroll.
        /// Safe to call before the viewport has a real height — MenuFormFitter retries.
        /// </summary>
        public static void FinalizeMenuForm(ScrollRect scroll)
        {
            if (scroll == null || scroll.content == null || scroll.viewport == null)
                return;

            Canvas.ForceUpdateCanvases();
            var root = scroll.transform as RectTransform;
            if (root != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(root);
            LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.viewport);
            LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);

            var layout = scroll.content.GetComponent<VerticalLayoutGroup>();
            if (layout != null)
                layout.spacing = MenuActionSpacing;

            // Measure children while content is stretched (fitter off).
            var fitter = scroll.content.GetComponent<ContentSizeFitter>();
            if (fitter != null)
                fitter.enabled = false;

            var height = MeasureMenuContentHeight(scroll.content);
            var viewHeight = scroll.viewport.rect.height;
            if (viewHeight < 8f && root != null)
                viewHeight = root.rect.height;
            if (viewHeight < 8f)
            {
                // Layout not ready yet — MenuFormFitter will retry next frames.
                MenuFormFitter.Bind(scroll);
                return;
            }

            var content = scroll.content;
            var fits = height <= viewHeight - 4f;
            if (fits)
            {
                // Fill viewport; VLG MiddleCenter keeps the stack vertically centered.
                content.anchorMin = Vector2.zero;
                content.anchorMax = Vector2.one;
                content.pivot = new Vector2(0.5f, 0.5f);
                content.offsetMin = Vector2.zero;
                content.offsetMax = Vector2.zero;
                content.anchoredPosition = Vector2.zero;
                content.sizeDelta = Vector2.zero;
                if (layout != null)
                    layout.childAlignment = TextAnchor.MiddleCenter;
                if (fitter != null)
                    fitter.enabled = false;
                scroll.vertical = false;
                scroll.normalizedPosition = new Vector2(0f, 0.5f);
            }
            else
            {
                content.anchorMin = new Vector2(0f, 1f);
                content.anchorMax = new Vector2(1f, 1f);
                content.pivot = new Vector2(0.5f, 1f);
                content.anchoredPosition = Vector2.zero;
                content.sizeDelta = new Vector2(0f, height);
                if (layout != null)
                    layout.childAlignment = TextAnchor.UpperCenter;
                if (fitter != null)
                {
                    fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                    fitter.enabled = true;
                }

                scroll.vertical = true;
                scroll.normalizedPosition = new Vector2(0f, 1f);
                LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            }
        }

        private static float MeasureMenuContentHeight(RectTransform content)
        {
            if (content == null)
                return 1f;

            var layout = content.GetComponent<VerticalLayoutGroup>();
            var padding = layout != null ? layout.padding.top + layout.padding.bottom : 0f;
            var spacing = layout != null ? layout.spacing : 0f;
            var total = (float)padding;
            var counted = 0;
            for (var i = 0; i < content.childCount; i++)
            {
                var child = content.GetChild(i) as RectTransform;
                if (child == null || !child.gameObject.activeInHierarchy)
                    continue;

                var element = child.GetComponent<LayoutElement>();
                var h = 0f;
                if (element != null && element.preferredHeight > 0f)
                    h = element.preferredHeight;
                else if (element != null && element.minHeight > 0f)
                    h = element.minHeight;
                else
                    h = Mathf.Max(child.rect.height, LayoutUtility.GetPreferredHeight(child));

                if (h < 1f)
                    h = MenuActionHeight;

                if (counted > 0)
                    total += spacing;
                total += h;
                counted++;
            }

            return Mathf.Max(1f, total);
        }

        public static void SizeMenuRow(Component target, float height)
        {
            if (target == null)
                return;

            var rect = target.transform as RectTransform;
            if (rect == null)
                return;

            var element = rect.GetComponent<LayoutElement>();
            if (element == null)
                element = rect.gameObject.AddComponent<LayoutElement>();
            element.minHeight = height;
            element.preferredHeight = height;
            element.flexibleHeight = 0f;
            element.flexibleWidth = 0f;
            element.minWidth = MenuActionWidth * 0.92f;
            element.preferredWidth = MenuActionWidth;
        }

        public static void SizeMenuAction(Component target, float height = -1f)
        {
            var resolved = height > 0f ? Mathf.Max(height, MenuActionHeight) : MenuActionHeight;
            SizeMenuRow(target, resolved);
        }

        public static Text AddMenuTitle(Transform parent, string title, string subtitle = null)
        {
            var titleLabel = CreateLabel(parent, "Title", title, 42, TextAnchor.MiddleCenter);
            titleLabel.fontStyle = FontStyle.Bold;
            titleLabel.color = TitleGold;
            titleLabel.resizeTextForBestFit = true;
            titleLabel.resizeTextMinSize = 28;
            titleLabel.resizeTextMaxSize = 48;
            SizeMenuRow(titleLabel, 52f);

            if (!string.IsNullOrEmpty(subtitle))
            {
                var sub = CreateLabel(parent, "Subtitle", subtitle, 22, TextAnchor.MiddleCenter);
                sub.color = MutedText;
                sub.resizeTextForBestFit = true;
                sub.resizeTextMinSize = 16;
                sub.resizeTextMaxSize = 24;
                SizeMenuRow(sub, 34f);
            }

            return titleLabel;
        }

        public static Text AddMenuCaption(Transform parent, string text)
        {
            var caption = CreateLabel(parent, "Caption", text, 22, TextAnchor.MiddleCenter);
            caption.color = LabelColor;
            SizeMenuRow(caption, 32f);
            return caption;
        }

        public static Button AddMenuAction(Transform parent, string label, bool accent, float height = -1f)
        {
            var button = CreateButton(parent, label);
            if (accent)
                StyleAccent(button);
            else
                StyleGlass(button);

            SizeMenuAction(button, height);
            var caption = button.GetComponentInChildren<Text>();
            if (caption != null)
            {
                caption.fontSize = 32;
                caption.resizeTextForBestFit = true;
                caption.resizeTextMinSize = 20;
                caption.resizeTextMaxSize = 36;
            }

            return button;
        }

        public static RectTransform CreateFramedCard(Transform parent, string name)
        {
            var frame = new GameObject(name, typeof(RectTransform), typeof(Image));
            frame.layer = parent.gameObject.layer;
            frame.transform.SetParent(parent, false);
            var frameImage = frame.GetComponent<Image>();
            frameImage.sprite = PanelSprite;
            frameImage.type = Image.Type.Sliced;
            frameImage.color = PanelColor;
            frameImage.raycastTarget = false;
            EnsureSoftShadow(frame);

            var inner = new GameObject("Inner", typeof(RectTransform), typeof(Image));
            inner.layer = parent.gameObject.layer;
            inner.transform.SetParent(frame.transform, false);
            var innerImage = inner.GetComponent<Image>();
            innerImage.sprite = PanelSprite;
            innerImage.type = Image.Type.Sliced;
            innerImage.color = PanelColor;
            innerImage.raycastTarget = true;
            var innerRect = inner.GetComponent<RectTransform>();
            Stretch(innerRect);
            innerRect.offsetMin = new Vector2(1f, 1f);
            innerRect.offsetMax = new Vector2(-1f, -1f);

            return frame.GetComponent<RectTransform>();
        }

        public static void EnsureNeonOutline(GameObject target, Color color, bool soft = false)
        {
            // Flat Material: prefer soft drop shadow over neon outlines.
            EnsureSoftShadow(target);
            if (target == null)
                return;

            var outline = target.GetComponent<Outline>();
            if (outline == null)
                return;

            outline.effectColor = UIThemeConfig.BorderSubtle;
            outline.effectDistance = new Vector2(1f, -1f);
            outline.enabled = soft;
        }

        public static void EnsureSoftShadow(GameObject target)
        {
            EnsureTactileChrome(target, null, 0f);
        }

        public static void ClearChromeEffects(GameObject target)
        {
            if (target == null)
                return;

            var outlines = target.GetComponents<Outline>();
            for (var i = 0; i < outlines.Length; i++)
                outlines[i].enabled = false;

            var shadows = target.GetComponents<Shadow>();
            for (var i = 0; i < shadows.Length; i++)
                shadows[i].enabled = false;
        }

        public static Text CreateScreenHeader(Transform parent, string title, string subtitle = null)
        {
            var titleLabel = CreateLabel(parent, "Title", title, 44, TextAnchor.MiddleCenter);
            titleLabel.fontStyle = FontStyle.Bold;
            titleLabel.color = TitleGold;
            titleLabel.resizeTextForBestFit = true;
            titleLabel.resizeTextMinSize = 28;
            titleLabel.resizeTextMaxSize = 48;
            Place(titleLabel.rectTransform, 0.1f, 0.86f, 0.9f, 0.96f);

            var rule = new GameObject("TitleRule", typeof(RectTransform), typeof(Image));
            rule.layer = parent.gameObject.layer;
            rule.transform.SetParent(parent, false);
            var ruleImage = rule.GetComponent<Image>();
            ruleImage.sprite = PillSprite;
            ruleImage.type = Image.Type.Sliced;
            ruleImage.color = new Color(UIThemeConfig.TextAccent.r, UIThemeConfig.TextAccent.g, UIThemeConfig.TextAccent.b, 0.45f);
            ruleImage.raycastTarget = false;
            Place(rule.GetComponent<RectTransform>(), 0.35f, 0.845f, 0.65f, 0.855f);

            if (!string.IsNullOrEmpty(subtitle))
            {
                var sub = CreateLabel(parent, "Subtitle", subtitle, 22, TextAnchor.MiddleCenter);
                sub.color = MutedText;
                Place(sub.rectTransform, 0.12f, 0.79f, 0.88f, 0.845f);
            }

            return titleLabel;
        }

        public static void AttachMenuIcon(Button button, string iconKey)
        {
            if (button == null)
                return;

            const float iconSize = 96f;
            const float leftPad = 14f;

            var existing = button.transform.Find("MenuIcon");
            Image icon;
            if (existing == null)
            {
                var go = new GameObject("MenuIcon", typeof(RectTransform), typeof(Image));
                go.layer = button.gameObject.layer;
                go.transform.SetParent(button.transform, false);
                icon = go.GetComponent<Image>();
            }
            else
            {
                icon = existing.GetComponent<Image>();
            }

            icon.raycastTarget = false;
            icon.preserveAspect = true;
            icon.sprite = MenuIcon(iconKey);
            icon.color = GlassTextColor;
            icon.type = Image.Type.Simple;

            var rect = icon.rectTransform;
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(iconSize, iconSize);
            rect.anchoredPosition = new Vector2(leftPad + iconSize * 0.5f, 0f);
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;

            var caption = button.GetComponentInChildren<Text>();
            if (caption == null)
                return;

            caption.alignment = TextAnchor.MiddleLeft;
            caption.horizontalOverflow = HorizontalWrapMode.Overflow;
            caption.verticalOverflow = VerticalWrapMode.Truncate;
            var captionRect = caption.rectTransform;
            captionRect.anchorMin = Vector2.zero;
            captionRect.anchorMax = Vector2.one;
            captionRect.pivot = new Vector2(0.5f, 0.5f);
            captionRect.offsetMin = new Vector2(leftPad + iconSize + 14f, 4f);
            captionRect.offsetMax = new Vector2(-18f, -4f);
            captionRect.localScale = Vector3.one;
            captionRect.localRotation = Quaternion.identity;
        }

        private static Sprite MenuBackdropSprite()
        {
            var menu = UnityEngine.Object.FindAnyObjectByType<MainMenuScreen>(FindObjectsInactive.Include);
            return menu != null ? menu.MenuBackdropSprite : null;
        }

        public static void RepairCanvas(Canvas canvas)
        {
            if (canvas == null)
                return;

            var root = canvas.GetComponent<RectTransform>();
            if (root != null && root.localScale.sqrMagnitude < 0.001f)
            {
                root.localScale = Vector3.one;
                root.anchorMin = Vector2.zero;
                root.anchorMax = Vector2.one;
                root.pivot = new Vector2(0.5f, 0.5f);
                root.offsetMin = Vector2.zero;
                root.offsetMax = Vector2.zero;
            }

            FitCanvas(canvas);
        }

        public static void FitCanvas(Canvas canvas)
        {
            if (canvas == null)
                return;

            var scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler == null)
                return;

            var landscape = Screen.width > Screen.height;
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = landscape ? new Vector2(1920f, 1080f) : new Vector2(1080f, 1920f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
        }

        public static Text CreateLabel(Transform parent, string name, string value, int fontSize, TextAnchor alignment)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = Font;
            text.text = value;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = LabelColor;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        public static Button CreateButton(Transform parent, string label)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = RoundSprite;
            image.type = Image.Type.Sliced;
            image.color = GlassColor;
            var button = go.GetComponent<Button>();
            var colors = button.colors;
            colors.fadeDuration = 0.08f;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.08f, 1.08f, 1.12f, 1f);
            colors.pressedColor = new Color(0.82f, 0.82f, 0.9f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(1f, 1f, 1f, 0.45f);
            button.colors = colors;

            var caption = CreateLabel(go.transform, "Label", label, 26, TextAnchor.MiddleCenter);
            caption.color = GlassTextColor;
            caption.resizeTextForBestFit = true;
            caption.resizeTextMinSize = 14;
            caption.resizeTextMaxSize = 30;
            Stretch(caption.rectTransform);
            StyleGlass(button);
            return button;
        }

        public static void StyleAccent(Button button)
        {
            StyleButton(button, ButtonRole.Primary, false);
        }

        public static void StyleGlass(Button button)
        {
            StyleButton(button, ButtonRole.Secondary, false);
        }

        public static void StylePill(Button button)
        {
            StyleButton(button, ButtonRole.Primary, true);
        }

        public static void StyleDestructive(Button button)
        {
            StyleButton(button, ButtonRole.Destructive, false);
        }

        public static void StylePrimary(Button button)
        {
            StyleButton(button, ButtonRole.Primary, false);
        }

        public static void StyleSecondary(Button button)
        {
            StyleButton(button, ButtonRole.Secondary, false);
        }

        private enum ButtonRole
        {
            Primary,
            Secondary,
            Destructive
        }

        public static Image CreateBadge(Transform parent, string name, bool coin)
        {
            var badge = new GameObject(name, typeof(RectTransform), typeof(Image));
            badge.layer = parent.gameObject.layer;
            badge.transform.SetParent(parent, false);
            var image = badge.GetComponent<Image>();
            image.sprite = coin ? CoinSprite : DiamondSprite;
            image.color = coin ? CoinColor : RatingColor;
            image.raycastTarget = false;
            image.preserveAspect = true;
            return image;
        }

        private static void StyleButton(Button button, ButtonRole role, bool pill)
        {
            if (button == null)
                return;

            var sprite = pill ? PillSprite : RoundSprite;
            var image = button.GetComponent<Image>();
            Color face;
            Color textColor;
            Color? outlineColor;
            Color? depthColor;
            switch (role)
            {
                case ButtonRole.Primary:
                    face = UIThemeConfig.ButtonAccent;
                    textColor = UIThemeConfig.TextOnAccent;
                    outlineColor = null;
                    depthColor = UIThemeConfig.ButtonAccentDepth;
                    break;
                case ButtonRole.Destructive:
                    face = UIThemeConfig.ButtonDangerFace;
                    textColor = UIThemeConfig.ButtonDangerText;
                    outlineColor = UIThemeConfig.ButtonDangerBorder;
                    depthColor = null;
                    break;
                default:
                    face = UIThemeConfig.ButtonSecondaryFace;
                    textColor = UIThemeConfig.ButtonSecondaryText;
                    outlineColor = UIThemeConfig.ButtonSecondaryBorder;
                    depthColor = null;
                    break;
            }

            if (image != null)
            {
                image.sprite = sprite;
                image.type = Image.Type.Sliced;
                image.color = face;
            }

            var caption = button.GetComponentInChildren<Text>();
            if (caption != null)
            {
                caption.color = textColor;
                caption.fontStyle = FontStyle.Bold;
            }

            var rim = button.transform.Find("Rim");
            if (rim != null)
                rim.gameObject.SetActive(false);
            var glow = button.transform.Find("Glow");
            if (glow != null)
                glow.gameObject.SetActive(false);

            if (depthColor.HasValue)
                EnsureDepthLedge(button, sprite, depthColor.Value, UIThemeConfig.ButtonDepthPx);
            else
                SetDepthLedgeActive(button, false);

            EnsureTactileChrome(button.gameObject, outlineColor, outlineColor.HasValue ? 1.75f : 0f);
            ButtonVisualFeedback.Ensure(button);
            var feedback = button.GetComponent<ButtonVisualFeedback>();
            if (feedback != null)
                feedback.Configure(depthColor.HasValue ? UIThemeConfig.ButtonDepthPx - 1.5f : 3f, face);
        }

        private static void EnsureDepthLedge(Button button, Sprite sprite, Color color, float depthPx)
        {
            var existing = button.transform.Find("DepthLedge");
            Image ledge;
            if (existing == null)
            {
                var go = new GameObject("DepthLedge", typeof(RectTransform), typeof(Image));
                go.layer = button.gameObject.layer;
                go.transform.SetParent(button.transform, false);
                go.transform.SetAsFirstSibling();
                ledge = go.GetComponent<Image>();
                ledge.raycastTarget = false;
            }
            else
            {
                existing.gameObject.SetActive(true);
                ledge = existing.GetComponent<Image>();
            }

            ledge.sprite = sprite;
            ledge.type = Image.Type.Sliced;
            ledge.color = color;
            var ignore = ledge.GetComponent<LayoutElement>();
            if (ignore == null)
                ignore = ledge.gameObject.AddComponent<LayoutElement>();
            ignore.ignoreLayout = true;
            var rect = ledge.rectTransform;
            Stretch(rect);
            rect.offsetMin = new Vector2(0f, -depthPx);
            rect.offsetMax = new Vector2(0f, -1f);
        }

        private static void SetDepthLedgeActive(Button button, bool active)
        {
            var ledge = button.transform.Find("DepthLedge");
            if (ledge != null)
                ledge.gameObject.SetActive(active);
        }

        public static void EnsureTactileChrome(GameObject target, Color? outlineColor, float outlinePx)
        {
            if (target == null)
                return;

            var shadow = target.GetComponent<Shadow>();
            if (shadow == null)
                shadow = target.AddComponent<Shadow>();
            shadow.effectColor = new Color(0.08f, 0.12f, 0.2f, 0.22f);
            shadow.effectDistance = new Vector2(0f, -3f);
            shadow.useGraphicAlpha = true;
            shadow.enabled = true;

            var outline = target.GetComponent<Outline>();
            if (outlineColor.HasValue && outlinePx > 0.1f)
            {
                if (outline == null)
                    outline = target.AddComponent<Outline>();
                outline.effectColor = outlineColor.Value;
                outline.effectDistance = new Vector2(outlinePx, -outlinePx);
                outline.useGraphicAlpha = true;
                outline.enabled = true;
            }
            else if (outline != null)
            {
                outline.enabled = false;
            }
        }

        private static void EnsureRim(Button button, Sprite sprite, Color color)
        {
            var existing = button.transform.Find("Rim");
            Image rim;
            if (existing == null)
            {
                var go = new GameObject("Rim", typeof(RectTransform), typeof(Image));
                go.layer = button.gameObject.layer;
                go.transform.SetParent(button.transform, false);
                go.transform.SetAsFirstSibling();
                rim = go.GetComponent<Image>();
                rim.raycastTarget = false;
            }
            else
            {
                rim = existing.GetComponent<Image>();
            }

            rim.sprite = sprite;
            rim.type = Image.Type.Sliced;
            rim.color = color;
            var rect = rim.rectTransform;
            Stretch(rect);
            rect.offsetMin = new Vector2(-2f, -2f);
            rect.offsetMax = new Vector2(2f, 2f);
        }

        public static Sprite RoundSprite
        {
            get
            {
                if (_roundSprite == null)
                    _roundSprite = BuildSprite(96, 96, 28);
                return _roundSprite;
            }
        }

        public static Sprite PillSprite
        {
            get
            {
                if (_pillSprite == null)
                    _pillSprite = BuildSprite(160, 72, 36);
                return _pillSprite;
            }
        }

        public static Sprite PanelSprite
        {
            get
            {
                if (_panelSprite == null)
                    _panelSprite = BuildSprite(128, 128, 24);
                return _panelSprite;
            }
        }

        /// <summary>Fully opaque square sprite — no rounded corners / alpha fringes.</summary>
        public static Sprite SolidSprite
        {
            get
            {
                if (_solidSprite == null)
                    _solidSprite = BuildSolidSprite(8);
                return _solidSprite;
            }
        }

        private static Sprite FeltBackdropSprite
        {
            get
            {
                if (_feltSprite == null)
                    _feltSprite = BuildFeltBackdrop(256, 256);
                return _feltSprite;
            }
        }

        private static Sprite VignetteSprite
        {
            get
            {
                if (_vignetteSprite == null)
                    _vignetteSprite = BuildVignette(128);
                return _vignetteSprite;
            }
        }

        private static Sprite CoinSprite
        {
            get
            {
                if (_coinSprite == null)
                    _coinSprite = BuildDisc(64);
                return _coinSprite;
            }
        }

        private static Sprite MenuIcon(string key)
        {
            if (IconSprites.TryGetValue(key, out var sprite) && sprite != null)
                return sprite;

            sprite = BuildMenuIcon(key);
            IconSprites[key] = sprite;
            return sprite;
        }

        private static Sprite DiamondSprite
        {
            get
            {
                if (_diamondSprite == null)
                    _diamondSprite = BuildDiamond(64);
                return _diamondSprite;
            }
        }

        private static Sprite BuildSprite(int width, int height, int radius)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            var pixels = new Color32[width * height];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var coverage = RoundCoverage(x, y, width, height, radius);
                    pixels[y * width + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(coverage * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            var border = new Vector4(radius, radius, radius, radius);
            return Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
        }

        private static Sprite BuildSolidSprite(int size)
        {
            size = Mathf.Max(2, size);
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Point;
            var pixels = new Color32[size * size];
            for (var index = 0; index < pixels.Length; index++)
                pixels[index] = new Color32(255, 255, 255, 255);

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                Vector4.zero);
        }

        private static float RoundCoverage(int x, int y, int width, int height, int radius)
        {
            var px = x + 0.5f;
            var py = y + 0.5f;
            var dx = 0f;
            var dy = 0f;
            if (px < radius)
                dx = radius - px;
            else if (px > width - radius)
                dx = px - (width - radius);
            if (py < radius)
                dy = radius - py;
            else if (py > height - radius)
                dy = py - (height - radius);

            var inside = radius - Mathf.Sqrt(dx * dx + dy * dy);
            return Mathf.Clamp01(inside + 0.65f);
        }

        private static Sprite BuildFeltBackdrop(int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            var pixels = new Color32[width * height];
            var cx = (width - 1) * 0.5f;
            var cy = (height - 1) * 0.5f;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var noise = Mathf.PerlinNoise(x * 0.05f + 2f, y * 0.06f + 5f);
                    var radial = 1f - Mathf.Clamp01(Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy)) / (width * 0.85f));
                    // Cool slate-blue field so white cards read clearly.
                    var shade = 0.88f + noise * 0.04f + radial * 0.03f;
                    pixels[y * width + x] = new Color32(
                        (byte)Mathf.Clamp(shade * 230f, 0, 255),
                        (byte)Mathf.Clamp(shade * 236f, 0, 255),
                        (byte)Mathf.Clamp(shade * 244f, 0, 255),
                        255);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Sprite BuildVignette(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            var pixels = new Color32[size * size];
            var center = (size - 1) * 0.5f;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center)) / (size * 0.72f);
                    var alpha = Mathf.Clamp01((dist - 0.35f) / 0.65f);
                    alpha = alpha * alpha * 0.72f;
                    pixels[y * size + x] = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Sprite BuildMenuIcon(string key)
        {
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            var pixels = new Color32[size * size];
            var center = (size - 1) * 0.5f;

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var px = x + 0.5f;
                    var py = y + 0.5f;
                    var coverage = IconCoverage(key, px, py, center);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(coverage) * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        private static float IconCoverage(string key, float x, float y, float center)
        {
            var dx = x - center;
            var dy = y - center;
            switch (key)
            {
                case "online":
                    return GlobeCoverage(dx, dy);
                case "offline":
                    return DiceCoverage(dx, dy);
                case "settings":
                    return GearCoverage(dx, dy);
                case "shop":
                    return BagCoverage(dx, dy);
                case "custom":
                    return PaletteCoverage(dx, dy);
                default:
                    return RingCoverage(Mathf.Sqrt(dx * dx + dy * dy), 22f, 6f);
            }
        }

        private static float GlobeCoverage(float dx, float dy)
        {
            var radius = Mathf.Sqrt(dx * dx + dy * dy);
            var ring = RingCoverage(radius, 38f, 5.5f);
            var meridian = SoftBand(Mathf.Abs(dx), 3.2f) * SoftStep(38f - radius);
            var parallel = SoftBand(Mathf.Abs(dy), 3.2f) * SoftStep(38f - radius);
            return Mathf.Max(ring, Mathf.Max(meridian, parallel));
        }

        private static float DiceCoverage(float dx, float dy)
        {
            var box = SoftBox(dx, dy, 28f, 28f, 7f);
            var hole = SoftBox(dx, dy, 16f, 16f, 4f);
            var body = Mathf.Clamp01(box - hole);
            var pip = DiscCoverage(dx, dy, 5.5f);
            return Mathf.Max(body, pip);
        }

        private static float GearCoverage(float dx, float dy)
        {
            var radius = Mathf.Sqrt(dx * dx + dy * dy);
            var angle = Mathf.Atan2(dy, dx);
            var teeth = 0.5f + 0.5f * Mathf.Cos(angle * 6f);
            var outer = 34f + teeth * 8f;
            var rim = SoftStep(outer - radius) - SoftStep(18f - radius);
            var hub = DiscCoverage(dx, dy, 8f);
            return Mathf.Max(Mathf.Clamp01(rim), hub);
        }

        private static float BagCoverage(float dx, float dy)
        {
            var body = SoftBox(dx, dy + 4f, 26f, 24f, 8f);
            var handle = RingCoverage(Mathf.Sqrt(dx * dx + (dy - 18f) * (dy - 18f)), 14f, 4.5f)
                         * SoftStep(8f - Mathf.Abs(dx));
            return Mathf.Max(body, handle);
        }

        private static float PaletteCoverage(float dx, float dy)
        {
            var a = DiscCoverage(dx + 12f, dy - 6f, 14f);
            var b = DiscCoverage(dx - 10f, dy + 8f, 14f);
            var c = DiscCoverage(dx + 2f, dy + 14f, 10f);
            return Mathf.Max(a, Mathf.Max(b, c));
        }

        private static float RingCoverage(float radius, float outer, float thickness)
        {
            var inner = outer - thickness;
            return SoftStep(outer - radius) - SoftStep(inner - radius);
        }

        private static float DiscCoverage(float dx, float dy, float radius)
        {
            return SoftStep(radius - Mathf.Sqrt(dx * dx + dy * dy));
        }

        private static float SoftBox(float dx, float dy, float halfW, float halfH, float radius)
        {
            var qx = Mathf.Abs(dx) - (halfW - radius);
            var qy = Mathf.Abs(dy) - (halfH - radius);
            var outside = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f));
            var inside = Mathf.Min(Mathf.Max(qx, qy), 0f);
            return SoftStep(radius - (outside + inside));
        }

        private static float SoftBand(float distance, float halfWidth)
        {
            return SoftStep(halfWidth - Mathf.Abs(distance));
        }

        private static float SoftStep(float edge)
        {
            return Mathf.Clamp01(edge + 0.75f);
        }

        private static Sprite BuildDisc(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            var pixels = new Color32[size * size];
            var center = (size - 1) * 0.5f;
            var outer = size * 0.48f;
            var inner = size * 0.28f;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var distance = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                    var ring = distance <= outer && distance >= inner ? 1f : Mathf.Clamp01(outer - distance + 0.6f);
                    if (distance < inner)
                        ring = 1f;
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(ring) * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Sprite BuildDiamond(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            var pixels = new Color32[size * size];
            var center = (size - 1) * 0.5f;
            var reach = size * 0.46f;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var manhattan = Mathf.Abs(x - center) + Mathf.Abs(y - center);
                    var coverage = Mathf.Clamp01(reach - manhattan + 0.7f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(coverage * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        public static void Place(RectTransform rect, float xMin, float yMin, float xMax, float yMax)
        {
            rect.anchorMin = new Vector2(xMin, yMin);
            rect.anchorMax = new Vector2(xMax, yMax);
            rect.offsetMin = new Vector2(12f, 6f);
            rect.offsetMax = new Vector2(-12f, -6f);
            rect.localScale = Vector3.one;
        }

        public static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
        }

        public static TextMeshProUGUI CreateAutoLabel(Transform parent, string name, string value, TextAlignmentOptions alignment)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = value;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = alignment;
            tmp.color = LabelColor;
            tmp.raycastTarget = false;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 14f;
            tmp.fontSizeMax = 32f;
            tmp.fontSize = 32f;
            tmp.overflowMode = TextOverflowModes.Overflow;

            var font = ResolveTmpFont();
            if (font != null)
            {
                tmp.font = font;
                return tmp;
            }

            tmp.enabled = false;
            var fallback = CreateLabel(go.transform, "Fallback", value, 28, ToAnchor(alignment));
            fallback.fontStyle = FontStyle.Bold;
            fallback.color = LabelColor;
            fallback.resizeTextForBestFit = true;
            fallback.resizeTextMinSize = 14;
            fallback.resizeTextMaxSize = 32;
            Stretch(fallback.rectTransform);
            return tmp;
        }

        public static void ApplyAutoText(TextMeshProUGUI label, string value, Color color)
        {
            if (label == null)
                return;

            label.text = value;
            label.color = color;
            label.enableAutoSizing = true;
            label.fontSizeMin = 14f;
            label.fontSizeMax = 32f;
            var fallback = label.GetComponentInChildren<Text>(true);
            if (fallback == null)
                return;

            fallback.text = value;
            fallback.color = color;
            fallback.resizeTextForBestFit = true;
            fallback.resizeTextMinSize = 14;
            fallback.resizeTextMaxSize = 32;
        }

        private static TMP_FontAsset ResolveTmpFont()
        {
            var inter = UIThemeConfig.InterFont;
            if (inter != null)
                return inter;

            if (Resources.Load("TMP Settings") == null)
                return null;

            return TMP_Settings.defaultFontAsset;
        }

        private static TextAnchor ToAnchor(TextAlignmentOptions alignment)
        {
            switch (alignment)
            {
                case TextAlignmentOptions.MidlineLeft:
                case TextAlignmentOptions.Left:
                case TextAlignmentOptions.BottomLeft:
                case TextAlignmentOptions.TopLeft:
                    return TextAnchor.MiddleLeft;
                case TextAlignmentOptions.MidlineRight:
                case TextAlignmentOptions.Right:
                case TextAlignmentOptions.BottomRight:
                case TextAlignmentOptions.TopRight:
                    return TextAnchor.MiddleRight;
                default:
                    return TextAnchor.MiddleCenter;
            }
        }
    }
}
