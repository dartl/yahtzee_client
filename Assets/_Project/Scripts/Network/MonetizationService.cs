using System;
using System.Globalization;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Yahtzee.Core;
using Yahtzee.Feedback;
using Yahtzee.Localization;

namespace Yahtzee.Network
{
    /// <summary>
    /// Client façade for IAP + rewarded ads. Store SDKs plug in here;
    /// grants always go through Laravel verification endpoints.
    /// </summary>
    public static class MonetizationService
    {
        public static bool AdsAllowed
        {
            get
            {
                var user = NetworkManager.Instance != null ? NetworkManager.Instance.CurrentUser : null;
                return user == null || !user.IsAdFree;
            }
        }

        public static async Task<EconomyCatalogDto> LoadCatalogAsync()
        {
            if (NetworkManager.Instance == null)
                throw new InvalidOperationException("NetworkManager missing.");
            return await NetworkManager.Instance.GetEconomyCatalog();
        }

        public static async Task<bool> PurchaseProductAsync(string productId)
        {
            if (NetworkManager.Instance == null || string.IsNullOrWhiteSpace(productId))
                return false;

            // Editor / stub: sign a dev receipt. Replace with Unity IAP purchase flow in production.
            var tx = "dev-" + Guid.NewGuid().ToString("N");
            var receipt = "dev:" + productId + ":" + tx;
            try
            {
                await NetworkManager.Instance.RedeemIap(productId, tx, Application.platform.ToString(), receipt);
                PlayerFeedback.Report(new FeedbackEvent(
                    "IAP_OK",
                    GameText.Get("ui.shop.iap_ok"),
                    FeedbackSeverity.Toast,
                    ApiErrorCategory.Economy,
                    false));
                return true;
            }
            catch (Exception exception)
            {
                PlayerFeedback.FromException(exception);
                return false;
            }
        }

        public static async Task<bool> ShowRewardedAdAsync()
        {
            if (!AdsAllowed)
            {
                PlayerFeedback.Report(new FeedbackEvent(
                    "AD_FREE",
                    GameText.Get("ui.shop.ad_free_active"),
                    FeedbackSeverity.Toast,
                    ApiErrorCategory.Economy,
                    false));
                return false;
            }

            if (NetworkManager.Instance == null || NetworkManager.Instance.CurrentUser == null)
                return false;

            // Stub: pretend an ad completed, then redeem with HMAC-compatible server secret in editor only.
            // Production: AdMob/Unity Ads callback → server-signed impression or Play/App Store SSV.
            var impression = "imp-" + Guid.NewGuid().ToString("N");
            var userId = NetworkManager.Instance.CurrentUser.Id.ToString(CultureInfo.InvariantCulture);
            var signature = DevHmac(userId + "|" + impression);
            try
            {
                await NetworkManager.Instance.RedeemRewardedAd(impression, signature);
                PlayerFeedback.Report(new FeedbackEvent(
                    "AD_REWARD_OK",
                    GameText.Get("ui.shop.ad_reward_ok"),
                    FeedbackSeverity.Toast,
                    ApiErrorCategory.Economy,
                    false));
                return true;
            }
            catch (Exception exception)
            {
                PlayerFeedback.FromException(exception);
                return false;
            }
        }

        public static void TryShowInterstitialAfterMatch()
        {
            if (!AdsAllowed)
                return;
            // Production: load/show interstitial. Stub logs only.
            Debug.Log("Yahtzee ads: interstitial placeholder after match.");
        }

        private static string DevHmac(string payload)
        {
            // Must match YAHTZEE_ADS_REWARD_SECRET default on server for editor testing.
            var key = System.Text.Encoding.UTF8.GetBytes("yahtzee-ads-dev");
            using (var hmac = new System.Security.Cryptography.HMACSHA256(key))
            {
                var hash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(payload));
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }
    }

    public sealed class EconomyCatalogDto
    {
        public EconomyProductDto[] Products { get; set; }
        public int RewardedAdCoins { get; set; }
    }

    public sealed class EconomyProductDto
    {
        public string ProductId { get; set; }
        public int? SoftCurrency { get; set; }
        public int? AdFreeDays { get; set; }
        public string Label { get; set; }
    }
}
