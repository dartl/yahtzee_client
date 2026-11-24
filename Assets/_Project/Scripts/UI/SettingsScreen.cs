using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using Yahtzee.Audio;
using Yahtzee.Core;
using Yahtzee.Feedback;
using Yahtzee.Localization;
using Yahtzee.Network;

namespace Yahtzee.UI
{
    public sealed class SettingsScreen : UIScreen
    {
        public override ScreenType Type => ScreenType.Settings;

        private Toggle _sfxToggle;
        private Toggle _musicToggle;
        private InputField _nickname;
        private Text _status;
        private Button _langRu;
        private Button _langEn;
        private bool _built;
        private bool _busy;
        private bool _layoutLandscape = true;

        public override void Show()
        {
            base.Show();
            var rect = (RectTransform)transform;
            UiFactory.EnsureScreenFillsCanvas(rect);
            UiFactory.ApplyOpaqueMenuBackground(rect);
            UiFactory.FitCanvas(GetComponentInParent<Canvas>());

            var landscape = Screen.width > Screen.height;
            if (!_built || landscape != _layoutLandscape)
            {
                Rebuild();
                _built = true;
                _layoutLandscape = landscape;
            }

            RefreshFields();
            UiMotion.PopIn(transform);
        }

        private void OnEnable()
        {
            GameLocale.OnChanged += HandleLocaleChanged;
        }

        private void OnDisable()
        {
            GameLocale.OnChanged -= HandleLocaleChanged;
        }

        private void HandleLocaleChanged()
        {
            if (!isActiveAndEnabled)
                return;

            Rebuild();
            RefreshFields();
        }

        private void OnRectTransformDimensionsChange()
        {
            if (!isActiveAndEnabled || !_built)
                return;

            var landscape = Screen.width > Screen.height;
            if (landscape == _layoutLandscape)
                return;

            Rebuild();
            _layoutLandscape = landscape;
            RefreshFields();
        }

        private void Rebuild()
        {
            UiFactory.ClearContent(transform);
            UiFactory.ApplyOpaqueMenuBackground((RectTransform)transform);
            Build();
        }

        private void Build()
        {
            var content = UiFactory.BeginScrollForm(transform, out var scroll);
            var row = UiFactory.MenuActionHeight;

            UiFactory.AddMenuTitle(content, GameText.Get("ui.settings.title"), GameText.Get("ui.settings.subtitle"));

            _sfxToggle = CreateToggleRow(content, GameText.Get("ui.settings.sfx"), GameAudio.SfxEnabled, row, value =>
            {
                GameAudio.SfxEnabled = value;
                if (value)
                    GameAudio.PlayUiClick();
            });
            _musicToggle = CreateToggleRow(content, GameText.Get("ui.settings.music"), GameAudio.MusicEnabled, row, value =>
            {
                GameAudio.MusicEnabled = value;
                if (value)
                    GameAudio.PlayUiClick();
            });

            UiFactory.AddMenuCaption(content, GameText.Get("ui.settings.language"));
            var langRow = new GameObject("Language", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            langRow.layer = gameObject.layer;
            langRow.transform.SetParent(content, false);
            UiFactory.SizeMenuAction(langRow.GetComponent<RectTransform>(), row);
            var langLayout = langRow.GetComponent<HorizontalLayoutGroup>();
            langLayout.spacing = 10f;
            langLayout.childControlWidth = true;
            langLayout.childControlHeight = true;
            langLayout.childForceExpandWidth = true;
            langLayout.childForceExpandHeight = true;

            _langRu = UiFactory.CreateButton(langRow.transform, GameText.Get("ui.settings.lang_ru"));
            _langRu.onClick.AddListener(() => SelectLanguage(GameLocale.Russian));
            _langEn = UiFactory.CreateButton(langRow.transform, GameText.Get("ui.settings.lang_en"));
            _langEn.onClick.AddListener(() => SelectLanguage(GameLocale.English));
            PaintLanguage();

            UiFactory.AddMenuCaption(content, GameText.Get("ui.settings.nickname"));
            _nickname = CreateNicknameField(content);
            UiFactory.SizeMenuAction(_nickname.GetComponent<RectTransform>(), row);

            _status = UiFactory.CreateLabel(content, "Status", string.Empty, 20, TextAnchor.MiddleCenter);
            _status.color = UIThemeConfig.TextMuted;
            UiFactory.SizeMenuRow(_status, 48f);

            var save = UiFactory.AddMenuAction(content, GameText.Get("ui.settings.save_nickname"), true);
            save.onClick.AddListener(() => _ = SaveNickname());

            var bind = UiFactory.AddMenuAction(content, GameText.Get("ui.settings.bind_device"), false);
            bind.onClick.AddListener(() => _ = BindDevice());

            var export = UiFactory.AddMenuAction(content, GameText.Get("ui.settings.export_data"), false);
            export.onClick.AddListener(() => _ = ExportData());

            var delete = UiFactory.AddMenuAction(content, GameText.Get("ui.settings.delete_account"), false);
            UiFactory.StyleDestructive(delete);
            delete.onClick.AddListener(() => _ = DeleteAccount());

            var note = UiFactory.CreateLabel(content, "BtNote", GameText.Get("ui.settings.bluetooth_note"), 18, TextAnchor.MiddleCenter);
            note.color = UIThemeConfig.TextMuted;
            UiFactory.SizeMenuRow(note, 56f);

            var links = new GameObject("Links", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            links.layer = gameObject.layer;
            links.transform.SetParent(content, false);
            UiFactory.SizeMenuAction(links.GetComponent<RectTransform>(), row);
            var linksLayout = links.GetComponent<HorizontalLayoutGroup>();
            linksLayout.spacing = 10f;
            linksLayout.childControlWidth = true;
            linksLayout.childControlHeight = true;
            linksLayout.childForceExpandWidth = true;
            linksLayout.childForceExpandHeight = true;

            var privacy = UiFactory.CreateButton(links.transform, GameText.Get("ui.settings.privacy"));
            UiFactory.StyleGlass(privacy);
            privacy.onClick.AddListener(() => OpenUrl(YahtzeeConfig.Current.PrivacyPolicyUrl));

            var terms = UiFactory.CreateButton(links.transform, GameText.Get("ui.settings.terms"));
            UiFactory.StyleGlass(terms);
            terms.onClick.AddListener(() => OpenUrl(YahtzeeConfig.Current.TermsOfServiceUrl));

            var back = UiFactory.AddMenuAction(content, GameText.Get("ui.common.back"), false);
            back.onClick.AddListener(ReturnToMenu);

            UiFactory.FinalizeMenuForm(scroll);
        }

        private void SelectLanguage(string locale)
        {
            GameLocale.Set(locale);
            PaintLanguage();
            GameAudio.PlayUiClick();
        }

        private void PaintLanguage()
        {
            if (_langRu != null)
            {
                if (GameLocale.Current == GameLocale.Russian)
                    UiFactory.StyleAccent(_langRu);
                else
                    UiFactory.StyleGlass(_langRu);
            }

            if (_langEn != null)
            {
                if (GameLocale.Current == GameLocale.English)
                    UiFactory.StyleAccent(_langEn);
                else
                    UiFactory.StyleGlass(_langEn);
            }
        }

        private void RefreshFields()
        {
            if (_sfxToggle != null)
                _sfxToggle.isOn = GameAudio.SfxEnabled;
            if (_musicToggle != null)
                _musicToggle.isOn = GameAudio.MusicEnabled;
            PaintLanguage();

            var nickname = GameLocale.Current == GameLocale.Russian ? "Гость" : "Guest";
            if (NetworkManager.Instance != null && NetworkManager.Instance.CurrentUser != null
                && !string.IsNullOrWhiteSpace(NetworkManager.Instance.CurrentUser.Nickname))
                nickname = NetworkManager.Instance.CurrentUser.Nickname;
            else
                nickname = PlayerPrefs.GetString("yahtzee.local.nickname", nickname);

            if (_nickname != null)
                _nickname.text = nickname;
            ScreenFeedback.Clear(_status);
        }

        private async Task SaveNickname()
        {
            if (_busy || _nickname == null)
                return;

            var value = (_nickname.text ?? string.Empty).Trim();
            if (value.Length < 2)
            {
                ScreenFeedback.SetErrorKey(_status, "ui.settings.nickname_short");
                return;
            }

            _busy = true;
            ScreenFeedback.SetInfo(_status, "ui.settings.saving");
            try
            {
                PlayerPrefs.SetString("yahtzee.local.nickname", value);
                PlayerPrefs.Save();
                if (NetworkManager.Instance != null && NetworkManager.Instance.IsAuthenticated)
                {
                    var profile = await NetworkManager.Instance.UpdateProfileNickname(value);
                    if (profile != null)
                        SkinManager.Remember(profile);
                }

                ScreenFeedback.SetInfo(_status, "ui.settings.saved");
                GameAudio.PlayUiClick();
            }
            catch (Exception exception)
            {
                ScreenFeedback.SetError(_status, exception);
            }
            finally
            {
                _busy = false;
            }
        }

        private async Task BindDevice()
        {
            if (_busy || NetworkManager.Instance == null || !NetworkManager.Instance.IsAuthenticated)
            {
                ScreenFeedback.SetErrorKey(_status, "ui.settings.need_online");
                return;
            }

            _busy = true;
            ScreenFeedback.SetInfo(_status, "ui.settings.binding");
            try
            {
                var deviceId = PlayerPrefs.GetString("yahtzee.device.id", string.Empty);
                if (string.IsNullOrWhiteSpace(deviceId) || deviceId.Length < 8)
                {
                    deviceId = "device-" + Guid.NewGuid().ToString("N");
                    PlayerPrefs.SetString("yahtzee.device.id", deviceId);
                }

                var deviceSecret = PlayerPrefs.GetString("yahtzee.device.secret", string.Empty);
                if (string.IsNullOrWhiteSpace(deviceSecret) || deviceSecret.Length < 32)
                {
                    deviceSecret = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
                    PlayerPrefs.SetString("yahtzee.device.secret", deviceSecret);
                }

                PlayerPrefs.Save();
                await NetworkManager.Instance.BindDevice(deviceId, deviceSecret);
                ScreenFeedback.SetInfo(_status, "ui.settings.bind_ok");
                GameAudio.PlayUiClick();
            }
            catch (Exception exception)
            {
                ScreenFeedback.SetError(_status, exception);
            }
            finally
            {
                _busy = false;
            }
        }

        private async Task ExportData()
        {
            if (_busy || NetworkManager.Instance == null || !NetworkManager.Instance.IsAuthenticated)
            {
                ScreenFeedback.SetErrorKey(_status, "ui.settings.need_online");
                return;
            }

            _busy = true;
            ScreenFeedback.SetInfo(_status, "ui.settings.exporting");
            try
            {
                var payload = await NetworkManager.Instance.ExportProfile();
                var path = Path.Combine(Application.persistentDataPath, "yahtzee-export.json");
                File.WriteAllText(path, payload.ToString(Newtonsoft.Json.Formatting.Indented));
                ScreenFeedback.SetInfo(_status, "ui.settings.export_ok");
                Debug.Log("Yahtzee profile export: " + path);
                GameAudio.PlayUiClick();
            }
            catch (Exception exception)
            {
                ScreenFeedback.SetError(_status, exception);
            }
            finally
            {
                _busy = false;
            }
        }

        private async Task DeleteAccount()
        {
            if (_busy || NetworkManager.Instance == null || !NetworkManager.Instance.IsAuthenticated)
            {
                ScreenFeedback.SetErrorKey(_status, "ui.settings.need_online");
                return;
            }

            _busy = true;
            ScreenFeedback.SetInfo(_status, "ui.settings.deleting");
            try
            {
                await NetworkManager.Instance.DeleteAccount();
                PlayerPrefs.DeleteKey(YahtzeeConfig.Current.TokenPrefKey);
                PlayerPrefs.DeleteKey(YahtzeeConfig.Current.ActiveMatchPrefKey);
                PlayerPrefs.Save();
                ScreenFeedback.SetInfo(_status, "ui.settings.delete_ok");
                GameAudio.PlayUiClick();
                if (UIManager.Instance != null)
                    UIManager.Instance.SwitchToScreen(ScreenType.MainMenu);
            }
            catch (Exception exception)
            {
                ScreenFeedback.SetError(_status, exception);
            }
            finally
            {
                _busy = false;
            }
        }

        private Toggle CreateToggleRow(Transform parent, string label, bool on, float height, Action<bool> changed)
        {
            var row = new GameObject(label, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.layer = gameObject.layer;
            row.transform.SetParent(parent, false);
            UiFactory.SizeMenuRow(row.GetComponent<RectTransform>(), height);

            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 12f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;

            var caption = UiFactory.CreateLabel(row.transform, "Label", label, 28, TextAnchor.MiddleLeft);
            caption.color = UIThemeConfig.TextPrimary;
            var toggleGo = new GameObject("Toggle", typeof(RectTransform), typeof(Toggle), typeof(Image), typeof(LayoutElement));
            toggleGo.layer = gameObject.layer;
            toggleGo.transform.SetParent(row.transform, false);
            var toggleLayout = toggleGo.GetComponent<LayoutElement>();
            toggleLayout.preferredWidth = 72f;
            toggleLayout.flexibleWidth = 0f;
            var toggle = toggleGo.GetComponent<Toggle>();
            toggle.isOn = on;
            toggle.onValueChanged.AddListener(value => changed(value));
            var image = toggleGo.GetComponent<Image>();
            image.color = on ? UIThemeConfig.ButtonAccent : UIThemeConfig.BorderSubtle;
            toggle.onValueChanged.AddListener(value =>
            {
                image.color = value ? UIThemeConfig.ButtonAccent : UIThemeConfig.BorderSubtle;
            });
            return toggle;
        }

        private InputField CreateNicknameField(Transform parent)
        {
            var root = new GameObject("Nickname", typeof(RectTransform), typeof(Image), typeof(InputField), typeof(LayoutElement));
            root.layer = gameObject.layer;
            root.transform.SetParent(parent, false);
            var image = root.GetComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0.92f);
            image.sprite = UiFactory.RoundSprite;
            image.type = Image.Type.Sliced;

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textGo.layer = gameObject.layer;
            textGo.transform.SetParent(root.transform, false);
            var text = textGo.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 28;
            text.color = UIThemeConfig.TextPrimary;
            text.alignment = TextAnchor.MiddleLeft;
            text.supportRichText = false;
            var textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(18f, 6f);
            textRect.offsetMax = new Vector2(-18f, -6f);

            var field = root.GetComponent<InputField>();
            field.textComponent = text;
            field.contentType = InputField.ContentType.Standard;
            field.characterLimit = 24;
            return field;
        }

        private static void OpenUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return;
            Application.OpenURL(url);
        }

        private void ReturnToMenu()
        {
            if (UIManager.Instance != null)
                UIManager.Instance.SwitchToScreen(ScreenType.MainMenu);
        }
    }
}
