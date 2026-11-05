using System.Collections.Generic;
using UnityEngine;

namespace Alogame.SDK.Bridge
{
    /// <summary>
    /// Editor/standalone fallback — mirrors `MockBridge.ts`. There is no native SDK to call into
    /// outside a real Android/iOS device build, so every call resolves immediately with an empty
    /// success payload. This lets game code run (and UI flow through) in the Editor without
    /// special-casing every SDK call, at the cost of never exercising the real auth/payment logic.
    /// </summary>
    internal sealed class MockBridgeTransport : IBridgeTransport
    {
        public void Initialize(int gameId, bool debugEnv)
        {
            Debug.Log($"[Alogame Mock SDK] Initialized for game {gameId} (debugEnv={debugEnv}, Editor/standalone preview mode).");
        }

        public void SendToNative(string jsonPayload)
        {
            if (Json.Deserialize(jsonPayload) is not Dictionary<string, object> payload) return;
            string callbackId = payload.TryGetValue("callbackId", out var cb) ? cb as string : null;
            string method = payload.TryGetValue("method", out var m) ? m as string : "unknown";
            if (callbackId == null) return;

            Debug.Log($"[Alogame Mock SDK] callNative({method}) -> mock success");
            var response = new Dictionary<string, object>
            {
                ["callbackId"] = callbackId,
                ["success"] = true,
                ["data"] = MockData(method),
            };
            AlogameBridge.HandleNativeResult(Json.Serialize(response));
        }

        private static readonly HashSet<string> UserMethods = new()
        {
            "auth.login", "auth.register", "auth.playNow", "auth.socialLogin", "auth.merge",
            "auth.mergeSocial", "auth.showLoginUI", "auth.fetchUserInfo", "auth.updateProfile",
            "auth.linkAloId", "auth.getCurrentUser", "ui.showUpdateProfile",
        };

        /// <summary>Shaped like the native bridges' payloads so game code takes its success path.</summary>
        private static object MockData(string method)
        {
            if (UserMethods.Contains(method))
            {
                return new Dictionary<string, object>
                {
                    ["id"] = "mock-uuid", ["uuid"] = "mock-uuid", ["userId"] = "0",
                    ["username"] = "editor", ["displayName"] = "Editor", ["token"] = "mock-token",
                    ["isPlayNow"] = false, ["loginType"] = "oeg", ["accountStatus"] = "active",
                };
            }
            if (method == "payment.queryProducts") return new List<object>();
            return new Dictionary<string, object>();
        }
    }
}
