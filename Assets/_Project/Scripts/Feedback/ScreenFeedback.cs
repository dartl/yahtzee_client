using System;
using UnityEngine;
using UnityEngine.UI;
using Yahtzee.Core;
using Yahtzee.Localization;
using Yahtzee.Network;

namespace Yahtzee.Feedback
{
    /// <summary>
    /// Inline status helper for screen labels. Routes transport/service/auth to PlayerFeedback.
    /// </summary>
    public static class ScreenFeedback
    {
        private static readonly Color ErrorColor = new Color(0.92f, 0.35f, 0.32f, 1f);

        public static void SetInfo(Text label, string textKey, params object[] args)
        {
            if (label == null)
                return;

            label.color = UIThemeConfig.TextMuted;
            label.text = args != null && args.Length > 0
                ? GameText.Format(textKey, args)
                : GameText.Get(textKey);
        }

        public static void SetRawInfo(Text label, string localizedText)
        {
            if (label == null)
                return;

            label.color = UIThemeConfig.TextMuted;
            label.text = localizedText ?? string.Empty;
        }

        public static void SetError(Text label, Exception exception)
        {
            if (exception is NetworkException network)
            {
                SetError(label, network);
                return;
            }

            if (label == null)
                return;

            label.color = ErrorColor;
            label.text = GameText.FromException(exception);
        }

        public static void SetError(Text label, NetworkException network)
        {
            if (network == null)
            {
                Clear(label);
                return;
            }

            var info = ApiErrorCatalog.Describe(network.ErrorCode);
            if (!string.IsNullOrEmpty(network.Category))
            {
                var category = ApiErrorCatalog.ParseCategory(network.Category);
                info = new ApiErrorInfo(info.Code, category, info.Severity, network.Retryable ?? info.Retryable);
            }

            if (ApiErrorCatalog.PreferGlobal(info))
            {
                PlayerFeedback.FromNetwork(network);
                if (label != null)
                {
                    label.color = ErrorColor;
                    label.text = GameText.FromError(network.ErrorCode, network.Message);
                }

                return;
            }

            if (label == null)
            {
                PlayerFeedback.FromNetwork(network, FeedbackSeverity.Toast);
                return;
            }

            label.color = ErrorColor;
            label.text = GameText.FromError(network.ErrorCode, network.Message);
        }

        public static void SetErrorKey(Text label, string textKey)
        {
            if (label == null)
                return;

            label.color = ErrorColor;
            label.text = GameText.Get(textKey);
        }

        public static void Clear(Text label)
        {
            if (label == null)
                return;

            label.text = string.Empty;
            label.color = UIThemeConfig.TextMuted;
        }
    }
}
