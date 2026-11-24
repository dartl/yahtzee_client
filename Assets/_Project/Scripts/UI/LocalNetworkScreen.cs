using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Yahtzee.Core;
using Yahtzee.Network;

namespace Yahtzee.UI
{
    public sealed class LocalNetworkScreen : UIScreen
    {
        public override ScreenType Type => ScreenType.LocalNetwork;

        [SerializeField] private Button startHostButton;
        [SerializeField] private TMP_InputField ipInputField;
        [SerializeField] private Button connectManualButton;
        [SerializeField] private Button scanNetworkButton;
        [SerializeField] private Transform serverListContainer;
        [SerializeField] private GameObject serverRowPrefab;
        [SerializeField] private Button backToMenuButton;
        [SerializeField] private Button startMatchButton;
        [SerializeField] private TextMeshProUGUI statusText;

        private readonly HashSet<string> _seenAddresses = new HashSet<string>();
        private bool _wired;
        private bool _bluetoothMode;
        private bool _openBluetooth;
        private Button _bluetoothModeButton;

        public void Prepare(bool bluetooth)
        {
            _openBluetooth = bluetooth && BluetoothNetworkManager.IsSupported;
        }

        private void Awake()
        {
            UiFactory.ApplyOpaqueMenuBackground((RectTransform)transform);
            EnsureWidgets();
            Wire();
        }

        public override void Show()
        {
            UiFactory.EnsureScreenFillsCanvas((RectTransform)transform);
            UiFactory.ApplyOpaqueMenuBackground((RectTransform)transform);
            EnsureWidgets();
            Wire();
            if (!BluetoothNetworkManager.IsSupported)
                _openBluetooth = false;
            if (_bluetoothMode != _openBluetooth)
                ReleaseIdleTransport(_bluetoothMode);
            _bluetoothMode = _openBluetooth;
            base.Show();
            ClearServerList();
            ApplyTransportLabels();
            if (_bluetoothMode)
                WriteStatus("Режим Bluetooth 1v1. Станьте видимым или найдите устройство рядом.");
            else if (!LocalNetworkManager.IsSupported)
                WriteStatus("Локальная сеть недоступна в веб-версии.");
            else
                WriteStatus("Создайте комнату или найдите устройства рядом.");

            RefreshStartButton();
        }

        public override void Hide()
        {
            if (LocalNetworkManager.HasInstance)
                LocalNetworkManager.Instance.StopDiscovery();
            if (BluetoothNetworkManager.HasInstance && !BluetoothNetworkManager.Instance.InMatch)
                BluetoothNetworkManager.Instance.StopDiscovery();

            base.Hide();
        }

        private void OnEnable()
        {
            var table = LocalNetworkManager.Instance;
            table.StatusChanged += OnStatusChanged;
            table.LocalPlayerJoined += OnPlayerJoined;
            table.LocalHostDiscovered += OnHostDiscovered;
            table.MatchStarted += OnMatchStarted;
            if (!BluetoothNetworkManager.IsSupported)
                return;

            var radio = BluetoothNetworkManager.Instance;
            radio.DeviceFound += OnBluetoothFound;
            radio.StatusChanged += OnBluetoothStatus;
            radio.MatchStarted += OnMatchStarted;
        }

        private void OnDisable()
        {
            if (!LocalNetworkManager.HasInstance)
                return;

            var table = LocalNetworkManager.Instance;
            table.StatusChanged -= OnStatusChanged;
            table.LocalPlayerJoined -= OnPlayerJoined;
            table.LocalHostDiscovered -= OnHostDiscovered;
            table.MatchStarted -= OnMatchStarted;
            if (!BluetoothNetworkManager.HasInstance)
                return;

            var radio = BluetoothNetworkManager.Instance;
            radio.DeviceFound -= OnBluetoothFound;
            radio.StatusChanged -= OnBluetoothStatus;
            radio.MatchStarted -= OnMatchStarted;
        }

        private void OnBluetoothMode()
        {
            if (!BluetoothNetworkManager.IsSupported)
                return;

            _bluetoothMode = !_bluetoothMode;
            ClearServerList();
            if (_bluetoothMode)
            {
                if (LocalNetworkManager.HasInstance)
                {
                    var table = LocalNetworkManager.Instance;
                    table.StopDiscovery();
                    if (!table.InMatch)
                        table.StopSession();
                }

                if (BluetoothNetworkManager.HasInstance)
                    BluetoothNetworkManager.Instance.StopDiscovery();
            }
            else
            {
                if (BluetoothNetworkManager.HasInstance)
                {
                    var radio = BluetoothNetworkManager.Instance;
                    radio.StopDiscovery();
                    if (!radio.InMatch)
                        radio.StopSession();
                }

                if (LocalNetworkManager.HasInstance)
                    LocalNetworkManager.Instance.StopDiscovery();
            }

            ApplyTransportLabels();
            if (_bluetoothMode)
                WriteStatus("Режим Bluetooth 1v1. Станьте видимым или найдите устройство рядом.");
            else if (LocalNetworkManager.IsSupported)
                WriteStatus("Создайте комнату или найдите устройства рядом.");
            else
                WriteStatus("Локальная сеть недоступна в веб-версии.");
            RefreshStartButton();
        }

        private void OnStartHost()
        {
            if (_bluetoothMode)
            {
                PassAndPlayManager.Close();
                if (LocalNetworkManager.HasInstance)
                    LocalNetworkManager.Instance.StopSession();
                ClearServerList();
                WriteStatus("Открываю Bluetooth...");
                BluetoothNetworkManager.Instance.StartBluetoothHost();
                return;
            }

            if (!LocalNetworkManager.IsSupported)
            {
                WriteStatus("Локальная сеть недоступна в веб-версии.");
                return;
            }

            PassAndPlayManager.Close();
            ClearServerList();
            WriteStatus("Открываю комнату...");
            var table = LocalNetworkManager.Instance;
            table.StartLocalHost();
            if (!table.IsHost)
                return;

            WriteStatus("Ждём игроков.\nВведи мой IP: " + table.LocalAddress + "\nКогда все зайдут, нажми «Начать».");
            RefreshStartButton();
        }

        private void OnConnectManual()
        {
            if (!LocalNetworkManager.IsSupported)
            {
                WriteStatus("Локальная сеть недоступна в веб-версии.");
                return;
            }

            var address = ipInputField != null ? ipInputField.text : string.Empty;
            if (string.IsNullOrWhiteSpace(address))
            {
                WriteStatus("Введите IP-адрес.");
                return;
            }

            ConnectTo(address.Trim());
        }

        private void OnScan()
        {
            if (_bluetoothMode)
            {
                ClearServerList();
                WriteStatus("Ищу устройства Bluetooth...");
                BluetoothNetworkManager.Instance.DiscoverAndConnectBluetooth();
                return;
            }

            if (!LocalNetworkManager.IsSupported)
            {
                WriteStatus("Локальная сеть недоступна в веб-версии.");
                return;
            }

            ClearServerList();
            WriteStatus("Ищу устройства...");
            LocalNetworkManager.Instance.StartDiscovery();
        }

        private void OnBack()
        {
            if (LocalNetworkManager.HasInstance)
            {
                var table = LocalNetworkManager.Instance;
                table.StopDiscovery();
                if (!table.InMatch)
                    table.StopSession();
            }

            if (BluetoothNetworkManager.HasInstance)
            {
                var radio = BluetoothNetworkManager.Instance;
                radio.StopDiscovery();
                if (!radio.InMatch)
                    radio.StopSession();
            }

            if (UIManager.Instance != null)
                UIManager.Instance.SwitchToScreen(ScreenType.OfflineMenu);
        }

        private void ReleaseIdleTransport(bool bluetooth)
        {
            if (bluetooth)
            {
                if (!BluetoothNetworkManager.HasInstance)
                    return;

                var radio = BluetoothNetworkManager.Instance;
                radio.StopDiscovery();
                if (!radio.InMatch)
                    radio.StopSession();
                return;
            }

            if (!LocalNetworkManager.HasInstance)
                return;

            var table = LocalNetworkManager.Instance;
            table.StopDiscovery();
            if (!table.InMatch)
                table.StopSession();
        }

        private void OnStatusChanged(string status)
        {
            if (!isActiveAndEnabled || _bluetoothMode)
                return;

            var table = LocalNetworkManager.Instance;
            if (table.IsHost && !table.InMatch && !string.IsNullOrEmpty(table.LocalAddress) &&
                status != null && status.StartsWith("Комната открыта"))
            {
                WriteStatus("Ждём игроков.\nВведи мой IP: " + table.LocalAddress + "\nКогда все зайдут, нажми «Начать».");
                RefreshStartButton();
                return;
            }

            if (!table.IsHost && !table.InMatch && status != null && status.StartsWith("Вы в комнате"))
            {
                WriteStatus(status + "\nЖдём, пока создатель нажмёт «Начать».");
                RefreshStartButton();
                return;
            }

            WriteStatus(string.IsNullOrWhiteSpace(status) ? "Ошибка связи." : status);
            RefreshStartButton();
        }

        private void OnPlayerJoined(LocalPlayerJoined joined)
        {
            if (_bluetoothMode || !isActiveAndEnabled || joined == null || !LocalNetworkManager.HasInstance)
                return;

            var table = LocalNetworkManager.Instance;
            RefreshStartButton();
            if (!table.IsHost || table.InMatch)
                return;

            WriteStatus("В комнате " + table.PlayerCount + " из " + LocalNetworkManager.MaxPlayers +
                        ".\nВведи мой IP: " + table.LocalAddress +
                        "\nКогда все зайдут, нажми «Начать».");
        }

        private void OnStartMatch()
        {
            if (!LocalNetworkManager.HasInstance)
                return;

            var table = LocalNetworkManager.Instance;
            if (!table.IsHost || table.InMatch)
                return;

            if (table.PlayerCount < 2)
            {
                WriteStatus("Нужен ещё хотя бы один игрок.\nВведи мой IP: " + table.LocalAddress);
                RefreshStartButton();
                return;
            }

            table.StartMatch();
        }

        private void OnHostDiscovered(LocalHostFound found)
        {
            if (_bluetoothMode || !isActiveAndEnabled || found == null || string.IsNullOrWhiteSpace(found.Address))
                return;

            if (!_seenAddresses.Add(found.Address))
                return;

            SpawnServerRow(found);
        }

        private void OnMatchStarted()
        {
            if (!isActiveAndEnabled || UIManager.Instance == null)
                return;

            UIManager.Instance.SwitchToScreen(ScreenType.Gameplay);
        }

        private void ConnectTo(string address)
        {
            PassAndPlayManager.Close();
            WriteStatus("Подключение...");
            LocalNetworkManager.Instance.ConnectToLocalHost(address);
        }

        private void SpawnServerRow(LocalHostFound found)
        {
            if (serverRowPrefab == null || serverListContainer == null)
                return;

            var row = Instantiate(serverRowPrefab, serverListContainer);
            row.SetActive(true);
            row.transform.localScale = Vector3.one;
            var label = row.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null)
                label.text = found.Name + "\n" + found.Address + "\nВойти";

            var button = row.GetComponent<Button>();
            if (button == null)
                button = row.GetComponentInChildren<Button>(true);

            if (button == null)
                return;

            var address = found.Address;
            var bluetooth = _bluetoothMode;
            button.onClick.AddListener(() =>
            {
                if (bluetooth)
                    ConnectBluetooth(address);
                else
                    ConnectTo(address);
            });
        }

        private void OnBluetoothFound(BluetoothDeviceFound found)
        {
            if (!_bluetoothMode || !isActiveAndEnabled || found == null || string.IsNullOrWhiteSpace(found.Address))
                return;

            if (!_seenAddresses.Add("bt:" + found.Address))
                return;

            SpawnServerRow(new LocalHostFound
            {
                Name = found.Name,
                Address = found.Address
            });
        }

        private void OnBluetoothStatus(string status)
        {
            if (!_bluetoothMode || !isActiveAndEnabled)
                return;

            WriteStatus(string.IsNullOrWhiteSpace(status) ? "Ошибка Bluetooth." : status);
        }

        private void ConnectBluetooth(string address)
        {
            PassAndPlayManager.Close();
            if (LocalNetworkManager.HasInstance)
                LocalNetworkManager.Instance.StopSession();
            WriteStatus("Подключение по Bluetooth...");
            BluetoothNetworkManager.Instance.ConnectBluetooth(address);
        }

        private void ClearServerList()
        {
            _seenAddresses.Clear();
            if (serverListContainer == null)
                return;

            for (var index = serverListContainer.childCount - 1; index >= 0; index--)
                Destroy(serverListContainer.GetChild(index).gameObject);
        }

        private void WriteStatus(string message)
        {
            if (statusText != null)
                statusText.text = message;
        }

        private void Wire()
        {
            if (_wired)
                return;

            _wired = true;
            if (startHostButton != null)
                startHostButton.onClick.AddListener(OnStartHost);
            if (connectManualButton != null)
                connectManualButton.onClick.AddListener(OnConnectManual);
            if (scanNetworkButton != null)
                scanNetworkButton.onClick.AddListener(OnScan);
            if (backToMenuButton != null)
                backToMenuButton.onClick.AddListener(OnBack);
            if (startMatchButton != null)
                startMatchButton.onClick.AddListener(OnStartMatch);
            if (_bluetoothModeButton != null)
                _bluetoothModeButton.onClick.AddListener(OnBluetoothMode);
        }

        private void EnsureWidgets()
        {
            if (statusText == null)
            {
                statusText = UiFactory.CreateAutoLabel(transform, "Status", string.Empty, TextAlignmentOptions.Center);
                statusText.textWrappingMode = TextWrappingModes.Normal;
                UiFactory.Place(statusText.rectTransform, 0.08f, 0.78f, 0.92f, 0.9f);
            }

            if (startHostButton == null)
            {
                startHostButton = UiFactory.CreateButton(transform, "Создать комнату");
                UiFactory.StyleAccent(startHostButton);
                UiFactory.Place(startHostButton.GetComponent<RectTransform>(), 0.12f, 0.66f, 0.88f, 0.76f);
            }

            if (ipInputField == null)
                ipInputField = CreateIpField();

            if (connectManualButton == null)
            {
                connectManualButton = UiFactory.CreateButton(transform, "Подключиться по IP");
                UiFactory.StyleAccent(connectManualButton);
                UiFactory.Place(connectManualButton.GetComponent<RectTransform>(), 0.12f, 0.46f, 0.88f, 0.55f);
            }

            if (scanNetworkButton == null)
            {
                scanNetworkButton = UiFactory.CreateButton(transform, "Искать устройства");
                UiFactory.StyleGlass(scanNetworkButton);
                UiFactory.Place(scanNetworkButton.GetComponent<RectTransform>(), 0.12f, 0.35f, 0.88f, 0.44f);
            }

            if (serverListContainer == null)
                serverListContainer = CreateServerList();

            if (serverRowPrefab == null)
                serverRowPrefab = CreateServerRowPrefab();

            if (BluetoothNetworkManager.IsSupported && _bluetoothModeButton == null)
            {
                _bluetoothModeButton = UiFactory.CreateButton(transform, "Режим Bluetooth 1v1");
                UiFactory.Place(_bluetoothModeButton.GetComponent<RectTransform>(), 0.12f, 0.91f, 0.88f, 0.98f);
                UiFactory.StyleGlass(_bluetoothModeButton);
            }

            if (backToMenuButton == null)
            {
                backToMenuButton = UiFactory.CreateButton(transform, "Назад");
                UiFactory.StyleGlass(backToMenuButton);
                UiFactory.Place(backToMenuButton.GetComponent<RectTransform>(), 0.22f, 0.03f, 0.78f, 0.11f);
            }

            if (startMatchButton == null)
            {
                startMatchButton = UiFactory.CreateButton(transform, "Начать");
                UiFactory.StyleAccent(startMatchButton);
                UiFactory.Place(startMatchButton.GetComponent<RectTransform>(), 0.12f, 0.13f, 0.88f, 0.23f);
            }

            ApplyTransportLabels();
            RefreshStartButton();
        }

        private void ApplyTransportLabels()
        {
            SetButtonCaption(startHostButton, _bluetoothMode ? "Сделать видимым (Хост)" : "Создать комнату");
            SetButtonCaption(scanNetworkButton, _bluetoothMode ? "Поиск устройств" : "Искать устройства");
            SetButtonCaption(_bluetoothModeButton, _bluetoothMode ? "Режим Wi-Fi" : "Режим Bluetooth 1v1");
            if (_bluetoothModeButton != null)
            {
                _bluetoothModeButton.gameObject.SetActive(BluetoothNetworkManager.IsSupported);
                if (_bluetoothMode)
                    UiFactory.StyleAccent(_bluetoothModeButton);
                else
                    UiFactory.StyleGlass(_bluetoothModeButton);
            }

            if (ipInputField != null)
                ipInputField.gameObject.SetActive(!_bluetoothMode);
            if (connectManualButton != null)
                connectManualButton.gameObject.SetActive(!_bluetoothMode);
        }

        private static void SetButtonCaption(Button button, string caption)
        {
            if (button == null)
                return;

            var label = button.GetComponentInChildren<Text>(true);
            if (label != null)
                label.text = caption;
        }

        private void RefreshStartButton()
        {
            if (startMatchButton == null)
                return;

            var table = LocalNetworkManager.HasInstance ? LocalNetworkManager.Instance : null;
            var hosting = !_bluetoothMode && table != null && table.IsHost && !table.InMatch;
            startMatchButton.gameObject.SetActive(hosting);
            startMatchButton.interactable = hosting && table.PlayerCount >= 2;
            if (hosting)
                startMatchButton.transform.SetAsLastSibling();
        }

        private TMP_InputField CreateIpField()
        {
            var fieldObject = new GameObject("IpInput", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
            fieldObject.layer = gameObject.layer;
            fieldObject.transform.SetParent(transform, false);
            fieldObject.GetComponent<Image>().color = UIThemeConfig.CardBg;
            var rect = fieldObject.GetComponent<RectTransform>();
            UiFactory.Place(rect, 0.12f, 0.56f, 0.88f, 0.65f);

            var text = UiFactory.CreateAutoLabel(fieldObject.transform, "Text", string.Empty, TextAlignmentOptions.MidlineLeft);
            text.raycastTarget = false;
            UiFactory.Stretch(text.rectTransform);
            text.rectTransform.offsetMin = new Vector2(16f, 6f);
            text.rectTransform.offsetMax = new Vector2(-16f, -6f);

            var placeholder = UiFactory.CreateAutoLabel(fieldObject.transform, "Placeholder", "IP, например 192.168.1.5", TextAlignmentOptions.MidlineLeft);
            placeholder.fontStyle = FontStyles.Italic;
            placeholder.color = new Color(0.7f, 0.7f, 0.66f, 0.75f);
            placeholder.raycastTarget = false;
            UiFactory.Stretch(placeholder.rectTransform);
            placeholder.rectTransform.offsetMin = new Vector2(16f, 6f);
            placeholder.rectTransform.offsetMax = new Vector2(-16f, -6f);

            var field = fieldObject.GetComponent<TMP_InputField>();
            field.textViewport = rect;
            field.textComponent = text;
            field.placeholder = placeholder;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.contentType = TMP_InputField.ContentType.Standard;
            return field;
        }

        private Transform CreateServerList()
        {
            var listObject = new GameObject("ServerList", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            listObject.layer = gameObject.layer;
            listObject.transform.SetParent(transform, false);
            var rect = listObject.GetComponent<RectTransform>();
            UiFactory.Place(rect, 0.08f, 0.13f, 0.92f, 0.34f);
            var layout = listObject.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = listObject.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return listObject.transform;
        }

        private GameObject CreateServerRowPrefab()
        {
            var row = new GameObject("ServerRowPrefab", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            row.layer = gameObject.layer;
            row.transform.SetParent(transform, false);
            row.GetComponent<Image>().color = new Color(0.16f, 0.28f, 0.22f, 1f);
            var element = row.GetComponent<LayoutElement>();
            element.minHeight = 64f;
            element.preferredHeight = 72f;
            element.flexibleWidth = 1f;
            var label = UiFactory.CreateAutoLabel(row.transform, "Label", "Войти", TextAlignmentOptions.Center);
            label.textWrappingMode = TextWrappingModes.Normal;
            UiFactory.Stretch(label.rectTransform);
            row.SetActive(false);
            return row;
        }
    }
}
