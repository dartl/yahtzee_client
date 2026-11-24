using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
#if !(UNITY_WEBGL && !UNITY_EDITOR)
using System.Net;
using System.Net.Sockets;
using System.Threading;
#endif
using Newtonsoft.Json;
using UnityEngine;
using Yahtzee.Core;
using Yahtzee.Models;

namespace Yahtzee.Network
{
    public sealed class LocalDiceRolled
    {
        public int Seat;
        public string PlayerName;
        public int[] Dice;
        public int RollsLeft;
        public bool[] Held;
    }

    public sealed class LocalCategorySelected
    {
        public int Seat;
        public string PlayerName;
        public YahtzeeCategory Category;
        public int Score;
        public int NextSeat;
    }

    public sealed class LocalPlayerJoined
    {
        public int Seat;
        public string PlayerName;
        public int PlayerCount;
    }

    public sealed class LocalHostFound
    {
        public string Name;
        public string Address;
        public int Port;
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    public sealed class LocalNetworkManager : MonoBehaviour
    {
        public const int MaxPlayers = 4;

        private static LocalNetworkManager _instance;

        public static LocalNetworkManager Instance
        {
            get
            {
                if (_instance != null)
                    return _instance;

                var host = new GameObject(nameof(LocalNetworkManager));
                _instance = host.AddComponent<LocalNetworkManager>();
                return _instance;
            }
        }

        public static bool HasInstance => _instance != null;
        public static bool IsSupported => false;

        public event Action<LocalDiceRolled> LocalDiceRolled;
        public event Action<LocalCategorySelected> LocalCategorySelected;
        public event Action<LocalPlayerJoined> LocalPlayerJoined;
        public event Action MatchStarted;
        public event Action<string> StatusChanged;
        public event Action<LocalHostFound> LocalHostDiscovered;
        public event Action<int, bool[]> LocalDiceHeld;

        public bool InMatch => false;
        public bool IsHost => false;
        public int SessionId => 0;
        public int LocalSeat => 0;
        public int TurnSeat => 0;
        public int PlayerCount => 0;
        public int RollsLeft => 0;
        public bool IsLocalTurn => false;
        public string Status { get; private set; } = "Локальная сеть недоступна в веб-версии.";
        public string LocalAddress => string.Empty;
        public int[] CurrentDice => Array.Empty<int>();
        public Scorecard LocalSheet => SheetForSeat(0);

        private readonly Scorecard[] _sheets = { new Scorecard(), new Scorecard(), new Scorecard(), new Scorecard() };

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
            if (_instance == this)
                _instance = null;
        }

        public Scorecard SheetForSeat(int seat)
        {
            var index = seat >= 0 && seat < _sheets.Length ? seat : 0;
            return _sheets[index];
        }

        public string NameOf(int seat)
        {
            return "Игрок " + (seat + 1);
        }

        public void StartLocalHost()
        {
            Status = "Локальная сеть недоступна в веб-версии.";
            StatusChanged?.Invoke(Status);
        }

        public void ConnectToLocalHost(string targetIp)
        {
            Status = "Локальная сеть недоступна в веб-версии.";
            StatusChanged?.Invoke(Status);
        }

        public void StartMatch()
        {
            Status = "Локальная сеть недоступна в веб-версии.";
            StatusChanged?.Invoke(Status);
        }

        public void SubmitRoll(bool[] keep)
        {
        }

        public void SubmitHold(bool[] keep)
        {
        }

        public bool SubmitCategory(YahtzeeCategory category)
        {
            return false;
        }

        public void StopSession()
        {
        }

        public void StartDiscovery()
        {
            Status = "Локальная сеть недоступна в веб-версии.";
            StatusChanged?.Invoke(Status);
        }

        public void StopDiscovery()
        {
        }
    }
#else
    public sealed class LocalNetworkManager : MonoBehaviour
    {
        public const int MaxPlayers = 4;

        private const int BufferSize = 1024;

        private static LocalNetworkManager _instance;

        private readonly List<Peer> _peers = new List<Peer>();
        private readonly Scorecard[] _sheets = new Scorecard[MaxPlayers];
        private readonly string[] _names = new string[MaxPlayers];
        private readonly object _writeGate = new object();
        private readonly System.Random _random = new System.Random();

        private SynchronizationContext _main;
        private TcpListener _listener;
        private UdpClient _beacon;
        private int _discoveryGeneration;
        private TcpClient _serverLink;
        private StreamWriter _serverWriter;
        private int _sessionId;
        private int _playerCount = 1;
        private int _turnSeat;
        private int _rollsLeft;
        private int[] _dice = Array.Empty<int>();
        private bool _listening;
        private bool _started;

        public static LocalNetworkManager Instance
        {
            get
            {
                if (_instance != null)
                    return _instance;

                var host = new GameObject(nameof(LocalNetworkManager));
                _instance = host.AddComponent<LocalNetworkManager>();
                return _instance;
            }
        }

        public static bool HasInstance => _instance != null;

        public static bool IsSupported
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return false;
#else
                return true;
#endif
            }
        }

        public event Action<LocalDiceRolled> LocalDiceRolled;
        public event Action<LocalCategorySelected> LocalCategorySelected;
        public event Action<LocalPlayerJoined> LocalPlayerJoined;
        public event Action MatchStarted;
        public event Action<string> StatusChanged;
        public event Action<LocalHostFound> LocalHostDiscovered;
        public event Action<int, bool[]> LocalDiceHeld;

        public bool InMatch => _started;
        public bool IsHost { get; private set; }
        public int SessionId => _sessionId;
        public int LocalSeat { get; private set; }
        public int TurnSeat => _turnSeat;
        public int PlayerCount => _playerCount;
        public int RollsLeft => _rollsLeft;
        public bool IsLocalTurn => _started && _turnSeat == LocalSeat;
        public string Status { get; private set; } = string.Empty;
        public string LocalAddress { get; private set; } = "127.0.0.1";

        public int[] CurrentDice
        {
            get
            {
                if (_dice == null || _dice.Length == 0)
                    return Array.Empty<int>();

                var copy = new int[_dice.Length];
                Array.Copy(_dice, copy, _dice.Length);
                return copy;
            }
        }

        public Scorecard LocalSheet => SheetForSeat(LocalSeat);

        private int Port => YahtzeeConfig.Current.LocalTablePort;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            if (transform.parent != null)
                transform.SetParent(null, false);

            DontDestroyOnLoad(gameObject);
            _main = SynchronizationContext.Current;
            for (var index = 0; index < _sheets.Length; index++)
                _sheets[index] = new Scorecard();
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;

            CloseSockets();
        }

        public Scorecard SheetForSeat(int seat)
        {
            if (seat < 0 || seat >= MaxPlayers)
                return _sheets[0];

            return _sheets[seat];
        }

        public string NameOf(int seat)
        {
            if (seat < 0 || seat >= _playerCount || string.IsNullOrWhiteSpace(_names[seat]))
                return "Игрок " + (seat + 1);

            return _names[seat];
        }

        public void StartLocalHost()
        {
            if (!IsSupported)
            {
                SetStatus("Локальная сеть недоступна в веб-версии.");
                return;
            }

            Application.runInBackground = true;
            StopSession();
            IsHost = true;
            LocalSeat = 0;
            _playerCount = 1;
            _names[0] = DeviceName("Хост");
            _started = false;
            LocalAddress = FindLocalAddress();
#if UNITY_WEBGL && !UNITY_EDITOR
            SetStatus("Локальная сеть недоступна в веб-версии.");
#else
            try
            {
                _listener = new TcpListener(IPAddress.Any, Port);
                _listener.Start();
                _listening = true;
                ThreadPool.QueueUserWorkItem(_ => AcceptLoop());
                StartBeacon();
                SetStatus("Комната открыта. Адрес: " + LocalAddress + ":" + Port);
                RaiseJoined(0, _names[0], _playerCount);
            }
            catch (Exception exception)
            {
                SetStatus("Не удалось открыть комнату: " + exception.Message);
                CloseSockets();
            }
#endif
        }

        public void ConnectToLocalHost(string targetIp)
        {
            if (!IsSupported)
            {
                SetStatus("Локальная сеть недоступна в веб-версии.");
                return;
            }

            var address = ParseAddress(targetIp);
            if (address == null)
            {
                SetStatus("Введите адрес создателя комнаты.");
                return;
            }

            Application.runInBackground = true;
            StopSession();
            IsHost = false;
            _started = false;
#if UNITY_WEBGL && !UNITY_EDITOR
            SetStatus("Локальная сеть недоступна в веб-версии.");
#else
            ThreadPool.QueueUserWorkItem(_ => ConnectLoop(address));
#endif
        }

        public void StartMatch()
        {
            if (!IsHost || _started || _playerCount < 2)
            {
                SetStatus("Нужен ещё хотя бы один игрок рядом.");
                return;
            }

            for (var index = 0; index < MaxPlayers; index++)
                _sheets[index].Reset();

            _dice = Array.Empty<int>();
            _turnSeat = 0;
            _rollsLeft = YahtzeeConfig.Current.RollsPerTurn;
            _started = true;
            _sessionId++;
            Broadcast(new WireMessage
            {
                kind = "start",
                next = _turnSeat,
                rollsLeft = _rollsLeft,
                count = _playerCount,
                players = SnapshotPlayers()
            });
            SetStatus("Партия началась.");
            MatchStarted?.Invoke();
        }

        public void SubmitRoll(bool[] keep)
        {
            if (!_started)
                return;

            if (IsHost)
            {
                ApplyRoll(LocalSeat, keep);
                return;
            }

            SendToHost(new WireMessage
            {
                kind = "roll",
                seat = LocalSeat,
                keep = keep ?? Array.Empty<bool>()
            });
        }

        public void SubmitHold(bool[] keep)
        {
            if (!_started || !IsLocalTurn)
                return;

            if (IsHost)
            {
                ApplyHold(LocalSeat, keep);
                return;
            }

            SendToHost(new WireMessage
            {
                kind = "hold",
                seat = LocalSeat,
                keep = keep ?? Array.Empty<bool>()
            });
        }

        public bool SubmitCategory(YahtzeeCategory category)
        {
            if (!_started)
                return false;

            if (IsHost)
                return ApplyScore(LocalSeat, category);

            SendToHost(new WireMessage
            {
                kind = "score",
                seat = LocalSeat,
                category = (int)category
            });
            return true;
        }

        public void StartDiscovery()
        {
            if (!IsSupported)
            {
                SetStatus("Локальная сеть недоступна в веб-версии.");
                return;
            }

            var generation = ++_discoveryGeneration;
            SetStatus("Ищу устройства...");
#if !(UNITY_WEBGL && !UNITY_EDITOR)
            ThreadPool.QueueUserWorkItem(_ => DiscoverLoop(generation));
#endif
        }

        public void StopDiscovery()
        {
            _discoveryGeneration++;
        }

        public void StopSession()
        {
            _discoveryGeneration++;
            _started = false;
            _listening = false;
            IsHost = false;
            _playerCount = 1;
            _turnSeat = 0;
            _rollsLeft = 0;
            _dice = Array.Empty<int>();
            CloseSockets();
        }

#if !(UNITY_WEBGL && !UNITY_EDITOR)
        private void AcceptLoop()
        {
            while (_listening && _listener != null)
            {
                TcpClient client;
                try
                {
                    client = _listener.AcceptTcpClient();
                }
                catch (Exception)
                {
                    break;
                }

                if (!_listening)
                {
                    client.Close();
                    break;
                }

                client.NoDelay = true;
                AdoptGuest(client);
            }
        }

        private void ConnectLoop(string address)
        {
            try
            {
                var client = new TcpClient();
                client.NoDelay = true;
                client.Connect(address, Port);
                Post(() => CompleteJoin(client));
            }
            catch (Exception exception)
            {
                Post(() => SetStatus("Нет соединения с " + address + ": " + exception.Message));
            }
        }

        private void AdoptGuest(TcpClient client)
        {
            if (_started || _playerCount >= MaxPlayers)
            {
                client.Close();
                SetStatus(_started ? "Партия уже идёт." : "Комната заполнена.");
                return;
            }

            var seat = _playerCount;
            var peer = OpenPeer(client, seat);
            peer.Welcomed = true;
            _peers.Add(peer);
            _playerCount++;
            var guestName = "Игрок " + (seat + 1);
            _names[seat] = guestName;
            var playerCount = _playerCount;
            ThreadPool.QueueUserWorkItem(_ => ReadLoop(peer));
            Send(peer.Writer, new WireMessage
            {
                kind = "welcome",
                seat = seat,
                name = guestName,
                count = playerCount,
                players = SnapshotPlayers()
            });
            Broadcast(new WireMessage
            {
                kind = "joined",
                seat = seat,
                name = guestName,
                count = playerCount,
                players = SnapshotPlayers()
            });
            Post(() =>
            {
                SetStatus("Подключился " + _names[seat] + ". В комнате: " + playerCount);
                RaiseJoined(seat, _names[seat], playerCount);
            });
        }

        private void CompleteJoin(TcpClient client)
        {
            _serverLink = client;
            var stream = client.GetStream();
            _serverWriter = new StreamWriter(stream, new UTF8Encoding(false), BufferSize, true) { AutoFlush = true };
            var peer = new Peer
            {
                Client = client,
                Writer = _serverWriter,
                Seat = -1
            };
            ThreadPool.QueueUserWorkItem(_ => ReadLoop(peer));
            Send(_serverWriter, new WireMessage { kind = "hello", name = DeviceName("Гость") });
            SetStatus("Соединение установлено, ждём место в комнате.");
        }

        private void ReadLoop(Peer peer)
        {
            try
            {
                using (var reader = new StreamReader(peer.Client.GetStream(), Encoding.UTF8, false, BufferSize, true))
                {
                    while (true)
                    {
                        var line = reader.ReadLine();
                        if (line == null)
                            break;

                        if (line.Length == 0)
                            continue;

                        var message = JsonConvert.DeserializeObject<WireMessage>(line);
                        if (message == null)
                            continue;

                        Post(() => HandleMessage(peer, message));
                    }
                }
            }
            catch (Exception exception)
            {
                Post(() => SetStatus("Связь прервана: " + exception.Message));
            }
            finally
            {
                Post(() => DropPeer(peer));
            }
        }
#endif

        private void HandleMessage(Peer peer, WireMessage message)
        {
            if (message == null || string.IsNullOrEmpty(message.kind))
                return;

            if (!IsHost)
            {
                HandleGuestMessage(message);
                return;
            }

            switch (message.kind)
            {
                case "hello":
                    WelcomeGuest(peer, message.name);
                    break;
                case "roll":
                    ApplyRoll(peer.Seat, message.keep);
                    break;
                case "hold":
                    ApplyHold(message.seat >= 0 ? message.seat : peer.Seat, message.keep);
                    break;
                case "score":
                    if (Enum.IsDefined(typeof(YahtzeeCategory), message.category))
                        ApplyScore(peer.Seat, (YahtzeeCategory)message.category);
                    break;
            }
        }

        private void HandleGuestMessage(WireMessage message)
        {
            switch (message.kind)
            {
                case "welcome":
                    LocalSeat = message.seat;
                    _playerCount = message.count > 0 ? message.count : 1;
                    ApplyRoster(message.players);
                    SetStatus("Вы в комнате. Место " + (LocalSeat + 1) + " из " + _playerCount);
                    RaiseJoined(LocalSeat, NameOf(LocalSeat), _playerCount);
                    break;
                case "joined":
                    _playerCount = message.count > 0 ? message.count : _playerCount;
                    ApplyRoster(message.players);
                    if (message.seat >= 0 && message.seat < MaxPlayers && !string.IsNullOrWhiteSpace(message.name))
                        _names[message.seat] = message.name;
                    RaiseJoined(message.seat, NameOf(message.seat), _playerCount);
                    break;
                case "start":
                    ApplyRoster(message.players);
                    BeginMirror(message.next, message.rollsLeft);
                    MatchStarted?.Invoke();
                    break;
                case "dice":
                    _dice = message.dice ?? Array.Empty<int>();
                    _rollsLeft = message.rollsLeft;
                    _turnSeat = message.seat;
                    LocalDiceRolled?.Invoke(new LocalDiceRolled
                    {
                        Seat = message.seat,
                        PlayerName = NameOf(message.seat),
                        Dice = CurrentDice,
                        RollsLeft = _rollsLeft,
                        Held = message.keep
                    });
                    break;
                case "hold":
                    LocalDiceHeld?.Invoke(message.seat, message.keep ?? Array.Empty<bool>());
                    break;
                case "scored":
                    ApplyRemoteScore(message);
                    break;
                case "turn":
                    _dice = Array.Empty<int>();
                    _turnSeat = message.next;
                    _rollsLeft = message.rollsLeft;
                    LocalCategorySelected?.Invoke(new LocalCategorySelected
                    {
                        Seat = -1,
                        PlayerName = string.Empty,
                        Category = YahtzeeCategory.Chance,
                        Score = 0,
                        NextSeat = message.next
                    });
                    break;
            }
        }

        private void WelcomeGuest(Peer peer, string requestedName)
        {
            if (peer == null || peer.Seat < 0 || peer.Seat >= MaxPlayers)
                return;

            if (!string.IsNullOrWhiteSpace(requestedName))
                _names[peer.Seat] = requestedName.Trim();

            if (peer.Welcomed)
            {
                SetStatus("Подключился " + _names[peer.Seat] + ". В комнате: " + _playerCount);
                return;
            }

            peer.Welcomed = true;
            Send(peer.Writer, new WireMessage
            {
                kind = "welcome",
                seat = peer.Seat,
                name = _names[peer.Seat],
                count = _playerCount,
                players = SnapshotPlayers()
            });
            Broadcast(new WireMessage
            {
                kind = "joined",
                seat = peer.Seat,
                name = _names[peer.Seat],
                count = _playerCount,
                players = SnapshotPlayers()
            });
            SetStatus("Подключился " + _names[peer.Seat] + ". В комнате: " + _playerCount);
            RaiseJoined(peer.Seat, _names[peer.Seat], _playerCount);
        }

        private void ApplyHold(int seat, bool[] keep)
        {
            if (!_started || seat != _turnSeat || _dice == null || _dice.Length != DiceGameEngine.DiceCount)
                return;

            if (_rollsLeft >= YahtzeeConfig.Current.RollsPerTurn)
                return;

            var mask = new bool[DiceGameEngine.DiceCount];
            if (keep != null)
            {
                var length = keep.Length < mask.Length ? keep.Length : mask.Length;
                Array.Copy(keep, mask, length);
            }

            Broadcast(new WireMessage
            {
                kind = "hold",
                seat = seat,
                keep = mask
            });
            LocalDiceHeld?.Invoke(seat, mask);
        }

        private void ApplyRoll(int seat, bool[] keep)
        {
            if (!_started || seat != _turnSeat || _rollsLeft <= 0)
                return;

            var count = DiceGameEngine.DiceCount;
            if (_dice == null || _dice.Length != count)
                _dice = new int[count];

            var throwing = 0;
            var kept = new bool[count];
            for (var index = 0; index < count; index++)
            {
                var held = keep != null && index < keep.Length && keep[index] && _rollsLeft < YahtzeeConfig.Current.RollsPerTurn;
                kept[index] = held;
                if (held)
                    continue;

                throwing++;
                _dice[index] = _random.Next(1, 7);
            }

            if (throwing == 0)
                return;

            _rollsLeft--;
            var rolled = new LocalDiceRolled
            {
                Seat = seat,
                PlayerName = NameOf(seat),
                Dice = CurrentDice,
                RollsLeft = _rollsLeft,
                Held = kept
            };
            Broadcast(new WireMessage
            {
                kind = "dice",
                seat = seat,
                dice = rolled.Dice,
                rollsLeft = _rollsLeft,
                keep = kept
            });
            LocalDiceRolled?.Invoke(rolled);
        }

        private bool ApplyScore(int seat, YahtzeeCategory category)
        {
            if (!_started || seat != _turnSeat || _dice == null || _dice.Length != DiceGameEngine.DiceCount)
                return false;

            var score = DiceGameEngine.CalculateScore(category, _dice, _sheets[seat]);
            if (!_sheets[seat].RegisterScore(category, score, _dice))
                return false;

            var finished = EveryoneFinished();
            _dice = Array.Empty<int>();
            _turnSeat = finished ? -1 : NextOpenSeat(seat);
            _rollsLeft = finished ? 0 : YahtzeeConfig.Current.RollsPerTurn;
            var selected = new LocalCategorySelected
            {
                Seat = seat,
                PlayerName = NameOf(seat),
                Category = category,
                Score = score,
                NextSeat = _turnSeat
            };
            Broadcast(new WireMessage
            {
                kind = "scored",
                seat = seat,
                category = (int)category,
                score = score,
                next = _turnSeat,
                rollsLeft = _rollsLeft
            });
            LocalCategorySelected?.Invoke(selected);
            if (finished)
                SetStatus("Партия окончена.");

            return true;
        }

        private void ApplyRemoteScore(WireMessage message)
        {
            if (message.seat >= 0 && message.seat < MaxPlayers && Enum.IsDefined(typeof(YahtzeeCategory), message.category))
            {
                var category = (YahtzeeCategory)message.category;
                var sheet = _sheets[message.seat];
                if (!sheet.Scores.TryGetValue(category, out var existing) || !existing.HasValue)
                    sheet.RegisterScore(category, message.score);
            }

            _dice = Array.Empty<int>();
            _turnSeat = message.next;
            _rollsLeft = message.rollsLeft > 0 ? message.rollsLeft : _turnSeat >= 0 ? YahtzeeConfig.Current.RollsPerTurn : 0;
            LocalCategorySelected?.Invoke(new LocalCategorySelected
            {
                Seat = message.seat,
                PlayerName = NameOf(message.seat),
                Category = Enum.IsDefined(typeof(YahtzeeCategory), message.category)
                    ? (YahtzeeCategory)message.category
                    : YahtzeeCategory.Chance,
                Score = message.score,
                NextSeat = message.next
            });
        }

        private void BeginMirror(int turn, int rollsLeft)
        {
            for (var index = 0; index < MaxPlayers; index++)
                _sheets[index].Reset();

            _dice = Array.Empty<int>();
            _turnSeat = turn;
            _rollsLeft = rollsLeft > 0 ? rollsLeft : YahtzeeConfig.Current.RollsPerTurn;
            _started = true;
            _sessionId++;
            SetStatus("Партия началась.");
        }

        private void ApplyRoster(WirePlayer[] players)
        {
            if (players == null)
                return;

            var count = 0;
            for (var index = 0; index < players.Length && index < MaxPlayers; index++)
            {
                var player = players[index];
                if (player == null || player.seat < 0 || player.seat >= MaxPlayers)
                    continue;

                if (!string.IsNullOrWhiteSpace(player.name))
                    _names[player.seat] = player.name;

                if (player.seat + 1 > count)
                    count = player.seat + 1;
            }

            if (count > 0)
                _playerCount = count;
        }

        private WirePlayer[] SnapshotPlayers()
        {
            var players = new WirePlayer[_playerCount];
            for (var index = 0; index < _playerCount; index++)
            {
                players[index] = new WirePlayer
                {
                    seat = index,
                    name = NameOf(index),
                    score = _sheets[index].GetTotalScore()
                };
            }

            return players;
        }

        private void RaiseJoined(int seat, string playerName, int count)
        {
            LocalPlayerJoined?.Invoke(new LocalPlayerJoined
            {
                Seat = seat,
                PlayerName = playerName,
                PlayerCount = count
            });
        }

        private bool EveryoneFinished()
        {
            for (var index = 0; index < _playerCount; index++)
            {
                if (!_sheets[index].IsFullyCompleted())
                    return false;
            }

            return true;
        }

        private int NextOpenSeat(int from)
        {
            for (var step = 1; step <= _playerCount; step++)
            {
                var seat = (from + step) % _playerCount;
                if (!_sheets[seat].IsFullyCompleted())
                    return seat;
            }

            return -1;
        }

        private void DropPeer(Peer peer)
        {
            if (peer == null)
                return;

            _peers.Remove(peer);
            try
            {
                peer.Client.Close();
            }
            catch (Exception)
            {
                // Сокет уже закрыт другой стороной.
            }

            if (!IsHost)
            {
                _started = false;
                SetStatus("Соединение с комнатой закрыто.");
                return;
            }

            if (peer.Seat < 0 || peer.Seat >= _playerCount)
                return;

            SetStatus(NameOf(peer.Seat) + " отключился.");
            if (_started && _turnSeat == peer.Seat)
            {
                _dice = Array.Empty<int>();
                _turnSeat = NextOpenSeat(peer.Seat);
                _rollsLeft = _turnSeat >= 0 ? YahtzeeConfig.Current.RollsPerTurn : 0;
                Broadcast(new WireMessage
                {
                    kind = "turn",
                    next = _turnSeat,
                    rollsLeft = _rollsLeft
                });
                LocalCategorySelected?.Invoke(new LocalCategorySelected
                {
                    Seat = -1,
                    PlayerName = NameOf(peer.Seat),
                    Category = YahtzeeCategory.Chance,
                    Score = 0,
                    NextSeat = _turnSeat
                });
            }
        }

        private void Broadcast(WireMessage message)
        {
            for (var index = 0; index < _peers.Count; index++)
                Send(_peers[index].Writer, message);
        }

        private void SendToHost(WireMessage message)
        {
            Send(_serverWriter, message);
        }

        private void Send(StreamWriter writer, WireMessage message)
        {
            if (writer == null || message == null)
                return;

            var line = JsonConvert.SerializeObject(message);
            lock (_writeGate)
            {
                try
                {
                    writer.WriteLine(line);
                }
                catch (Exception exception)
                {
                    SetStatus("Не удалось отправить ход: " + exception.Message);
                }
            }
        }

        private static Peer OpenPeer(TcpClient client, int seat)
        {
            var writer = new StreamWriter(client.GetStream(), new UTF8Encoding(false), BufferSize, true)
            {
                AutoFlush = true
            };
            return new Peer
            {
                Client = client,
                Writer = writer,
                Seat = seat
            };
        }

        private int DiscoveryPort => Port >= 65535 ? 40778 : Port + 1;

        private void StartBeacon()
        {
            StopBeacon();
            try
            {
                _beacon = new UdpClient(DiscoveryPort);
                _beacon.EnableBroadcast = true;
                ThreadPool.QueueUserWorkItem(_ => BeaconLoop());
            }
            catch (Exception exception)
            {
                SetStatus("Комната открыта, но автопоиск недоступен: " + exception.Message);
            }
        }

        private void StopBeacon()
        {
            if (_beacon == null)
                return;

            try
            {
                _beacon.Close();
            }
            catch (Exception)
            {
                // Маяк уже закрыт.
            }

            _beacon = null;
        }

        private void BeaconLoop()
        {
            var beacon = _beacon;
            while (_listening && beacon != null && ReferenceEquals(beacon, _beacon))
            {
                try
                {
                    var remote = new IPEndPoint(IPAddress.Any, 0);
                    var data = beacon.Receive(ref remote);
                    var text = Encoding.UTF8.GetString(data);
                    if (!text.StartsWith("YAHTZEE?", StringComparison.Ordinal))
                        continue;

                    var safeName = (_names[0] ?? "Хост").Replace("|", " ");
                    var payload = "YAHTZEE|" + safeName + "|" + LocalAddress + "|" + Port;
                    var response = Encoding.UTF8.GetBytes(payload);
                    beacon.Send(response, response.Length, remote);
                }
                catch (Exception)
                {
                    break;
                }
            }
        }

        private void DiscoverLoop(int generation)
        {
            UdpClient client = null;
            try
            {
                client = new UdpClient();
                client.EnableBroadcast = true;
                client.Client.ReceiveTimeout = 400;
                var request = Encoding.UTF8.GetBytes("YAHTZEE?");
                client.Send(request, request.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));
                var broadcasts = LocalBroadcasts();
                for (var index = 0; index < broadcasts.Count; index++)
                    client.Send(request, request.Length, new IPEndPoint(broadcasts[index], DiscoveryPort));

                var seen = new HashSet<string>();
                var foundAny = false;
                var until = DateTime.UtcNow.AddMilliseconds(2500);
                while (DateTime.UtcNow < until && generation == _discoveryGeneration)
                {
                    try
                    {
                        var remote = new IPEndPoint(IPAddress.Any, 0);
                        var data = client.Receive(ref remote);
                        var text = Encoding.UTF8.GetString(data);
                        if (!TryParseBeacon(text, remote, out var found))
                            continue;

                        if (found.Address == LocalAddress && IsHost)
                            continue;

                        if (!seen.Add(found.Address))
                            continue;

                        foundAny = true;
                        var captured = found;
                        Post(() =>
                        {
                            if (generation != _discoveryGeneration)
                                return;

                            LocalHostDiscovered?.Invoke(captured);
                        });
                    }
                    catch (SocketException)
                    {
                        // Таймаут приёма: продолжаем слушать до конца окна поиска.
                    }
                }

                Post(() =>
                {
                    if (generation != _discoveryGeneration)
                        return;

                    SetStatus(foundAny ? "Поиск завершён." : "Устройства не найдены.");
                });
            }
            catch (Exception exception)
            {
                Post(() =>
                {
                    if (generation == _discoveryGeneration)
                        SetStatus("Ошибка связи: " + exception.Message);
                });
            }
            finally
            {
                if (client != null)
                    client.Close();
            }
        }

        private static bool TryParseBeacon(string text, IPEndPoint remote, out LocalHostFound found)
        {
            found = null;
            if (string.IsNullOrEmpty(text) || !text.StartsWith("YAHTZEE|", StringComparison.Ordinal))
                return false;

            var parts = text.Split('|');
            if (parts.Length < 4)
                return false;

            var address = string.IsNullOrWhiteSpace(parts[2]) ? remote.Address.ToString() : parts[2].Trim();
            if (!int.TryParse(parts[3], out var port))
                port = YahtzeeConfig.Current.LocalTablePort;

            found = new LocalHostFound
            {
                Name = string.IsNullOrWhiteSpace(parts[1]) ? "Комната" : parts[1].Trim(),
                Address = address,
                Port = port
            };
            return true;
        }

        private static List<IPAddress> LocalBroadcasts()
        {
            var list = new List<IPAddress>();
            try
            {
                var addresses = Dns.GetHostAddresses(Dns.GetHostName());
                for (var index = 0; index < addresses.Length; index++)
                {
                    var address = addresses[index];
                    if (address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address))
                        continue;

                    var bytes = address.GetAddressBytes();
                    bytes[3] = 255;
                    list.Add(new IPAddress(bytes));
                }
            }
            catch (Exception)
            {
                return list;
            }

            return list;
        }

        private void CloseSockets()
        {
            _listening = false;
            StopBeacon();
            if (_listener != null)
            {
                try
                {
                    _listener.Stop();
                }
                catch (Exception)
                {
                    // Слушатель уже остановлен.
                }

                _listener = null;
            }

            for (var index = 0; index < _peers.Count; index++)
            {
                try
                {
                    _peers[index].Client.Close();
                }
                catch (Exception)
                {
                    // Сокет гостя уже закрыт.
                }
            }

            _peers.Clear();
            if (_serverLink != null)
            {
                try
                {
                    _serverLink.Close();
                }
                catch (Exception)
                {
                    // Сокет хоста уже закрыт.
                }

                _serverLink = null;
                _serverWriter = null;
            }
        }

        private void Post(Action action)
        {
            if (action == null)
                return;

            if (_main != null)
                _main.Post(_ => action(), null);
            else
                action();
        }

        private void SetStatus(string status)
        {
            Status = status ?? string.Empty;
            StatusChanged?.Invoke(Status);
        }

        private static string DeviceName(string fallback)
        {
            var name = SystemInfo.deviceName;
            if (string.IsNullOrWhiteSpace(name))
                return fallback;

            name = name.Trim();
            return name.Length > 18 ? name.Substring(0, 18) : name;
        }

        private static string FindLocalAddress()
        {
            try
            {
                var addresses = Dns.GetHostAddresses(Dns.GetHostName());
                for (var index = 0; index < addresses.Length; index++)
                {
                    var address = addresses[index];
                    if (address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
                        return address.ToString();
                }
            }
            catch (Exception)
            {
                return "127.0.0.1";
            }

            return "127.0.0.1";
        }

        private static string ParseAddress(string targetIp)
        {
            if (string.IsNullOrWhiteSpace(targetIp))
                return null;

            var value = targetIp.Trim();
            var colon = value.LastIndexOf(':');
            if (colon > 0)
                value = value.Substring(0, colon);

            return value.Length == 0 ? null : value;
        }

        private sealed class Peer
        {
            public TcpClient Client;
            public StreamWriter Writer;
            public int Seat;
            public bool Welcomed;
        }

        private sealed class WireMessage
        {
            public string kind;
            public int seat;
            public string name;
            public int[] dice;
            public int rollsLeft;
            public int category;
            public int score;
            public int next;
            public int count;
            public bool[] keep;
            public WirePlayer[] players;
        }

        private sealed class WirePlayer
        {
            public int seat;
            public string name;
            public int score;
        }
    }
#endif
}
