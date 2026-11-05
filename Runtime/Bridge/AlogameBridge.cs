using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Alogame.SDK.Bridge
{
    /// <summary>
    /// Engine-side half of the bridge protocol — mirrors `AlogameSdk.bridge.callNative` +
    /// `CallbackRouter` in the Egret/Cocos TS bridge. Picks the right <see cref="IBridgeTransport"/>
    /// for the running platform and turns `{method, params}` calls into awaitable Tasks.
    /// </summary>
    internal static class AlogameBridge
    {
        private static IBridgeTransport _transport;
        private static bool _initialized;

        public static IBridgeTransport Transport => _transport ??= CreateTransport();

        public static void Initialize(int gameId, bool debugEnv)
        {
            if (_initialized) return;
            _initialized = true;
            AlogameCallbackReceiver.EnsureExists();
            Transport.Initialize(gameId, debugEnv);
        }

        public static Task<object> CallNative(string method, Dictionary<string, object> parameters)
        {
            var tcs = new TaskCompletionSource<object>();
            string callbackId = CallbackRouter.Register(tcs);
            var payload = new Dictionary<string, object>
            {
                ["method"] = method,
                ["params"] = WithoutNulls(parameters) ?? new Dictionary<string, object>(),
                ["callbackId"] = callbackId,
            };

            Debug.Log($"[Alogame SDK] JS -> Native: method={method}");
            Transport.SendToNative(Json.Serialize(payload));
            return tcs.Task;
        }

        /// <summary>
        /// Omitted optionals are left out instead of sent as JSON null. Android's org.json turns
        /// null into the string "null" for optString and throws on getInt, so an explicit null
        /// reached the SDK as text or hung the call. Nested dictionaries (gameData) too.
        /// </summary>
        private static Dictionary<string, object> WithoutNulls(Dictionary<string, object> source)
        {
            if (source == null) return null;
            var result = new Dictionary<string, object>(source.Count);
            foreach (var kvp in source)
            {
                if (kvp.Value == null) continue;
                result[kvp.Key] = kvp.Value is Dictionary<string, object> nested ? WithoutNulls(nested) : kvp.Value;
            }
            return result;
        }

        /// <summary>Invoked by <see cref="AlogameCallbackReceiver"/> when native delivers a response.</summary>
        internal static void HandleNativeResult(string json)
        {
            if (Json.Deserialize(json) is not Dictionary<string, object> response) return;

            // Native-initiated events carry `event` instead of a callbackId.
            if (response.TryGetValue("event", out var ev) && ev is string eventName)
            {
                response.TryGetValue("data", out var eventData);
                DispatchEvent(eventName, eventData as Dictionary<string, object>);
                return;
            }

            string callbackId = response.TryGetValue("callbackId", out var cb) ? cb as string : null;
            bool success = response.TryGetValue("success", out var s) && s is bool b && b;
            response.TryGetValue("data", out var data);

            CallbackRouter.Resolve(callbackId, success, data);
        }

        private static void DispatchEvent(string eventName, Dictionary<string, object> data)
        {
            switch (eventName)
            {
                case "analytics.deepLinkResolved":
                    AlogameAnalytics.DispatchDeepLinkResolved(data ?? new Dictionary<string, object>());
                    break;
                case "auth.loggedIn":
                    AlogameAuth.DispatchLoggedIn(AuthUser.FromJson(data));
                    break;
                case "auth.sessionEnded":
                    AlogameAuth.DispatchSessionEnded(JsonField.Str(data, "message"));
                    break;
                case "push.messageReceived":
                    AlogamePush.DispatchMessageReceived(data ?? new Dictionary<string, object>());
                    break;
                case "push.tokenRefreshed":
                    AlogamePush.DispatchTokenRefreshed(JsonField.Str(data, "token"));
                    break;
                default:
                    Debug.LogWarning($"[Alogame SDK] Unknown native event '{eventName}' ignored.");
                    break;
            }
        }

        private static IBridgeTransport CreateTransport()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return new AndroidBridgeTransport();
#elif UNITY_IOS && !UNITY_EDITOR
            return new IosBridgeTransport();
#else
            Debug.LogWarning("[Alogame SDK] Not running on Android/iOS device — using Mock Bridge (Editor/standalone preview).");
            return new MockBridgeTransport();
#endif
        }
    }
}
