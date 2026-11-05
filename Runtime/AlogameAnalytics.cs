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

        /// <summary>
        /// Pass a link to the attribution provider. <see cref="AlogameSDK.Initialize"/> already
        /// does this for every <c>Application.deepLinkActivated</c> link and for the launch
        /// <c>Application.absoluteURL</c>, so call it only for links your game receives some other way.
        /// </summary>
        public static Task ProcessDeeplink(string url)
        {
            return Bridge.AlogameBridge.CallNative("analytics.processDeeplink", new Dictionary<string, object> { ["url"] = url });
        }

        private static System.Action<Dictionary<string, object>> _deepLinkResolved;
        private static Dictionary<string, object> _pendingDeepLink;

        /// <summary>
        /// AppsFlyer OneLink deep links (direct and deferred), as AppsFlyer's raw click-event
        /// payload: <c>deep_link_value</c>, <c>deep_link_sub1</c>…<c>deep_link_sub10</c>,
        /// <c>is_deferred</c> (bool), <c>media_source</c>, <c>campaign</c>, ... Every key is
        /// optional and may be null. Requires AppsFlyer as the game's attribution provider.
        /// Raised on the main thread. A link resolved before anything subscribed — the usual
        /// timing for a deferred deep link on first launch — is delivered once to the first
        /// subscriber, during the <c>+=</c>.
        /// </summary>
        public static event System.Action<Dictionary<string, object>> DeepLinkResolved
        {
            add
            {
                _deepLinkResolved += value;
                var held = _pendingDeepLink;
                if (held == null || value == null) return;
                _pendingDeepLink = null;
                Invoke(value, held);
            }
            remove { _deepLinkResolved -= value; }
        }

        internal static void DispatchDeepLinkResolved(Dictionary<string, object> data)
        {
            var handlers = _deepLinkResolved;
            if (handlers == null)
            {
                _pendingDeepLink = data;
                return;
            }
            foreach (var handler in handlers.GetInvocationList())
            {
                Invoke((System.Action<Dictionary<string, object>>)handler, data);
            }
        }

        // One throwing subscriber must not stop the others, nor unwind into the bridge.
        private static void Invoke(System.Action<Dictionary<string, object>> handler, Dictionary<string, object> data)
        {
            try { handler(data); }
            catch (System.Exception e) { UnityEngine.Debug.LogException(e); }
        }
    }
}
