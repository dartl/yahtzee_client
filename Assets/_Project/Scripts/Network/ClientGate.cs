using System;
using System.Threading.Tasks;
using Yahtzee.Feedback;
using Yahtzee.Localization;

namespace Yahtzee.Network
{
    /// <summary>
    /// Polls /api/status, enforces min client version and feature flags.
    /// </summary>
    public static class ClientGate
    {
        public const string ClientVersion = "1.0.0";

        public static ServiceStatusDto LastStatus { get; private set; }
        public static bool MatchmakingEnabled { get; private set; } = true;
        public static bool LobbiesEnabled { get; private set; } = true;
        public static bool ShopEnabled { get; private set; } = true;

        public static async Task RefreshAsync()
        {
            if (NetworkManager.Instance == null)
                return;

            var dto = await NetworkManager.Instance.RefreshServiceStatusAsync();
            Apply(dto);
        }

        public static void Apply(ServiceStatusDto dto)
        {
            LastStatus = dto;
            if (dto == null)
            {
                MatchmakingEnabled = false;
                LobbiesEnabled = false;
                ShopEnabled = false;
                return;
            }

            var features = dto.Features;
            MatchmakingEnabled = features == null || features.Matchmaking;
            LobbiesEnabled = features == null || features.Lobbies;
            ShopEnabled = features == null || features.Shop;

            if (!string.IsNullOrWhiteSpace(dto.MinClientVersion)
                && VersionOlder(ClientVersion, dto.MinClientVersion))
            {
                PlayerFeedback.ReportCode(
                    ApiErrorCode.VersionUnsupported,
                    GameText.Get("errors.VERSION_UNSUPPORTED"),
                    dto.RetryAfterSeconds);
                PlayerFeedback.SetServiceStatus(
                    ServiceUiStatus.VersionBlocked,
                    GameText.Get("service.body_version"),
                    dto.RetryAfterSeconds);
            }
        }

        public static string FeatureBlockedMessage(string featureKey)
        {
            return GameText.Get("ui.common.feature_disabled");
        }

        private static bool VersionOlder(string client, string minimum)
        {
            if (!Version.TryParse(Normalize(client), out var c))
                return false;
            if (!Version.TryParse(Normalize(minimum), out var m))
                return false;
            return c < m;
        }

        private static string Normalize(string value)
        {
            value = (value ?? string.Empty).Trim();
            var parts = value.Split('.');
            if (parts.Length == 1)
                return value + ".0.0";
            if (parts.Length == 2)
                return value + ".0";
            return value;
        }
    }
}
