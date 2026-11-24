using UnityEngine;

namespace Yahtzee.Core
{
    [CreateAssetMenu(fileName = "YahtzeeConfig", menuName = "Yahtzee/Config")]
    public sealed class YahtzeeConfig : ScriptableObject
    {
        private const string ResourceName = "YahtzeeConfig";

        private static YahtzeeConfig _current;

        public static YahtzeeConfig Current
        {
            get
            {
                if (_current != null)
                    return _current;

                _current = Resources.Load<YahtzeeConfig>(ResourceName);
                if (_current != null)
                    return _current;

                _current = CreateInstance<YahtzeeConfig>();
                _current.name = ResourceName;
                return _current;
            }
        }

        [Header("Название")]
        [SerializeField] private string gameName = "YADZY";

        [Header("Окружение")]
        [Tooltip("В release-билде без Development Build всегда production. В Editor — по флагу ниже.")]
        [SerializeField] private bool useProductionInEditor;

        [Header("API (development)")]
        [SerializeField] private string apiBaseUrl = "https://yahtzee.local";
        [SerializeField] private string guestLoginPath = "/api/auth/guest";
        [SerializeField] private string profilePath = "/api/profile";
        [SerializeField] private string quickMatchPath = "/api/matchmaking/queue";
        [SerializeField] private string rankedMatchPath = "/api/matchmaking/ranked";
        [SerializeField] private string tournamentJoinPath = "/api/tournament/join";
        [SerializeField] private string tournamentLeavePath = "/api/tournament/leave";
        [SerializeField] private string mmrLeaderboardPath = "/api/leaderboard/mmr";
        [SerializeField] private string tournamentLeaderboardPath = "/api/leaderboard/tournament";
        [SerializeField] private string matchActionPath = "/api/matches";
        [SerializeField] private string broadcastAuthPath = "/broadcasting/auth";
        [SerializeField] private string crashReportPath = "/api/crash-reports";
        [SerializeField] private string tokenPrefKey = "yahtzee.sanctum.token";
        [SerializeField] private string activeMatchPrefKey = "yahtzee.active.match.id";
        [SerializeField] private int httpTimeoutSeconds = 20;

        [Header("Reverb (development)")]
        [SerializeField] private string reverbHost = "yahtzee.local";
        [SerializeField] private int reverbPort = 8080;
        [SerializeField] private string reverbAppKey = "yadzee-local-key";
        [SerializeField] private bool reverbUseTls;
        [SerializeField] private int socketTimeoutSeconds = 10;
        [SerializeField] private float pingIntervalSeconds = 30f;
        [SerializeField] private int minPingIntervalSeconds = 15;
        [SerializeField] private int reconnectStartDelaySeconds = 1;
        [SerializeField] private int reconnectMaxDelaySeconds = 30;

        [Header("Production endpoints")]
        [SerializeField] private string productionApiBaseUrl = "https://api.yadzy.app";
        [SerializeField] private string productionReverbHost = "reverb.yadzy.app";
        [SerializeField] private int productionReverbPort;
        [SerializeField] private string productionReverbAppKey = "";
        [SerializeField] private bool productionReverbUseTls = true;
        [SerializeField] private string productionPrivacyPolicyUrl = "https://yadzy.app/privacy.html";
        [SerializeField] private string productionTermsOfServiceUrl = "https://yadzy.app/terms.html";

        [Header("Матчмейкинг")]
        [SerializeField] private int matchmakingPollSeconds = 2;

        [Header("Локальная сеть")]
        [SerializeField] private int localTablePort = 40777;

        [Header("Стор / юридическое (development)")]
        [SerializeField] private string privacyPolicyUrl = "https://yahtzee.local/privacy.html";
        [SerializeField] private string termsOfServiceUrl = "https://yahtzee.local/terms.html";

        [Header("Правила")]
        [SerializeField] private int diceCount = 5;
        [SerializeField] private int rollsPerTurn = 3;
        [SerializeField] private int upperBonusThreshold = 63;
        [SerializeField] private int upperBonusValue = 35;
        [SerializeField] private int fullHouseScore = 25;
        [SerializeField] private int smallStraightScore = 30;
        [SerializeField] private int largeStraightScore = 40;
        [SerializeField] private int yahtzeeScore = 50;
        [SerializeField] private int yahtzeeBonusPoints = 100;
        [SerializeField] private int maxYahtzeeBonuses = 12;

        [Header("Кубики")]
        [SerializeField] private float diceScale = 0.3f;
        [SerializeField] private float throwForce = 5.4f;
        [SerializeField] private float torqueForce = 3.4f;
        [SerializeField] private float throwForward = 1.5f;
        [SerializeField] private float restTimeoutSeconds = 8f;
        [SerializeField] private float restSpeedSqr = 0.01f;
        [SerializeField] private float bounciness = 0.14f;
        [SerializeField] private float friction = 0.9f;

        public bool UseProductionEndpoints
        {
            get
            {
#if UNITY_EDITOR
                return useProductionInEditor;
#else
                return !Debug.isDebugBuild || useProductionInEditor;
#endif
            }
        }

        public string GameName => string.IsNullOrWhiteSpace(gameName) ? "YADZY" : gameName.Trim();

        public string ApiBaseUrl
        {
            get
            {
                if (UseProductionEndpoints && !string.IsNullOrWhiteSpace(productionApiBaseUrl))
                    return productionApiBaseUrl.Trim().TrimEnd('/');

                return string.IsNullOrWhiteSpace(apiBaseUrl)
                    ? "http://127.0.0.1:8000"
                    : apiBaseUrl.Trim().TrimEnd('/');
            }
        }

        public string GuestLoginPath => NormalizePath(guestLoginPath, "/api/auth/guest");
        public string ProfilePath => NormalizePath(profilePath, "/api/profile");
        public string ShopPath => "/api/shop";
        public string ShopBuyPath => "/api/shop/buy";
        public string SkinEquipPath => "/api/skins/equip";
        public string CrashReportPath => NormalizePath(crashReportPath, "/api/crash-reports");
        public string ServiceStatusPath => "/api/status";
        public string QuickMatchPath => NormalizePath(quickMatchPath, "/api/matchmaking/queue");
        public string RankedMatchPath => NormalizePath(rankedMatchPath, "/api/matchmaking/ranked");
        public string TournamentJoinPath => NormalizePath(tournamentJoinPath, "/api/tournament/join");
        public string TournamentLeavePath => NormalizePath(tournamentLeavePath, "/api/tournament/leave");
        public string MmrLeaderboardPath => NormalizePath(mmrLeaderboardPath, "/api/leaderboard/mmr");
        public string TournamentLeaderboardPath => NormalizePath(tournamentLeaderboardPath, "/api/leaderboard/tournament");
        public string MatchActionPath => NormalizePath(matchActionPath, "/api/matches");
        public string BroadcastAuthPath => NormalizePath(broadcastAuthPath, "/broadcasting/auth");
        public string TokenPrefKey => string.IsNullOrWhiteSpace(tokenPrefKey) ? "yahtzee.sanctum.token" : tokenPrefKey.Trim();
        public string ActiveMatchPrefKey => string.IsNullOrWhiteSpace(activeMatchPrefKey)
            ? "yahtzee.active.match.id"
            : activeMatchPrefKey.Trim();
        public int HttpTimeoutSeconds => Mathf.Max(1, httpTimeoutSeconds);

        public string ReverbHost
        {
            get
            {
                if (UseProductionEndpoints && !string.IsNullOrWhiteSpace(productionReverbHost))
                    return productionReverbHost.Trim();
                return string.IsNullOrWhiteSpace(reverbHost) ? "127.0.0.1" : reverbHost.Trim();
            }
        }

        public int ReverbPort
        {
            get
            {
                if (UseProductionEndpoints)
                    return productionReverbPort <= 0 ? 0 : Mathf.Clamp(productionReverbPort, 1, 65535);
                return reverbPort <= 0 ? 0 : Mathf.Clamp(reverbPort, 1, 65535);
            }
        }

        public string ReverbAppKey
        {
            get
            {
                if (UseProductionEndpoints && !string.IsNullOrWhiteSpace(productionReverbAppKey))
                    return productionReverbAppKey.Trim();
                return reverbAppKey != null ? reverbAppKey.Trim() : string.Empty;
            }
        }

        public bool ReverbUseTls => UseProductionEndpoints ? productionReverbUseTls : reverbUseTls;
        public int SocketTimeoutSeconds => Mathf.Max(1, socketTimeoutSeconds);
        public float PingIntervalSeconds => Mathf.Max(1f, pingIntervalSeconds);
        public int MinPingIntervalSeconds => Mathf.Max(1, minPingIntervalSeconds);
        public int ReconnectStartDelaySeconds => Mathf.Max(1, reconnectStartDelaySeconds);
        public int ReconnectMaxDelaySeconds => Mathf.Max(ReconnectStartDelaySeconds, reconnectMaxDelaySeconds);

        public int MatchmakingPollSeconds => Mathf.Max(1, matchmakingPollSeconds);
        public int LocalTablePort => Mathf.Clamp(localTablePort, 1024, 65535);

        public string PrivacyPolicyUrl
        {
            get
            {
                if (UseProductionEndpoints && !string.IsNullOrWhiteSpace(productionPrivacyPolicyUrl))
                    return productionPrivacyPolicyUrl.Trim();
                return string.IsNullOrWhiteSpace(privacyPolicyUrl)
                    ? "https://yahtzee.local/privacy.html"
                    : privacyPolicyUrl.Trim();
            }
        }

        public string TermsOfServiceUrl
        {
            get
            {
                if (UseProductionEndpoints && !string.IsNullOrWhiteSpace(productionTermsOfServiceUrl))
                    return productionTermsOfServiceUrl.Trim();
                return string.IsNullOrWhiteSpace(termsOfServiceUrl)
                    ? "https://yahtzee.local/terms.html"
                    : termsOfServiceUrl.Trim();
            }
        }

        public int DiceCount => Mathf.Max(1, diceCount);
        public int RollsPerTurn => Mathf.Max(1, rollsPerTurn);
        public int UpperBonusThreshold => Mathf.Max(0, upperBonusThreshold);
        public int UpperBonusValue => Mathf.Max(0, upperBonusValue);
        public int FullHouseScore => Mathf.Max(0, fullHouseScore);
        public int SmallStraightScore => Mathf.Max(0, smallStraightScore);
        public int LargeStraightScore => Mathf.Max(0, largeStraightScore);
        public int YahtzeeScore => Mathf.Max(0, yahtzeeScore);
        public int YahtzeeBonusPoints => Mathf.Max(0, yahtzeeBonusPoints);
        public int MaxYahtzeeBonuses => Mathf.Clamp(maxYahtzeeBonuses, 0, 12);

        public float DiceScale => Mathf.Max(0.05f, diceScale);
        public float ThrowForce => Mathf.Max(0f, throwForce);
        public float TorqueForce => Mathf.Max(0f, torqueForce);
        public float ThrowForward => Mathf.Max(0f, throwForward);
        public float RestTimeoutSeconds => Mathf.Max(0.5f, restTimeoutSeconds);
        public float RestSpeedSqr => Mathf.Max(0.0001f, restSpeedSqr);
        public float Bounciness => Mathf.Clamp01(bounciness);
        public float Friction => Mathf.Max(0f, friction);

        public string MatchPath(int matchId, string action = null)
        {
            var basePath = MatchActionPath + "/" + matchId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(action))
                return basePath;
            return basePath + "/" + action.Trim().TrimStart('/');
        }

        private static string NormalizePath(string path, string fallback)
        {
            var value = string.IsNullOrWhiteSpace(path) ? fallback : path.Trim();
            if (!value.StartsWith("/"))
                value = "/" + value;
            return value;
        }
    }
}
