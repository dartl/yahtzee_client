using System;
using Yahtzee.Localization;
using Yahtzee.Network;

namespace Yahtzee.Feedback
{
    public enum ServiceUiStatus
    {
        Ok,
        Degraded,
        Maintenance,
        Offline,
        VersionBlocked,
        Banned
    }

    public readonly struct FeedbackEvent
    {
        public readonly string Code;
        public readonly string Message;
        public readonly FeedbackSeverity Severity;
        public readonly ApiErrorCategory Category;
        public readonly bool Retryable;
        public readonly int? RetryAfterSeconds;

        public FeedbackEvent(
            string code,
            string message,
            FeedbackSeverity severity,
            ApiErrorCategory category,
            bool retryable,
            int? retryAfterSeconds = null)
        {
            Code = code ?? ApiErrorCode.UnknownError;
            Message = message ?? string.Empty;
            Severity = severity;
            Category = category;
            Retryable = retryable;
            RetryAfterSeconds = retryAfterSeconds;
        }
    }

    /// <summary>
    /// Global player-facing feedback bus. Screens use ScreenFeedback for inline labels;
    /// transport/service/auth problems always go through here.
    /// </summary>
    public static class PlayerFeedback
    {
        public static event Action<FeedbackEvent> OnEvent;
        public static event Action<ServiceUiStatus, string, int?> OnServiceStatusChanged;

        public static ServiceUiStatus ServiceStatus { get; private set; } = ServiceUiStatus.Ok;

        public static void Report(FeedbackEvent feedback)
        {
            if (feedback.Severity == FeedbackSeverity.Silent)
                return;

            OnEvent?.Invoke(feedback);
        }

        public static void FromException(Exception exception, FeedbackSeverity? forceSeverity = null)
        {
            if (exception is NetworkException network)
            {
                FromNetwork(network, forceSeverity);
                return;
            }

            var message = GameText.FromException(exception);
            Report(new FeedbackEvent(
                ApiErrorCode.UnknownError,
                message,
                forceSeverity ?? FeedbackSeverity.Toast,
                ApiErrorCategory.Unknown,
                true));
        }

        public static void FromNetwork(NetworkException network, FeedbackSeverity? forceSeverity = null)
        {
            if (network == null)
                return;

            var info = ApiErrorCatalog.Describe(network.ErrorCode);
            if (!string.IsNullOrEmpty(network.Category))
            {
                var parsed = ApiErrorCatalog.ParseCategory(network.Category);
                info = new ApiErrorInfo(info.Code, parsed, info.Severity, network.Retryable ?? info.Retryable);
            }

            var severity = forceSeverity ?? info.Severity;
            var message = GameText.FromError(network.ErrorCode, network.Message);
            Report(new FeedbackEvent(
                network.ErrorCode,
                message,
                severity,
                info.Category,
                network.Retryable ?? info.Retryable,
                network.RetryAfterSeconds));

            if (info.Category == ApiErrorCategory.Service || info.Category == ApiErrorCategory.Auth)
                ApplyServiceFromCode(network.ErrorCode, message, network.RetryAfterSeconds);
        }

        public static void ReportCode(string code, string serverFallback = null, int? retryAfter = null)
        {
            var info = ApiErrorCatalog.Describe(code);
            var message = GameText.FromError(code, serverFallback);
            Report(new FeedbackEvent(code, message, info.Severity, info.Category, info.Retryable, retryAfter));
            if (ApiErrorCatalog.PreferGlobal(info))
                ApplyServiceFromCode(code, message, retryAfter);
        }

        public static void SetServiceStatus(ServiceUiStatus status, string message = null, int? retryAfterSeconds = null)
        {
            ServiceStatus = status;
            OnServiceStatusChanged?.Invoke(status, message ?? string.Empty, retryAfterSeconds);
        }

        public static void ClearServiceStatus()
        {
            if (ServiceStatus == ServiceUiStatus.Ok)
                return;

            ServiceStatus = ServiceUiStatus.Ok;
            OnServiceStatusChanged?.Invoke(ServiceUiStatus.Ok, string.Empty, null);
        }

        public static void ApplyServiceDto(ServiceStatusDto dto)
        {
            if (dto == null)
            {
                SetServiceStatus(ServiceUiStatus.Offline, GameText.Get("service.body_offline"), null);
                return;
            }

            switch ((dto.Status ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "maintenance":
                    SetServiceStatus(ServiceUiStatus.Maintenance, GameText.FromError(ApiErrorCode.ServiceMaintenance, dto.Message), dto.RetryAfterSeconds);
                    break;
                case "degraded":
                    SetServiceStatus(ServiceUiStatus.Degraded, GameText.FromError(ApiErrorCode.ServiceDegraded, dto.Message), dto.RetryAfterSeconds);
                    break;
                default:
                    ClearServiceStatus();
                    break;
            }
        }

        private static void ApplyServiceFromCode(string code, string message, int? retryAfter)
        {
            switch (code)
            {
                case ApiErrorCode.ServiceMaintenance:
                case ApiErrorCode.ServiceOverloaded:
                case ApiErrorCode.ServiceUnavailable:
                    SetServiceStatus(ServiceUiStatus.Maintenance, message, retryAfter);
                    break;
                case ApiErrorCode.ServiceDegraded:
                    SetServiceStatus(ServiceUiStatus.Degraded, message, retryAfter);
                    break;
                case ApiErrorCode.VersionUnsupported:
                    SetServiceStatus(ServiceUiStatus.VersionBlocked, message, retryAfter);
                    break;
                case ApiErrorCode.UserBanned:
                    SetServiceStatus(ServiceUiStatus.Banned, message, retryAfter);
                    break;
                case ApiErrorCode.NetworkUnreachable:
                case ApiErrorCode.RequestTimeout:
                case ApiErrorCode.SocketClosed:
                    SetServiceStatus(ServiceUiStatus.Offline, message, retryAfter);
                    break;
            }
        }
    }
}
