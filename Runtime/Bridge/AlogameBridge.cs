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

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            AlogameCallbackReceiver.EnsureExists();
            Transport.Initialize();
        }

        public static Task<object> CallNative(string method, Dictionary<string, object> parameters)
        {
            var tcs = new TaskCompletionSource<object>();
            string callbackId = CallbackRouter.Register(tcs);
            var payload = new Dictionary<string, object>
            {
                ["method"] = method,
                ["params"] = parameters ?? new Dictionary<string, object>(),
                ["callbackId"] = callbackId,
            };

            Debug.Log($"[Alogame SDK] JS -> Native: method={method}");
            Transport.SendToNative(Json.Serialize(payload));
            return tcs.Task;
        }

        /// <summary>Invoked by <see cref="AlogameCallbackReceiver"/> when native delivers a response.</summary>
        internal static void HandleNativeResult(string json)
        {
            if (Json.Deserialize(json) is not Dictionary<string, object> response) return;

            string callbackId = response.TryGetValue("callbackId", out var cb) ? cb as string : null;
            bool success = response.TryGetValue("success", out var s) && s is bool b && b;
            response.TryGetValue("data", out var data);

            CallbackRouter.Resolve(callbackId, success, data);
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
