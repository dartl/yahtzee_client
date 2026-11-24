using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine;
#elif !(UNITY_WEBGL && !UNITY_EDITOR)
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
#else
using UnityEngine;
#endif
using Newtonsoft.Json;
using Yahtzee.Core;
using Yahtzee.Models;

namespace Yahtzee.Network
{
    public sealed class BluetoothDeviceFound
    {
        public string Name;
        public string Address;
    }

    public sealed class BluetoothDiceRolled
    {
        public int Seat;
        public string PlayerName;
        public int[] Dice;
        public int RollsLeft;
        public bool[] Held;
    }

    public sealed class BluetoothCategorySelected
    {
        public int Seat;
        public string PlayerName;
        public YahtzeeCategory Category;
        public int Score;
        public int NextSeat;
    }

    public sealed class BluetoothNetworkManager : MonoBehaviour
    {
        public const string ServiceUuid = "00001101-0000-1000-8000-00805F9B34FB";
        public const string ServiceName = "Yatzy";

        private const int EmulatedPort = 47821;
        private const int EmulatedDiscoveryPort = 47822;

        private static BluetoothNetworkManager _instance;
        private readonly ConcurrentQueue<Action> _main = new ConcurrentQueue<Action>();
        private readonly Scorecard[] _sheets = { new Scorecard(), new Scorecard() };
        private readonly string[] _names = { string.Empty, string.Empty };
        private readonly System.Random _random = new System.Random();

        private int[] _dice = Array.Empty<int>();
        private int _sessionId;
        private bool _alive;

        public static BluetoothNetworkManager Instance
        {
            get
            {
                if (_instance != null)
                    return _instance;

                var host = new GameObject(nameof(BluetoothNetworkManager));
                _instance = host.AddComponent<BluetoothNetworkManager>();
                return _instance;
            }
        }

        public static bool HasInstance => _instance != null;

        /// <summary>
        /// Soft launch: Bluetooth multiplayer ships on Android devices only.
        /// Editor / iOS / PC / WebGL hide the entry point.
        /// </summary>
        public static bool IsSupported
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }

        public event Action<BluetoothDeviceFound> DeviceFound;
        public event Action<string> StatusChanged;
        public event Action Connected;
        public event Action MatchStarted;
        public event Action<BluetoothDiceRolled> DiceRolled;
        public event Action<BluetoothCategorySelected> CategorySelected;
        public event Action<int, bool[]> DiceHeld;

        public bool InMatch { get; private set; }
        public bool IsHost { get; private set; }
        public bool IsConnected { get; private set; }
        public int SessionId => _sessionId;
        public int LocalSeat { get; private set; }
        public int TurnSeat { get; private set; }
        public int PlayerCount { get; private set; }
        public int RollsLeft { get; private set; }
        public bool IsLocalTurn => InMatch && LocalSeat == TurnSeat;
        public string Status { get; private set; } = "Bluetooth не запущен.";
        public int[] CurrentDice => _dice ?? Array.Empty<int>();
        public Scorecard LocalSheet => SheetForSeat(LocalSeat);

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            StopSession();
            if (_instance == this)
                _instance = null;
        }

        private void Update()
        {
            while (_main.TryDequeue(out var action))
            {
                if (action == null)
                    continue;

                try
                {
                    action();
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("Yahtzee bluetooth: " + exception.Message);
                }
            }
        }

        public Scorecard SheetForSeat(int seat)
        {
            var index = seat >= 0 && seat < _sheets.Length ? seat : 0;
            return _sheets[index];
        }

        public string NameOf(int seat)
        {
            if (seat < 0 || seat >= _names.Length || string.IsNullOrWhiteSpace(_names[seat]))
                return "Игрок " + (seat + 1);

            return _names[seat];
        }

        public void StartBluetoothHost()
        {
            if (!IsSupported)
            {
                SetStatus("Bluetooth недоступен в веб-версии.");
                return;
            }

            StopSession();
            _alive = true;
            IsHost = true;
            LocalSeat = 0;
            PlayerCount = 1;
            _names[0] = DeviceLabel();
            _sheets[0].Reset();
            _sheets[1].Reset();
            SetStatus("Включаю видимость Bluetooth...");
#if UNITY_ANDROID && !UNITY_EDITOR
            BeginAndroidHost();
#elif !(UNITY_WEBGL && !UNITY_EDITOR)
            BeginEmulatedHost();
#endif
        }

        public void DiscoverAndConnectBluetooth()
        {
            if (!IsSupported)
            {
                SetStatus("Bluetooth недоступен в веб-версии.");
                return;
            }

            if (IsHost && IsConnected)
            {
                SetStatus("Это устройство уже ждёт соперника.");
                return;
            }

            _alive = true;
            SetStatus("Ищу устройства Bluetooth...");
#if UNITY_ANDROID && !UNITY_EDITOR
            BeginAndroidDiscovery();
#elif !(UNITY_WEBGL && !UNITY_EDITOR)
            BeginEmulatedDiscovery();
#endif
        }

        public void ConnectBluetooth(string address)
        {
            if (!IsSupported || string.IsNullOrWhiteSpace(address))
                return;

            if (IsConnected)
            {
                SetStatus("Соединение уже установлено.");
                return;
            }

            StopDiscovery();
            _alive = true;
            IsHost = false;
            LocalSeat = 1;
            PlayerCount = 1;
            _names[1] = DeviceLabel();
            SetStatus("Подключение к " + address + "...");
#if UNITY_ANDROID && !UNITY_EDITOR
            BeginAndroidConnect(address.Trim());
#elif !(UNITY_WEBGL && !UNITY_EDITOR)
            BeginEmulatedConnect(address.Trim());
#endif
        }

        public void StopDiscovery()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            StopAndroidDiscovery();
#elif !(UNITY_WEBGL && !UNITY_EDITOR)
            StopEmulatedDiscovery();
#endif
        }

        public void StopSession()
        {
            _alive = false;
            InMatch = false;
            IsConnected = false;
            IsHost = false;
            PlayerCount = 0;
            TurnSeat = 0;
            RollsLeft = 0;
            _dice = Array.Empty<int>();
            StopDiscovery();
#if UNITY_ANDROID && !UNITY_EDITOR
            CloseAndroid();
#elif !(UNITY_WEBGL && !UNITY_EDITOR)
            CloseEmulated();
#endif
        }

        public void SubmitRoll(bool[] keep)
        {
            if (!InMatch || !IsLocalTurn)
                return;

            if (IsHost)
            {
                ApplyRoll(LocalSeat, keep);
                return;
            }

            Send(new Packet { kind = "roll", seat = LocalSeat, keep = keep ?? Array.Empty<bool>() });
        }

        public void SubmitHold(bool[] keep)
        {
            if (!InMatch || !IsLocalTurn)
                return;

            if (IsHost)
            {
                PublishHold(LocalSeat, keep);
                return;
            }

            Send(new Packet { kind = "hold", seat = LocalSeat, keep = keep ?? Array.Empty<bool>() });
        }

        public bool SubmitCategory(YahtzeeCategory category)
        {
            if (!InMatch || !IsLocalTurn)
                return false;

            if (IsHost)
                return ApplyScore(LocalSeat, category);

            Send(new Packet { kind = "score", seat = LocalSeat, category = (int)category });
            return true;
        }

        private void OpenMatch()
        {
            _sheets[0].Reset();
            _sheets[1].Reset();
            _dice = Array.Empty<int>();
            TurnSeat = 0;
            RollsLeft = YahtzeeConfig.Current.RollsPerTurn;
            PlayerCount = 2;
            InMatch = true;
            IsConnected = true;
            _sessionId++;
            if (string.IsNullOrWhiteSpace(_names[0]))
                _names[0] = "Хост";
            if (string.IsNullOrWhiteSpace(_names[1]))
                _names[1] = "Гость";

            Send(new Packet
            {
                kind = "start",
                seat = 0,
                name = _names[0],
                next = TurnSeat,
                rollsLeft = RollsLeft
            });
            SetStatus("Партия началась.");
            MatchStarted?.Invoke();
        }

        private void ApplyRoll(int seat, bool[] keep)
        {
            if (!InMatch || seat != TurnSeat || RollsLeft <= 0)
                return;

            var count = DiceGameEngine.DiceCount;
            if (_dice == null || _dice.Length != count)
                _dice = new int[count];

            var throwing = 0;
            var kept = new bool[count];
            for (var index = 0; index < count; index++)
            {
                var held = keep != null && index < keep.Length && keep[index] && RollsLeft < YahtzeeConfig.Current.RollsPerTurn;
                kept[index] = held;
                if (held)
                    continue;

                throwing++;
                _dice[index] = _random.Next(1, 7);
            }

            if (throwing == 0)
                return;

            RollsLeft--;
            var rolled = new BluetoothDiceRolled
            {
                Seat = seat,
                PlayerName = NameOf(seat),
                Dice = (int[])_dice.Clone(),
                RollsLeft = RollsLeft,
                Held = kept
            };
            Send(new Packet
            {
                kind = "BluetoothDiceRolled",
                seat = seat,
                name = rolled.PlayerName,
                dice = rolled.Dice,
                rollsLeft = RollsLeft,
                held = kept
            });
            DiceRolled?.Invoke(rolled);
        }

        private bool ApplyScore(int seat, YahtzeeCategory category)
        {
            if (!InMatch || seat != TurnSeat || _dice == null || _dice.Length != DiceGameEngine.DiceCount)
                return false;

            var score = DiceGameEngine.CalculateScore(category, _dice, _sheets[seat]);
            if (!_sheets[seat].RegisterScore(category, score, _dice))
                return false;

            var finished = _sheets[0].IsFullyCompleted() && _sheets[1].IsFullyCompleted();
            _dice = Array.Empty<int>();
            TurnSeat = finished ? -1 : 1 - seat;
            RollsLeft = finished ? 0 : YahtzeeConfig.Current.RollsPerTurn;
            var selected = new BluetoothCategorySelected
            {
                Seat = seat,
                PlayerName = NameOf(seat),
                Category = category,
                Score = score,
                NextSeat = TurnSeat
            };
            Send(new Packet
            {
                kind = "BluetoothCategorySelected",
                seat = seat,
                name = selected.PlayerName,
                category = (int)category,
                score = score,
                next = TurnSeat,
                rollsLeft = RollsLeft
            });
            CategorySelected?.Invoke(selected);
            if (finished)
                SetStatus("Партия окончена.");
            return true;
        }

        private void PublishHold(int seat, bool[] keep)
        {
            if (!InMatch || seat != TurnSeat || _dice == null || _dice.Length != DiceGameEngine.DiceCount)
                return;

            if (RollsLeft >= YahtzeeConfig.Current.RollsPerTurn)
                return;

            var mask = new bool[DiceGameEngine.DiceCount];
            if (keep != null)
            {
                var length = keep.Length < mask.Length ? keep.Length : mask.Length;
                Array.Copy(keep, mask, length);
            }

            Send(new Packet { kind = "hold", seat = seat, keep = mask });
            if (seat != LocalSeat)
                DiceHeld?.Invoke(seat, mask);
        }

        private void Receive(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            Packet packet;
            try
            {
                packet = JsonConvert.DeserializeObject<Packet>(line);
            }
            catch (Exception)
            {
                return;
            }

            if (packet == null || string.IsNullOrEmpty(packet.kind))
                return;

            if (IsHost)
            {
                switch (packet.kind)
                {
                    case "hello":
                        _names[1] = string.IsNullOrWhiteSpace(packet.name) ? "Гость" : packet.name.Trim();
                        PlayerCount = 2;
                        IsConnected = true;
                        Send(new Packet { kind = "welcome", seat = 1, name = _names[0], next = 1 });
                        SetStatus("Подключился " + _names[1] + ".");
                        Connected?.Invoke();
                        OpenMatch();
                        break;
                    case "roll":
                        ApplyRoll(1, packet.keep);
                        break;
                    case "hold":
                        PublishHold(1, packet.keep);
                        break;
                    case "score":
                        if (Enum.IsDefined(typeof(YahtzeeCategory), packet.category))
                            ApplyScore(1, (YahtzeeCategory)packet.category);
                        break;
                }

                return;
            }

            switch (packet.kind)
            {
                case "welcome":
                    LocalSeat = 1;
                    PlayerCount = 2;
                    if (!string.IsNullOrWhiteSpace(packet.name))
                        _names[0] = packet.name.Trim();
                    IsConnected = true;
                    SetStatus("Сопряжение установлено.");
                    Connected?.Invoke();
                    break;
                case "start":
                    _names[0] = string.IsNullOrWhiteSpace(packet.name) ? _names[0] : packet.name.Trim();
                    TurnSeat = 0;
                    RollsLeft = packet.rollsLeft > 0 ? packet.rollsLeft : YahtzeeConfig.Current.RollsPerTurn;
                    _dice = Array.Empty<int>();
                    _sheets[0].Reset();
                    _sheets[1].Reset();
                    InMatch = true;
                    PlayerCount = 2;
                    _sessionId++;
                    SetStatus("Партия началась.");
                    MatchStarted?.Invoke();
                    break;
                case "BluetoothDiceRolled":
                    _dice = packet.dice ?? Array.Empty<int>();
                    RollsLeft = packet.rollsLeft;
                    TurnSeat = packet.seat;
                    DiceRolled?.Invoke(new BluetoothDiceRolled
                    {
                        Seat = packet.seat,
                        PlayerName = NameOf(packet.seat),
                        Dice = CurrentDice,
                        RollsLeft = RollsLeft,
                        Held = packet.held
                    });
                    break;
                case "BluetoothCategorySelected":
                    if (packet.seat >= 0 && packet.seat < _sheets.Length && _dice != null && _dice.Length == DiceGameEngine.DiceCount)
                        _sheets[packet.seat].RegisterScore((YahtzeeCategory)packet.category, packet.score);
                    _dice = Array.Empty<int>();
                    TurnSeat = packet.next;
                    RollsLeft = packet.rollsLeft;
                    CategorySelected?.Invoke(new BluetoothCategorySelected
                    {
                        Seat = packet.seat,
                        PlayerName = NameOf(packet.seat),
                        Category = (YahtzeeCategory)packet.category,
                        Score = packet.score,
                        NextSeat = packet.next
                    });
                    break;
                case "hold":
                    if (packet.seat != LocalSeat)
                        DiceHeld?.Invoke(packet.seat, packet.keep ?? Array.Empty<bool>());
                    break;
            }
        }

        private void Send(Packet packet)
        {
            if (packet == null)
                return;

            if (!IsConnected && packet.kind != "hello")
                return;

            string line;
            try
            {
                line = JsonConvert.SerializeObject(packet);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Yahtzee bluetooth: " + exception.Message);
                return;
            }

            TransportSend(line);
        }

        private void RaiseFound(string name, string address)
        {
            if (string.IsNullOrWhiteSpace(address))
                return;

            DeviceFound?.Invoke(new BluetoothDeviceFound
            {
                Name = string.IsNullOrWhiteSpace(name) ? address : name.Trim(),
                Address = address.Trim()
            });
        }

        private void SetStatus(string status)
        {
            Status = status ?? string.Empty;
            StatusChanged?.Invoke(Status);
        }

        private void Post(Action action)
        {
            if (action != null)
                _main.Enqueue(action);
        }

        private static string DeviceLabel()
        {
            var name = SystemInfo.deviceName;
            return string.IsNullOrWhiteSpace(name) ? "Yatzy" : name.Trim();
        }

        private sealed class Packet
        {
            public string kind;
            public string name;
            public int seat;
            public int[] dice;
            public int rollsLeft;
            public int category;
            public int score;
            public int next;
            public bool[] keep;
            public bool[] held;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject _adapter;
        private AndroidJavaObject _serverSocket;
        private AndroidJavaObject _socket;
        private AndroidJavaObject _input;
        private AndroidJavaObject _output;
        private AndroidJavaProxy _receiver;
        private AndroidJavaObject _activity;
        private bool _receiverRegistered;
        private readonly HashSet<string> _seenRadios = new HashSet<string>();
        private readonly object _radioGate = new object();
        private readonly object _writeGate = new object();

        private void BeginAndroidHost()
        {
            if (!PrepareAdapter(true))
                return;

            try
            {
                AskDiscoverable();
            }
            catch (Exception exception)
            {
                SetStatus("Не удалось открыть видимость: " + exception.Message);
                return;
            }

            var thread = new System.Threading.Thread(AcceptLoop) { IsBackground = true, Name = "YatzyBluetoothAccept" };
            thread.Start();
        }

        private void BeginAndroidDiscovery()
        {
            if (!PrepareAdapter(false))
                return;

            _seenRadios.Clear();
            try
            {
                RegisterDiscovery();
                ListBonded();
                _adapter.Call<bool>("startDiscovery");
                SetStatus("Сканирование Bluetooth. Выберите имя устройства.");
            }
            catch (Exception exception)
            {
                SetStatus("Поиск не запустился: " + exception.Message);
            }
        }

        private void BeginAndroidConnect(string address)
        {
            if (!PrepareAdapter(false))
                return;

            var thread = new System.Threading.Thread(() => ConnectLoop(address))
            {
                IsBackground = true,
                Name = "YatzyBluetoothConnect"
            };
            thread.Start();
        }

        private bool PrepareAdapter(bool advertise)
        {
            try
            {
                _activity = CurrentActivity();
                _adapter = ReadAdapter(_activity);
                if (_adapter == null)
                {
                    SetStatus("На устройстве нет Bluetooth.");
                    return false;
                }

                if (!EnsurePermissions(advertise))
                    return false;

                if (!_adapter.Call<bool>("isEnabled"))
                {
                    AskEnable();
                    SetStatus("Включите Bluetooth и повторите.");
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                SetStatus("Bluetooth недоступен: " + exception.Message);
                return false;
            }
        }

        private static AndroidJavaObject CurrentActivity()
        {
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                return player.GetStatic<AndroidJavaObject>("currentActivity");
        }

        private static AndroidJavaObject ReadAdapter(AndroidJavaObject activity)
        {
            if (activity != null)
            {
                var manager = activity.Call<AndroidJavaObject>("getSystemService", "bluetooth");
                if (manager != null)
                {
                    var modern = manager.Call<AndroidJavaObject>("getAdapter");
                    if (modern != null)
                        return modern;
                }
            }

            using (var klass = new AndroidJavaClass("android.bluetooth.BluetoothAdapter"))
                return klass.CallStatic<AndroidJavaObject>("getDefaultAdapter");
        }

        private bool EnsurePermissions(bool advertise)
        {
            var sdk = 0;
            using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                sdk = version.GetStatic<int>("SDK_INT");

            if (sdk < 31)
            {
                if (sdk < 23)
                    return true;

                const string location = "android.permission.ACCESS_FINE_LOCATION";
                if (UnityEngine.Android.Permission.HasUserAuthorizedPermission(location))
                    return true;

                UnityEngine.Android.Permission.RequestUserPermission(location);
                SetStatus("Разрешите геолокацию для поиска Bluetooth и повторите.");
                return false;
            }

            var needed = new List<string>
            {
                "android.permission.BLUETOOTH_CONNECT",
                "android.permission.BLUETOOTH_SCAN"
            };
            if (advertise)
                needed.Add("android.permission.BLUETOOTH_ADVERTISE");

            var missing = false;
            for (var index = 0; index < needed.Count; index++)
            {
                if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(needed[index]))
                    missing = true;
            }

            if (!missing)
                return true;

            UnityEngine.Android.Permission.RequestUserPermissions(needed.ToArray());
            SetStatus("Разрешите Bluetooth в системном окне и повторите.");
            return false;
        }

        private void AskEnable()
        {
            using (var intent = new AndroidJavaObject("android.content.Intent", "android.bluetooth.adapter.action.REQUEST_ENABLE"))
                _activity.Call("startActivity", intent);
        }

        private void AskDiscoverable()
        {
            using (var intent = new AndroidJavaObject("android.content.Intent", "android.bluetooth.adapter.action.REQUEST_DISCOVERABLE"))
            {
                intent.Call<AndroidJavaObject>("putExtra", "android.bluetooth.adapter.extra.DISCOVERABLE_DURATION", 300);
                _activity.Call("startActivity", intent);
            }

            SetStatus("Подтвердите видимость. Устройство ждёт соперника.");
        }

        private void AcceptLoop()
        {
            AndroidJavaObject server = null;
            try
            {
                using (var uuidClass = new AndroidJavaClass("java.util.UUID"))
                {
                    var uuid = uuidClass.CallStatic<AndroidJavaObject>("fromString", ServiceUuid);
                    server = _adapter.Call<AndroidJavaObject>("listenUsingRfcommWithServiceRecord", ServiceName, uuid);
                    uuid.Dispose();
                }
                _serverSocket = server;
                var socket = server.Call<AndroidJavaObject>("accept");
                if (!_alive || socket == null)
                {
                    if (socket != null)
                    {
                        socket.Call("close");
                        socket.Dispose();
                    }

                    return;
                }

                var input = socket.Call<AndroidJavaObject>("getInputStream");
                var output = socket.Call<AndroidJavaObject>("getOutputStream");
                Post(() => AttachStreams(socket, input, output));
                ReadAndroid(input);
            }
            catch (Exception exception)
            {
                if (_alive)
                    Post(() => SetStatus("Ожидание прервано: " + exception.Message));
            }
        }

        private void ConnectLoop(string address)
        {
            try
            {
                try
                {
                    _adapter.Call<bool>("cancelDiscovery");
                }
                catch (Exception)
                {
                }

                var device = _adapter.Call<AndroidJavaObject>("getRemoteDevice", address);
                AndroidJavaObject socket;
                using (var uuidClass = new AndroidJavaClass("java.util.UUID"))
                {
                    var uuid = uuidClass.CallStatic<AndroidJavaObject>("fromString", ServiceUuid);
                    socket = device.Call<AndroidJavaObject>("createRfcommSocketToServiceRecord", uuid);
                    uuid.Dispose();
                }
                socket.Call("connect");
                var input = socket.Call<AndroidJavaObject>("getInputStream");
                var output = socket.Call<AndroidJavaObject>("getOutputStream");
                Post(() =>
                {
                    AttachStreams(socket, input, output);
                    Send(new Packet { kind = "hello", name = DeviceLabel() });
                    SetStatus("Канал открыт, ждём партию.");
                });
                ReadAndroid(input);
            }
            catch (Exception exception)
            {
                Post(() => SetStatus("Не удалось подключиться: " + exception.Message));
            }
        }

        private void AttachStreams(AndroidJavaObject socket, AndroidJavaObject input, AndroidJavaObject output)
        {
            _socket = socket;
            _input = input;
            _output = output;
            IsConnected = true;
        }

        private void ReadAndroid(AndroidJavaObject input)
        {
            var buffer = new List<byte>(256);
            try
            {
                while (_alive)
                {
                    var value = input.Call<int>("read");
                    if (value < 0)
                        break;

                    if (value == '\n')
                    {
                        if (buffer.Count > 0 && buffer[buffer.Count - 1] == '\r')
                            buffer.RemoveAt(buffer.Count - 1);

                        if (buffer.Count > 0)
                        {
                            var line = Encoding.UTF8.GetString(buffer.ToArray());
                            buffer.Clear();
                            Post(() => Receive(line));
                        }

                        continue;
                    }

                    buffer.Add((byte)value);
                }
            }
            catch (Exception exception)
            {
                if (_alive)
                    Post(() => SetStatus("Связь прервана: " + exception.Message));
            }
        }

        private void RegisterDiscovery()
        {
            StopAndroidDiscovery();
            var proxy = new DiscoveryProxy(this);
            _receiver = proxy;
            using (var filter = new AndroidJavaObject("android.content.IntentFilter"))
            {
                filter.Call("addAction", "android.bluetooth.device.action.FOUND");
                filter.Call("addAction", "android.bluetooth.adapter.action.DISCOVERY_FINISHED");
                var sdk = 0;
                using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                    sdk = version.GetStatic<int>("SDK_INT");

                if (sdk >= 33)
                    _activity.Call<AndroidJavaObject>("registerReceiver", _receiver, filter, 2);
                else
                    _activity.Call<AndroidJavaObject>("registerReceiver", _receiver, filter);

                _receiverRegistered = true;
            }
        }

        private void ListBonded()
        {
            var bonded = _adapter.Call<AndroidJavaObject>("getBondedDevices");
            if (bonded == null)
                return;

            var iterator = bonded.Call<AndroidJavaObject>("iterator");
            if (iterator == null)
                return;

            while (iterator.Call<bool>("hasNext"))
            {
                var device = iterator.Call<AndroidJavaObject>("next");
                if (device == null)
                    continue;

                NoteDevice(device.Call<string>("getName"), device.Call<string>("getAddress"));
            }
        }

        private void NoteDevice(string name, string address)
        {
            if (string.IsNullOrWhiteSpace(address))
                return;

            lock (_radioGate)
            {
                if (!_seenRadios.Add(address))
                    return;
            }

            var label = name;
            var radio = address;
            Post(() => RaiseFound(label, radio));
        }

        private void StopAndroidDiscovery()
        {
            try
            {
                if (_adapter != null)
                    _adapter.Call<bool>("cancelDiscovery");
            }
            catch (Exception)
            {
            }

            if (!_receiverRegistered || _activity == null || _receiver == null)
                return;

            try
            {
                _activity.Call("unregisterReceiver", _receiver);
            }
            catch (Exception)
            {
            }

            _receiverRegistered = false;
            _receiver = null;
        }

        private void CloseAndroid()
        {
            StopAndroidDiscovery();
            CloseJava(_input);
            CloseJava(_output);
            CloseJava(_socket);
            CloseJava(_serverSocket);
            _input = null;
            _output = null;
            _socket = null;
            _serverSocket = null;
        }

        private static void CloseJava(AndroidJavaObject target)
        {
            if (target == null)
                return;

            try
            {
                target.Call("close");
            }
            catch (Exception)
            {
            }

            target.Dispose();
        }

        private void TransportSend(string line)
        {
            var output = _output;
            if (output == null || string.IsNullOrEmpty(line))
                return;

            try
            {
                lock (_writeGate)
                {
                    using (var text = new AndroidJavaObject("java.lang.String", line + "\n"))
                    {
                        var bytes = text.Call<sbyte[]>("getBytes", "UTF-8");
                        output.Call("write", bytes, 0, bytes.Length);
                        output.Call("flush");
                    }
                }
            }
            catch (Exception exception)
            {
                Post(() => SetStatus("Не удалось отправить ход: " + exception.Message));
            }
        }

        private sealed class DiscoveryProxy : AndroidJavaProxy
        {
            private readonly BluetoothNetworkManager _owner;

            public DiscoveryProxy(BluetoothNetworkManager owner)
                : base("android.content.BroadcastReceiver")
            {
                _owner = owner;
            }

            public void onReceive(AndroidJavaObject context, AndroidJavaObject intent)
            {
                if (intent == null || _owner == null)
                    return;

                var action = intent.Call<string>("getAction");
                if (action == "android.bluetooth.adapter.action.DISCOVERY_FINISHED")
                {
                    _owner.Post(() => _owner.SetStatus("Поиск завершён. Выберите устройство из списка."));
                    return;
                }

                if (action != "android.bluetooth.device.action.FOUND")
                    return;

                var device = intent.Call<AndroidJavaObject>("getParcelableExtra", "android.bluetooth.device.extra.DEVICE");
                if (device == null)
                    return;

                _owner.NoteDevice(device.Call<string>("getName"), device.Call<string>("getAddress"));
            }
        }
#elif UNITY_WEBGL && !UNITY_EDITOR
        private void TransportSend(string line)
        {
        }
#else
        private TcpListener _listener;
        private TcpClient _link;
        private NetworkStream _stream;
        private UdpClient _beacon;
        private UdpClient _scan;
        private System.Threading.Thread _acceptThread;
        private System.Threading.Thread _readThread;
        private System.Threading.Thread _scanThread;
        private readonly object _writeGate = new object();

        private void BeginEmulatedHost()
        {
            try
            {
                _listener = new TcpListener(IPAddress.Any, EmulatedPort);
                _listener.Start();
                StartBeacon();
                _acceptThread = new System.Threading.Thread(AcceptEmulated)
                {
                    IsBackground = true,
                    Name = "YatzyBluetoothEmulatedAccept"
                };
                _acceptThread.Start();
                SetStatus("Эмуляция Bluetooth. Порт " + EmulatedPort + ". Устройство видно в поиске.");
            }
            catch (Exception exception)
            {
                SetStatus("Не удалось открыть локальный порт: " + exception.Message);
            }
        }

        private void BeginEmulatedDiscovery()
        {
            StopEmulatedDiscovery();
            try
            {
                _scan = new UdpClient(EmulatedDiscoveryPort);
                _scanThread = new System.Threading.Thread(ScanEmulated)
                {
                    IsBackground = true,
                    Name = "YatzyBluetoothEmulatedScan"
                };
                _scanThread.Start();
                SetStatus("Ищу устройства на локальном порту...");
            }
            catch (Exception exception)
            {
                SetStatus("Поиск недоступен: " + exception.Message);
            }
        }

        private void BeginEmulatedConnect(string address)
        {
            var host = address;
            var port = EmulatedPort;
            var colon = address.LastIndexOf(':');
            if (colon > 0 && int.TryParse(address.Substring(colon + 1), out var parsed))
            {
                host = address.Substring(0, colon);
                port = parsed;
            }

            var thread = new System.Threading.Thread(() => ConnectEmulated(host, port))
            {
                IsBackground = true,
                Name = "YatzyBluetoothEmulatedConnect"
            };
            thread.Start();
        }

        private void AcceptEmulated()
        {
            try
            {
                var client = _listener.AcceptTcpClient();
                if (!_alive)
                {
                    client.Close();
                    return;
                }

                client.NoDelay = true;
                Post(() =>
                {
                    AdoptLink(client);
                    SetStatus("Соперник подключился по локальному порту.");
                });
            }
            catch (Exception exception)
            {
                if (_alive)
                    Post(() => SetStatus("Ожидание прервано: " + exception.Message));
            }
        }

        private void ConnectEmulated(string host, int port)
        {
            try
            {
                var client = new TcpClient();
                client.NoDelay = true;
                client.Connect(host, port);
                Post(() =>
                {
                    AdoptLink(client);
                    Send(new Packet { kind = "hello", name = DeviceLabel() });
                    SetStatus("Канал открыт, ждём партию.");
                });
            }
            catch (Exception exception)
            {
                Post(() => SetStatus("Не удалось подключиться: " + exception.Message));
            }
        }

        private void AdoptLink(TcpClient client)
        {
            _link = client;
            _stream = client.GetStream();
            IsConnected = true;
            _readThread = new System.Threading.Thread(() => ReadEmulated(_stream))
            {
                IsBackground = true,
                Name = "YatzyBluetoothEmulatedRead"
            };
            _readThread.Start();
        }

        private void ReadEmulated(NetworkStream stream)
        {
            var buffer = new List<byte>(256);
            var one = new byte[1];
            try
            {
                while (_alive)
                {
                    var read = stream.Read(one, 0, 1);
                    if (read <= 0)
                        break;

                    if (one[0] == (byte)'\n')
                    {
                        if (buffer.Count > 0 && buffer[buffer.Count - 1] == (byte)'\r')
                            buffer.RemoveAt(buffer.Count - 1);

                        if (buffer.Count > 0)
                        {
                            var line = Encoding.UTF8.GetString(buffer.ToArray());
                            buffer.Clear();
                            Post(() => Receive(line));
                        }

                        continue;
                    }

                    buffer.Add(one[0]);
                }
            }
            catch (Exception exception)
            {
                if (_alive)
                    Post(() => SetStatus("Связь прервана: " + exception.Message));
            }
        }

        private void StartBeacon()
        {
            StopEmulatedDiscovery();
            _beacon = new UdpClient();
            _beacon.EnableBroadcast = true;
            var payload = Encoding.UTF8.GetBytes("YATZY-BT|" + DeviceLabel() + "|" + EmulatedPort);
            var end = new IPEndPoint(IPAddress.Broadcast, EmulatedDiscoveryPort);
            var thread = new System.Threading.Thread(() =>
            {
                while (_alive && _beacon != null)
                {
                    try
                    {
                        _beacon.Send(payload, payload.Length, end);
                    }
                    catch (Exception)
                    {
                        return;
                    }

                    System.Threading.Thread.Sleep(800);
                }
            })
            {
                IsBackground = true,
                Name = "YatzyBluetoothBeacon"
            };
            thread.Start();
        }

        private void ScanEmulated()
        {
            var client = _scan;
            if (client == null)
                return;

            var remote = new IPEndPoint(IPAddress.Any, 0);
            try
            {
                while (_alive && _scan != null)
                {
                    var bytes = client.Receive(ref remote);
                    var text = Encoding.UTF8.GetString(bytes);
                    var parts = text.Split('|');
                    if (parts.Length < 3 || parts[0] != "YATZY-BT")
                        continue;

                    var port = EmulatedPort;
                    int.TryParse(parts[2], out port);
                    if (port <= 0)
                        port = EmulatedPort;
                    var address = remote.Address + ":" + port;
                    var label = parts[1];
                    Post(() => RaiseFound(label, address));
                }
            }
            catch (Exception)
            {
                if (_alive)
                    Post(() => SetStatus("Поиск завершён."));
            }
        }

        private void StopEmulatedDiscovery()
        {
            var scan = _scan;
            _scan = null;
            if (scan != null)
                scan.Close();
        }

        private void CloseEmulated()
        {
            StopEmulatedDiscovery();
            var beacon = _beacon;
            _beacon = null;
            if (beacon != null)
                beacon.Close();

            var stream = _stream;
            _stream = null;
            if (stream != null)
                stream.Close();

            var link = _link;
            _link = null;
            if (link != null)
                link.Close();

            var listener = _listener;
            _listener = null;
            if (listener != null)
                listener.Stop();
        }

        private void TransportSend(string line)
        {
            var stream = _stream;
            if (stream == null || string.IsNullOrEmpty(line))
                return;

            var bytes = Encoding.UTF8.GetBytes(line + "\n");
            try
            {
                lock (_writeGate)
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush();
                }
            }
            catch (Exception exception)
            {
                Post(() => SetStatus("Не удалось отправить ход: " + exception.Message));
            }
        }
#endif
    }
}
