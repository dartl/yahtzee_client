using System;

namespace Yahtzee.Network
{
    public enum ApiErrorCategory
    {
        Service,
        Auth,
        Validation,
        Gameplay,
        Lobby,
        Economy,
        Transport,
        Unknown
    }

    public enum FeedbackSeverity
    {
        Silent,
        Toast,
        Banner,
        Modal,
        Blocking
    }

    public static class ApiErrorCode
    {
        public const string ServiceMaintenance = "SERVICE_MAINTENANCE";
        public const string ServiceDegraded = "SERVICE_DEGRADED";
        public const string ServiceOverloaded = "SERVICE_OVERLOADED";
        public const string ServiceUnavailable = "SERVICE_UNAVAILABLE";
        public const string VersionUnsupported = "VERSION_UNSUPPORTED";
        public const string Unauthenticated = "UNAUTHENTICATED";
        public const string UserBanned = "USER_BANNED";
        public const string NetworkUnreachable = "NETWORK_UNREACHABLE";
        public const string RequestTimeout = "REQUEST_TIMEOUT";
        public const string SocketClosed = "SOCKET_CLOSED";
        public const string InvalidResponse = "INVALID_RESPONSE";
        public const string UnknownError = "UNKNOWN_ERROR";
        public const string AlreadyInLobby = "ALREADY_IN_LOBBY";
        public const string InvalidPassword = "INVALID_PASSWORD";
    }

    public readonly struct ApiErrorInfo
    {
        public readonly string Code;
        public readonly ApiErrorCategory Category;
        public readonly FeedbackSeverity Severity;
        public readonly bool Retryable;

        public ApiErrorInfo(string code, ApiErrorCategory category, FeedbackSeverity severity, bool retryable)
        {
            Code = code;
            Category = category;
            Severity = severity;
            Retryable = retryable;
        }
    }

    public static class ApiErrorCatalog
    {
        public static ApiErrorInfo Describe(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return new ApiErrorInfo(ApiErrorCode.UnknownError, ApiErrorCategory.Unknown, FeedbackSeverity.Toast, true);

            switch (code)
            {
                case ApiErrorCode.ServiceMaintenance:
                case ApiErrorCode.ServiceOverloaded:
                case ApiErrorCode.ServiceUnavailable:
                    return new ApiErrorInfo(code, ApiErrorCategory.Service, FeedbackSeverity.Blocking, true);
                case ApiErrorCode.ServiceDegraded:
                    return new ApiErrorInfo(code, ApiErrorCategory.Service, FeedbackSeverity.Banner, true);
                case ApiErrorCode.VersionUnsupported:
                    return new ApiErrorInfo(code, ApiErrorCategory.Service, FeedbackSeverity.Modal, false);
                case ApiErrorCode.Unauthenticated:
                    return new ApiErrorInfo(code, ApiErrorCategory.Auth, FeedbackSeverity.Modal, false);
                case ApiErrorCode.UserBanned:
                    return new ApiErrorInfo(code, ApiErrorCategory.Auth, FeedbackSeverity.Modal, false);
                case ApiErrorCode.NetworkUnreachable:
                case ApiErrorCode.RequestTimeout:
                case ApiErrorCode.SocketClosed:
                    return new ApiErrorInfo(code, ApiErrorCategory.Transport, FeedbackSeverity.Banner, true);
                case "FORBIDDEN":
                    return new ApiErrorInfo(code, ApiErrorCategory.Auth, FeedbackSeverity.Toast, false);
                case "VALIDATION_ERROR":
                case "NICKNAME_TAKEN":
                    return new ApiErrorInfo(code, ApiErrorCategory.Validation, FeedbackSeverity.Toast, false);
                case "ALREADY_IN_LOBBY":
                case "LOBBY_FULL":
                case "LOBBY_CLOSED":
                case "MATCH_NOT_FOUND":
                case "NOT_IN_LOBBY":
                case "INVALID_PASSWORD":
                case "INVALID_LOBBY_PASSWORD":
                case "NOT_HOST":
                case "NOT_ENOUGH_PLAYERS":
                case "MATCHMAKING_FAILED":
                case "DAILY_LIMIT_REACHED":
                    return new ApiErrorInfo(code, ApiErrorCategory.Lobby, FeedbackSeverity.Toast, code == "MATCHMAKING_FAILED");
                case "INSUFFICIENT_FUNDS":
                case "SKIN_NOT_FOUND":
                case "SKIN_NOT_OWNED":
                case "NOT_FOUND":
                    return new ApiErrorInfo(code, ApiErrorCategory.Economy, FeedbackSeverity.Toast, false);
                default:
                    if (code.StartsWith("HTTP_503", StringComparison.Ordinal) || code.StartsWith("HTTP_502", StringComparison.Ordinal))
                        return new ApiErrorInfo(code, ApiErrorCategory.Service, FeedbackSeverity.Blocking, true);
                    if (code.StartsWith("HTTP_", StringComparison.Ordinal))
                        return new ApiErrorInfo(code, ApiErrorCategory.Unknown, FeedbackSeverity.Toast, true);
                    return new ApiErrorInfo(code, ApiErrorCategory.Gameplay, FeedbackSeverity.Toast, false);
            }
        }

        public static ApiErrorCategory ParseCategory(string category)
        {
            if (string.IsNullOrWhiteSpace(category))
                return ApiErrorCategory.Unknown;

            switch (category.Trim().ToLowerInvariant())
            {
                case "service": return ApiErrorCategory.Service;
                case "auth": return ApiErrorCategory.Auth;
                case "validation": return ApiErrorCategory.Validation;
                case "gameplay": return ApiErrorCategory.Gameplay;
                case "lobby": return ApiErrorCategory.Lobby;
                case "economy": return ApiErrorCategory.Economy;
                case "transport": return ApiErrorCategory.Transport;
                default: return ApiErrorCategory.Unknown;
            }
        }

        public static bool PreferGlobal(ApiErrorInfo info)
        {
            return info.Severity == FeedbackSeverity.Banner
                || info.Severity == FeedbackSeverity.Modal
                || info.Severity == FeedbackSeverity.Blocking
                || info.Category == ApiErrorCategory.Transport
                || info.Category == ApiErrorCategory.Service
                || info.Category == ApiErrorCategory.Auth;
        }
    }
}
