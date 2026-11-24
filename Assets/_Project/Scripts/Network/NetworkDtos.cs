using System;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Yahtzee.Network
{
    internal static class NetworkJson
    {
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            MissingMemberHandling = MissingMemberHandling.Ignore,
            NullValueHandling = NullValueHandling.Include
        };
    }

    public class ReverbEvent
    {
        [JsonIgnore]
        public string EventName { get; set; }

        [JsonIgnore]
        public string Channel { get; set; }
    }

    public sealed class AuthResponse
    {
        public string Token { get; set; }
        public ProfileResponse User { get; set; }
    }

    public sealed class ProfileResponse
    {
        public int Id { get; set; }
        public string Nickname { get; set; }
        public string AvatarUrl { get; set; }
        public int? AvatarId { get; set; }
        public string CountryCode { get; set; }
        public int SoftCurrency { get; set; }
        public int? EquippedDiceSkinId { get; set; }
        public int? EquippedTableBoxSkinId { get; set; }
        public string EquippedDiceSkinSlug { get; set; }
        public string EquippedTableBoxSkinSlug { get; set; }
        public int Mmr { get; set; }
        public bool IsAdFree { get; set; }
        public string IsAdFreeUntil { get; set; }
        public string LastPlatform { get; set; }
        public string LastSeenAt { get; set; }
    }

    public sealed class MatchmakingResponse
    {
        public string Status { get; set; }
        public int? EstimatedWaitTime { get; set; }
        public string Message { get; set; }
        public int? QueueCount { get; set; }
        public int? MmrWindow { get; set; }
        public MatchState Match { get; set; }
    }

    public sealed class GameStateResponse
    {
        public MatchState Match { get; set; }
        public TurnState Turn { get; set; }
    }

    public sealed class MatchReconnectResponseDto
    {
        public MatchState Match { get; set; }
        public TurnState Turn { get; set; }

        [JsonIgnore]
        public int CurrentTurnPlayerId => Match != null && Match.CurrentPlayerId.HasValue ? Match.CurrentPlayerId.Value : 0;

        [JsonIgnore]
        public int RollsLeft => Turn != null ? Turn.RollsRemaining : 0;

        [JsonIgnore]
        public string TurnExpiresAt => Turn != null ? Turn.ExpiresAt : null;

        public static MatchReconnectResponseDto From(GameStateResponse state)
        {
            if (state == null)
                return null;

            return new MatchReconnectResponseDto
            {
                Match = state.Match,
                Turn = state.Turn
            };
        }
    }

    public sealed class MatchState
    {
        public int Id { get; set; }
        public string Status { get; set; }
        public string Type { get; set; }
        public int MaxPlayers { get; set; }
        public int PlayerCount { get; set; }
        public bool HasPassword { get; set; }
        public int? HostUserId { get; set; }
        public int? CurrentPlayerId { get; set; }
        public int CurrentRound { get; set; }
        public int Mmr { get; set; }
        public string ReconnectDeadlineAt { get; set; }
        public bool AbandonChoiceOpen { get; set; }
        public int TurnDurationSeconds { get; set; }
        public MatchSeat[] Players { get; set; }
    }

    public sealed class MatchSeat
    {
        public int? UserId { get; set; }
        public string Nickname { get; set; }
        public int TurnOrder { get; set; }
        public string ConnectionStatus { get; set; }
        public bool IsHost { get; set; }
        public bool IsBot { get; set; }
        public int? FinalScore { get; set; }
        public int? OriginUserId { get; set; }
        public string QueueOrigin { get; set; }
        public ScoreSheet ScoreSheet { get; set; }
    }

    public sealed class ScoreSheet
    {
        public int? Ones { get; set; }
        public int? Twos { get; set; }
        public int? Threes { get; set; }
        public int? Fours { get; set; }
        public int? Fives { get; set; }
        public int? Sixes { get; set; }
        public int? ThreeOfAKind { get; set; }
        public int? FourOfAKind { get; set; }
        public int? FullHouse { get; set; }
        public int? SmallStraight { get; set; }
        public int? LargeStraight { get; set; }
        public int? Yahtzee { get; set; }
        public int? Chance { get; set; }
        public int YahtzeeBonusCount { get; set; }
    }

    public sealed class TurnState
    {
        public int MatchPlayerId { get; set; }
        public int? UserId { get; set; }
        public bool IsBot { get; set; }
        public int?[] Dice { get; set; }
        public bool[] Held { get; set; }
        public int RollsCount { get; set; }
        public int RollsRemaining { get; set; }
        public string ExpiresAt { get; set; }
    }

    public sealed class MatchStartedEvent : ReverbEvent
    {
        public int MatchId { get; set; }
        public int? CurrentPlayerId { get; set; }
        public int CurrentRound { get; set; }
        public string TurnExpiresAt { get; set; }
        public StartedPlayer[] Players { get; set; }
    }

    public sealed class StartedPlayer
    {
        public int MatchPlayerId { get; set; }
        public int? UserId { get; set; }
        public string Nickname { get; set; }
        public int TurnOrder { get; set; }
        public bool IsBot { get; set; }
        public string ConnectionStatus { get; set; }
    }

    public sealed class DiceRolledEvent : ReverbEvent
    {
        public int MatchId { get; set; }
        public int? UserId { get; set; }
        public int MatchPlayerId { get; set; }
        public int[] Dice { get; set; }
        public bool[] Held { get; set; }
        public int RollsCount { get; set; }
        public int RollsRemaining { get; set; }
        public string TurnExpiresAt { get; set; }
    }

    public sealed class DiceHeldEvent : ReverbEvent
    {
        public int MatchId { get; set; }
        public int? UserId { get; set; }
        public int MatchPlayerId { get; set; }
        public int[] Dice { get; set; }
        public bool[] Held { get; set; }
    }

    public sealed class TurnChangedEvent : ReverbEvent
    {
        public int MatchId { get; set; }
        public int CurrentRound { get; set; }
        public int? CurrentPlayerId { get; set; }
        public int CurrentMatchPlayerId { get; set; }
        public bool CurrentIsBot { get; set; }
        public string TurnExpiresAt { get; set; }
        public int? ScoredUserId { get; set; }
        public int ScoredMatchPlayerId { get; set; }
        public string Category { get; set; }
        public int Score { get; set; }
        public bool YahtzeeBonusAwarded { get; set; }
        public ScoreSheet ScoreSheet { get; set; }
    }

    public sealed class MatchOverEvent : ReverbEvent
    {
        public int MatchId { get; set; }
        public string Reason { get; set; }
        public int[] WinnerUserIds { get; set; }
        public Standing[] Standings { get; set; }
    }

    public sealed class Standing
    {
        public int? UserId { get; set; }
        public string Nickname { get; set; }
        public bool IsBot { get; set; }
        public int FinalScore { get; set; }
        public int MmrDelta { get; set; }
        public int Mmr { get; set; }
        [Newtonsoft.Json.JsonProperty("softCurrencyAwarded")]
        public int SoftCurrencyAwarded { get; set; }
    }

    public sealed class OpponentDisconnectedEvent : ReverbEvent
    {
        public int MatchId { get; set; }
        public int? UserId { get; set; }
        public string Nickname { get; set; }
        public string Reason { get; set; }
        public string ReconnectUntil { get; set; }
        public int ReconnectSeconds { get; set; }
    }

    public sealed class PlayerLeftEvent : ReverbEvent
    {
        public int MatchId { get; set; }
        public int? UserId { get; set; }
        public string Nickname { get; set; }
        public int? FrozenScore { get; set; }
        public int? CurrentPlayerId { get; set; }
        public int CurrentRound { get; set; }
        public string MatchStatus { get; set; }
    }

    public sealed class TurnTimedOutEvent : ReverbEvent
    {
        public int MatchId { get; set; }
        public int? UserId { get; set; }
        public int MatchPlayerId { get; set; }
        public int CurrentRound { get; set; }
    }

    public sealed class PlayerReconnectedEvent : ReverbEvent
    {
        public int MatchId { get; set; }
        public int? UserId { get; set; }
        public string TurnExpiresAt { get; set; }
    }

    public sealed class OpponentAbandonChoiceEvent : ReverbEvent
    {
        public int MatchId { get; set; }
        public int? UserId { get; set; }
        public int? DisconnectedUserId { get; set; }
        public string Nickname { get; set; }
        public string Reason { get; set; }
        public string[] Choices { get; set; }
    }

    public sealed class PlayerReplacedByBotEvent : ReverbEvent
    {
        public int MatchId { get; set; }
        public int ReplacedUserId { get; set; }
        public int MatchPlayerId { get; set; }
    }

    public sealed class BroadcastAuthResponse
    {
        [JsonProperty("auth")]
        public string Auth { get; set; }

        [JsonProperty("channel_data")]
        public string ChannelData { get; set; }
    }

    public sealed class PusherConnectionData
    {
        [JsonProperty("socket_id")]
        public string SocketId { get; set; }

        [JsonProperty("activity_timeout")]
        public int ActivityTimeout { get; set; }
    }

    public sealed class PusherErrorData
    {
        [JsonProperty("code")]
        public int? Code { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }
    }

    public sealed class LobbyRoomDto
    {
        public int MatchId { get; set; }
        public string RoomName { get; set; }
        public int CurrentPlayersCount { get; set; }
        public int MaxPlayers { get; set; }
        public bool HasPassword { get; set; }
    }

    public sealed class LobbyListResponseDto
    {
        public LobbyRoomDto[] Lobbies { get; set; }
        public int Page { get; set; }
        public int PerPage { get; set; }
        public bool HasMore { get; set; }
    }

    public sealed class MmrLeaderboardEntryDto
    {
        public int Rank { get; set; }
        public int UserId { get; set; }
        public string Nickname { get; set; }
        public string CountryCode { get; set; }
        public int Mmr { get; set; }
    }

    public sealed class LeaderboardMetaDto
    {
        public int UserId { get; set; }
        public string Nickname { get; set; }
        public int Rank { get; set; }
        public int Mmr { get; set; }
        public float Points { get; set; }
        public int DailyMatchesPlayed { get; set; }
        public int DailyMatchesRemaining { get; set; }
    }

    public sealed class MmrLeaderboardDto
    {
        public MmrLeaderboardEntryDto[] Entries { get; set; }
        public LeaderboardMetaDto Meta { get; set; }
    }

    public sealed class TournamentLeaderboardEntryDto
    {
        public int Rank { get; set; }
        public int UserId { get; set; }
        public string Nickname { get; set; }
        public string CountryCode { get; set; }
        public float Points { get; set; }
    }

    public sealed class TournamentLeaderboardDto
    {
        public string WeekIdentifier { get; set; }
        public TournamentLeaderboardEntryDto[] Entries { get; set; }
        public LeaderboardMetaDto Meta { get; set; }
    }

    public sealed class LeaderboardEntryDto
    {
        [JsonProperty("userId")]
        public int Id { get; set; }

        public string Nickname { get; set; }

        [JsonProperty("mmr")]
        public int Rating { get; set; }

        [JsonProperty("points")]
        public float Score { get; set; }

        public string CountryCode { get; set; }
        public int Rank { get; set; }
    }

    public sealed class LeaderboardSelfMetaDto
    {
        [JsonProperty("rank")]
        public int MyRank { get; set; }

        [JsonProperty("mmr")]
        public int MyRating { get; set; }

        [JsonProperty("points")]
        public float MyScore { get; set; }

        [JsonProperty("dailyMatchesRemaining")]
        public int DailyMatchesLeft { get; set; }

        [JsonProperty("dailyMatchesPlayed")]
        public int DailyMatchesPlayed { get; set; }

        public string Nickname { get; set; }
    }

    public sealed class LeaderboardResponseDto
    {
        public LeaderboardEntryDto[] Entries { get; set; }
        public LeaderboardSelfMetaDto Meta { get; set; }

        public static LeaderboardResponseDto FromMmr(MmrLeaderboardDto source)
        {
            var response = new LeaderboardResponseDto();
            var rows = source != null ? source.Entries : null;
            var count = rows != null ? rows.Length : 0;
            var entries = new LeaderboardEntryDto[count];
            for (var index = 0; index < count; index++)
            {
                var row = rows[index];
                entries[index] = new LeaderboardEntryDto
                {
                    Id = row != null ? row.UserId : 0,
                    Nickname = row != null ? row.Nickname : null,
                    Rating = row != null ? row.Mmr : 0,
                    Score = row != null ? row.Mmr : 0f,
                    CountryCode = row != null ? row.CountryCode : null,
                    Rank = row != null ? row.Rank : index + 1
                };
            }

            response.Entries = entries;
            response.Meta = MapMeta(source != null ? source.Meta : null, true);
            return response;
        }

        public static LeaderboardResponseDto FromTournament(TournamentLeaderboardDto source)
        {
            var response = new LeaderboardResponseDto();
            var rows = source != null ? source.Entries : null;
            var count = rows != null ? rows.Length : 0;
            var entries = new LeaderboardEntryDto[count];
            for (var index = 0; index < count; index++)
            {
                var row = rows[index];
                entries[index] = new LeaderboardEntryDto
                {
                    Id = row != null ? row.UserId : 0,
                    Nickname = row != null ? row.Nickname : null,
                    Rating = 0,
                    Score = row != null ? row.Points : 0f,
                    CountryCode = row != null ? row.CountryCode : null,
                    Rank = row != null ? row.Rank : index + 1
                };
            }

            response.Entries = entries;
            response.Meta = MapMeta(source != null ? source.Meta : null, false);
            return response;
        }

        private static LeaderboardSelfMetaDto MapMeta(LeaderboardMetaDto meta, bool mmrBoard)
        {
            if (meta == null)
                return new LeaderboardSelfMetaDto();

            return new LeaderboardSelfMetaDto
            {
                MyRank = meta.Rank,
                MyRating = meta.Mmr,
                MyScore = mmrBoard ? meta.Mmr : meta.Points,
                DailyMatchesLeft = meta.DailyMatchesRemaining,
                DailyMatchesPlayed = meta.DailyMatchesPlayed,
                Nickname = meta.Nickname
            };
        }
    }

    public sealed class ApiErrorBody
    {
        public string Error { get; set; }
        public string Message { get; set; }
        public string Category { get; set; }
        public bool? Retryable { get; set; }
        public int? RetryAfterSeconds { get; set; }
        public string BannedUntil { get; set; }
        public string ExpiresAt { get; set; }
    }

    public sealed class ServiceStatusDto
    {
        public string Status { get; set; }
        public string Message { get; set; }
        public int? RetryAfterSeconds { get; set; }
        public string MinClientVersion { get; set; }
        public ServiceFeaturesDto Features { get; set; }
    }

    public sealed class ServiceFeaturesDto
    {
        public bool Matchmaking { get; set; } = true;
        public bool Lobbies { get; set; } = true;
        public bool Shop { get; set; } = true;
    }

    public sealed class NetworkException : Exception
    {
        public long StatusCode { get; }
        public string ErrorCode { get; }
        public string Category { get; }
        public bool? Retryable { get; }
        public int? RetryAfterSeconds { get; }

        public NetworkException(
            long statusCode,
            string errorCode,
            string message,
            string category = null,
            bool? retryable = null,
            int? retryAfterSeconds = null)
            : base(message)
        {
            StatusCode = statusCode;
            ErrorCode = errorCode ?? string.Empty;
            Category = category ?? string.Empty;
            Retryable = retryable;
            RetryAfterSeconds = retryAfterSeconds;
        }

        public static NetworkException From(long statusCode, string body)
        {
            var code = "HTTP_" + statusCode.ToString(CultureInfo.InvariantCulture);
            var message = "Request failed.";
            string category = null;
            bool? retryable = null;
            int? retryAfter = null;

            if (statusCode == 0)
            {
                code = ApiErrorCode.NetworkUnreachable;
                category = "transport";
                retryable = true;
            }
            else if (statusCode == 503)
                code = ApiErrorCode.ServiceUnavailable;
            else if (statusCode == 502)
                code = ApiErrorCode.ServiceUnavailable;

            if (!string.IsNullOrEmpty(body))
            {
                try
                {
                    var error = JsonConvert.DeserializeObject<ApiErrorBody>(body, NetworkJson.Settings);
                    if (error != null)
                    {
                        if (!string.IsNullOrEmpty(error.Error))
                            code = error.Error;
                        if (!string.IsNullOrEmpty(error.Message))
                            message = error.Message;
                        category = error.Category;
                        retryable = error.Retryable;
                        retryAfter = error.RetryAfterSeconds;
                    }
                }
                catch (JsonException)
                {
                    message = body;
                }
            }

            return new NetworkException(statusCode, code, message, category, retryable, retryAfter);
        }

        public static NetworkException Transport(string code, string fallbackMessage = null)
        {
            return new NetworkException(
                0,
                code,
                fallbackMessage ?? code,
                "transport",
                true,
                null);
        }
    }
}
