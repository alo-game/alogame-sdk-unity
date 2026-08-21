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
        public void Initialize()
        {
            Debug.Log("[Alogame Mock SDK] Initialized (Editor/standalone preview mode).");
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
                ["data"] = new Dictionary<string, object>(),
            };
            AlogameBridge.HandleNativeResult(Json.Serialize(response));
        }
    }
}
