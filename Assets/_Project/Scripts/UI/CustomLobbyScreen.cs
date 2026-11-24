using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using Yahtzee.Core;
using Yahtzee.Feedback;
using Yahtzee.Localization;
using Yahtzee.Network;

namespace Yahtzee.UI
{
    public sealed class CustomLobbyScreen : UIScreen
    {
        public override ScreenType Type => ScreenType.CustomLobby;

        private static Color SelectedColor => UIThemeConfig.ButtonAccent;
        private static readonly int[] TurnChoices = { 30, 60, 90, 120 };

        private int _players = 2;
        private int _turnSeconds = 60;
        private Text _status;
        private Button _createButton;
        private InputField _roomNameInput;
        private InputField _passwordInput;
        private Button[] _playerButtons;
        private Button[] _timeButtons;
        private bool _busy;
        private bool _built;
        private bool _layoutLandscape = true;

        public override void Show()
        {
            base.Show();
            var rect = (RectTransform)transform;
            UiFactory.EnsureScreenFillsCanvas(rect);
            ApplyOpaqueMenuBackground(rect);
            UiFactory.FitCanvas(GetComponentInParent<Canvas>());

            var landscape = Screen.width > Screen.height;
            if (!_built || landscape != _layoutLandscape || rect.rect.width < 80f)
            {
                Rebuild();
                _built = true;
                _layoutLandscape = landscape;
            }

            RefreshStatus();
            UiMotion.PopIn(transform);
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
            RefreshStatus();
        }

        private static void ApplyOpaqueMenuBackground(RectTransform screen)
        {
            UiFactory.ApplyOpaqueMenuBackground(screen);
        }

        private void Rebuild()
        {
            UiFactory.ClearContent(transform);
            ApplyOpaqueMenuBackground((RectTransform)transform);
            Build();
        }

        private async void CreateLobby()
        {
            if (_busy)
            {
                ScreenFeedback.SetInfo(_status, "ui.lobby.busy_create");
                return;
            }

            if (NetworkManager.Instance == null || !NetworkManager.Instance.IsAuthenticated)
            {
                ScreenFeedback.SetErrorKey(_status, "ui.lobby.no_auth");
                return;
            }

            _busy = true;
            if (_createButton != null)
                _createButton.interactable = false;
            ScreenFeedback.SetInfo(_status, "ui.lobby.creating");

            try
            {
                var password = _passwordInput != null ? _passwordInput.text : null;
                var roomName = _roomNameInput != null ? _roomNameInput.text : null;
                MatchState match;
                try
                {
                    match = await NetworkManager.Instance.CreateLobby(_players, _turnSeconds, password, roomName);
                }
                catch (NetworkException first) when (first.ErrorCode == ApiErrorCode.AlreadyInLobby)
                {
                    // Stale lobby seat from a previous room — leave and retry once.
                    await TryLeaveStaleLobby();
                    match = await NetworkManager.Instance.CreateLobby(_players, _turnSeconds, password, roomName);
                }

                if (this == null || UIManager.Instance == null)
                    return;

                if (match == null || match.Id <= 0)
                {
                    ScreenFeedback.SetErrorKey(_status, "ui.lobby.server_empty");
                    return;
                }

                var list = UIManager.Instance.GetScreen<LobbyListScreen>();
                if (list != null)
                    list.OpenWaitingRoom(match);

                UIManager.Instance.SwitchToScreen(ScreenType.LobbyList);
                list = UIManager.Instance.GetScreen<LobbyListScreen>();
                if (list != null)
                {
                    list.EnterWaitingRoom(match);
                    return;
                }

                ScreenFeedback.SetErrorKey(_status, "ui.lobby.waiting_missing");
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Yahtzee create lobby: " + exception.Message);
                ScreenFeedback.SetError(_status, exception);
            }
            finally
            {
                _busy = false;
                if (_createButton != null)
                    _createButton.interactable = true;
            }
        }

        private static async System.Threading.Tasks.Task TryLeaveStaleLobby()
        {
            var network = NetworkManager.Instance;
            if (network == null)
                return;

            var id = 0;
            if (network.LastLobby != null && network.LastLobby.Id > 0)
                id = network.LastLobby.Id;
            else if (network.ActiveMatchId.HasValue)
                id = network.ActiveMatchId.Value;

            if (id <= 0)
                return;

            try
            {
                await network.LeaveLobby(id.ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception)
            {
                // Best-effort cleanup before create retry.
            }
        }

        private void ReturnToList()
        {
            if (UIManager.Instance != null)
                UIManager.Instance.SwitchToScreen(ScreenType.LobbyList);
        }

        private void SelectPlayers(int count)
        {
            _players = count;
            Paint(_playerButtons, count.ToString(CultureInfo.InvariantCulture));
            RefreshStatus();
        }

        private void SelectTurn(int seconds)
        {
            _turnSeconds = seconds;
            Paint(_timeButtons, seconds.ToString(CultureInfo.InvariantCulture));
            RefreshStatus();
        }

        private void RefreshStatus()
        {
            ScreenFeedback.SetInfo(
                _status,
                "ui.lobby.hint_players",
                _players.ToString(CultureInfo.InvariantCulture),
                _turnSeconds.ToString(CultureInfo.InvariantCulture));
        }

        private static void Paint(Button[] buttons, string selected)
        {
            if (buttons == null)
                return;

            for (var index = 0; index < buttons.Length; index++)
            {
                var button = buttons[index];
                if (button == null)
                    continue;

                if (button.name == selected)
                    UiFactory.StyleAccent(button);
                else
                    UiFactory.StyleGlass(button);
            }
        }

        private void Build()
        {
            var content = UiFactory.BeginScrollForm(transform, out var scroll);
            var row = UiFactory.MenuActionHeight;

            UiFactory.AddMenuTitle(content, "Создать комнату", "Своё лобби · онлайн");
            AddCaption(content, "Название комнаты");
            _roomNameInput = AddLineField(content, "RoomName", "необязательно", InputField.ContentType.Standard);
            _roomNameInput.characterLimit = 32;
            UiFactory.SizeMenuAction(_roomNameInput.GetComponent<RectTransform>(), row);

            AddCaption(content, "Сколько игроков");
            _playerButtons = PlaceChoiceRow(content, new[] { "2", "3", "4" }, row,
                value => SelectPlayers(int.Parse(value, CultureInfo.InvariantCulture)));
            Paint(_playerButtons, "2");

            AddCaption(content, "Секунд на ход");
            var captions = new string[TurnChoices.Length];
            for (var index = 0; index < TurnChoices.Length; index++)
                captions[index] = TurnChoices[index].ToString(CultureInfo.InvariantCulture);
            _timeButtons = PlaceChoiceRow(content, captions, row,
                value => SelectTurn(int.Parse(value, CultureInfo.InvariantCulture)));
            Paint(_timeButtons, "60");

            AddCaption(content, "Пароль комнаты");
            _passwordInput = AddLineField(content, "RoomPassword", "необязательно", InputField.ContentType.Password);
            UiFactory.SizeMenuAction(_passwordInput.GetComponent<RectTransform>(), row);

            _status = UiFactory.CreateLabel(content, "Status", string.Empty, 22, TextAnchor.MiddleCenter);
            _status.horizontalOverflow = HorizontalWrapMode.Wrap;
            _status.verticalOverflow = VerticalWrapMode.Overflow;
            _status.color = UIThemeConfig.TextMuted;
            UiFactory.SizeMenuRow(_status.rectTransform, 56f);

            _createButton = UiFactory.AddMenuAction(content, "Создать комнату", true);
            _createButton.onClick.AddListener(CreateLobby);

            var back = UiFactory.AddMenuAction(content, "К списку комнат", false);
            back.onClick.AddListener(ReturnToList);

            UiFactory.FinalizeMenuForm(scroll);
        }

        private static void AddCaption(Transform parent, string text)
        {
            UiFactory.AddMenuCaption(parent, text);
        }

        private static Button[] PlaceChoiceRow(Transform parent, string[] captions, float height, Action<string> select)
        {
            var row = new GameObject("Choices", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.layer = parent.gameObject.layer;
            row.transform.SetParent(parent, false);
            UiFactory.SizeMenuAction(row.GetComponent<RectTransform>(), height);

            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 10f;
            layout.padding = new RectOffset(0, 0, 0, 0);
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;

            var buttons = new Button[captions.Length];
            for (var index = 0; index < captions.Length; index++)
            {
                var caption = captions[index];
                var button = UiFactory.CreateButton(row.transform, caption);
                button.name = caption;
                UiFactory.StyleGlass(button);
                var chosen = caption;
                button.onClick.AddListener(() => select(chosen));
                buttons[index] = button;
            }

            return buttons;
        }

        private static InputField AddLineField(Transform parent, string name, string placeholder, InputField.ContentType contentType)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(InputField), typeof(LayoutElement));
            root.layer = parent.gameObject.layer;
            root.transform.SetParent(parent, false);
            var image = root.GetComponent<Image>();
            image.sprite = UiFactory.RoundSprite;
            image.type = Image.Type.Sliced;
            image.color = UIThemeConfig.CardBg;

            var text = UiFactory.CreateLabel(root.transform, "Text", string.Empty, 24, TextAnchor.MiddleLeft);
            text.color = UIThemeConfig.TextPrimary;
            text.supportRichText = false;
            Pad(text.rectTransform);

            var hint = UiFactory.CreateLabel(root.transform, "Placeholder", placeholder, 22, TextAnchor.MiddleLeft);
            hint.color = UIThemeConfig.TextMuted;
            hint.fontStyle = FontStyle.Italic;
            Pad(hint.rectTransform);

            var field = root.GetComponent<InputField>();
            field.textComponent = text;
            field.placeholder = hint;
            field.contentType = contentType;
            field.lineType = InputField.LineType.SingleLine;
            return field;
        }

        private static void Pad(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(16f, 8f);
            rect.offsetMax = new Vector2(-16f, -8f);
            rect.localScale = Vector3.one;
        }
    }
}
