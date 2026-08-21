using System.Collections.Generic;
using System.Threading.Tasks;

namespace Alogame.SDK
{
    /// <summary>
    /// Mirrors `AlogamePush.ts` 1:1. Wired on both platforms, with two known platform gaps
    /// (native SDK limitations, not bridge gaps — see SDK-ALOGAME-BRIDGE-RELEASE-PLAN.md §2.2c):
    /// <list type="bullet">
    /// <item><see cref="GetDeviceToken"/> always resolves null on iOS — no public getter for the
    /// current token there (registration-driven, not pull-based like Android).</item>
    /// <item><see cref="SubscribeToTopic"/>/<see cref="UnsubscribeFromTopic"/> reject on iOS (FCM
    /// topics have no APNs equivalent) — Android-only by design.</item>
    /// </list>
    /// Zero call sites in either reference app this bridge was validated against — treat as less
    /// battle-tested than Auth/Payment/Analytics.
    /// </summary>
    public static class AlogamePush
    {
        public static async Task<bool> RequestPermission()
        {
            var raw = await Bridge.AlogameBridge.CallNative("push.requestPermission", null);
            return JsonField.Bool(raw as Dictionary<string, object>, "granted");
        }

        /// <remarks>Always resolves null on iOS — see class remarks.</remarks>
        public static async Task<string> GetDeviceToken(bool forceRefresh = false)
        {
            var raw = await Bridge.AlogameBridge.CallNative("push.getDeviceToken", new Dictionary<string, object> { ["forceRefresh"] = forceRefresh });
            return JsonField.Str(raw as Dictionary<string, object>, "token");
        }

        /// <summary>Android-only (FCM topic subscription) — rejects on iOS.</summary>
        public static Task SubscribeToTopic(string topic)
        {
            return Bridge.AlogameBridge.CallNative("push.subscribeToTopic", new Dictionary<string, object> { ["topic"] = topic });
        }

        /// <summary>Android-only (FCM topic subscription) — rejects on iOS.</summary>
        public static Task UnsubscribeFromTopic(string topic)
        {
            return Bridge.AlogameBridge.CallNative("push.unsubscribeFromTopic", new Dictionary<string, object> { ["topic"] = topic });
        }
    }
}
