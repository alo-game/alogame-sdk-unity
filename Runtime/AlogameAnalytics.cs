using System.Collections.Generic;
using System.Threading.Tasks;

namespace Alogame.SDK
{
    /// <summary>
    /// Mirrors `AlogameAnalytics.ts` 1:1. Calls straight through to native `OEGAnalytics`
    /// (Adjust, with optional Firebase composite). <see cref="LogEvent"/>/<see cref="LogRevenue"/>
    /// are the two methods real game integrations actually use in practice; the rest are
    /// supported for parity.
    /// </summary>
    public static class AlogameAnalytics
    {
        public static Task LogEvent(string eventNameOrToken, Dictionary<string, object> parameters = null)
        {
            return Bridge.AlogameBridge.CallNative("analytics.logEvent", new Dictionary<string, object>
            {
                ["eventNameOrToken"] = eventNameOrToken,
                ["parameters"] = parameters,
            });
        }

        public static Task LogPurchase(string eventToken, double price, string currency, string transactionId = null, string productId = null)
        {
            return Bridge.AlogameBridge.CallNative("analytics.logPurchase", new Dictionary<string, object>
            {
                ["eventToken"] = eventToken,
                ["price"] = price,
                ["currency"] = currency,
                ["transactionId"] = transactionId,
                ["productId"] = productId,
            });
        }

        public static Task LogRevenue(string eventNameOrToken, double revenue, string currency, string orderId = null, Dictionary<string, object> parameters = null)
        {
            return Bridge.AlogameBridge.CallNative("analytics.logRevenue", new Dictionary<string, object>
            {
                ["eventNameOrToken"] = eventNameOrToken,
                ["revenue"] = revenue,
                ["currency"] = currency,
                ["orderId"] = orderId,
                ["parameters"] = parameters,
            });
        }

        /// <summary>GDPR / consent toggle for third-party tracking (Adjust, etc).</summary>
        public static Task SetTrackingEnabled(bool enabled)
        {
            return Bridge.AlogameBridge.CallNative("analytics.setTrackingEnabled", new Dictionary<string, object> { ["enabled"] = enabled });
        }

        public static async Task<bool> IsInitialized()
        {
            var raw = await Bridge.AlogameBridge.CallNative("analytics.isInitialized", null);
            return JsonField.Bool(raw as Dictionary<string, object>, "initialized");
        }
    }
}
