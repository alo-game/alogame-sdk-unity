using System.Collections.Generic;
using System.Threading.Tasks;

namespace Alogame.SDK
{
    /// <summary>
    /// Mirrors `AlogamePush.ts`. Push needs platform setup the build hooks do for you on iOS
    /// (push entitlement, remote-notification background mode, APNs token forwarding) and a
    /// Firebase config on Android (see README). Platform differences:
    /// <list type="bullet">
    /// <item><see cref="GetDeviceToken"/> on iOS returns the APNs token (hex) once iOS has handed
    /// one to the app, i.e. after <see cref="RequestPermission"/> was granted; null before.
    /// Android returns the FCM token.</item>
    /// <item><see cref="SubscribeToTopic"/>/<see cref="UnsubscribeFromTopic"/> are FCM topics —
    /// Android only; they reject on iOS.</item>
    /// </list>
    /// </summary>
    public static class AlogamePush
    {
        public static async Task<bool> RequestPermission()
        {
            var raw = await Bridge.AlogameBridge.CallNative("push.requestPermission", null);
            return JsonField.Bool(raw as Dictionary<string, object>, "granted");
        }

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

        /// <summary>
        /// A push arrived while the game runs, or the player opened the game from one (iOS).
        /// The payload is the push's data: string values on Android; the full APNs payload on
        /// iOS (including the nested <c>aps</c> dictionary). The SDK has already shown it.
        /// Main thread.
        /// </summary>
        public static event System.Action<Dictionary<string, object>> MessageReceived;

        /// <summary>The device's push token changed (FCM on Android, APNs hex on iOS). Main thread.</summary>
        public static event System.Action<string> TokenRefreshed;

        internal static void DispatchMessageReceived(Dictionary<string, object> data) => Raise(MessageReceived, data);
        internal static void DispatchTokenRefreshed(string token) => Raise(TokenRefreshed, token);

        private static void Raise<T>(System.Action<T> handlers, T value)
        {
            if (handlers == null) return;
            foreach (var handler in handlers.GetInvocationList())
            {
                try { ((System.Action<T>)handler)(value); }
                catch (System.Exception e) { UnityEngine.Debug.LogException(e); }
            }
        }
    }
}
