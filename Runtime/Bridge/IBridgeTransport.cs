namespace Alogame.SDK.Bridge
{
    /// <summary>
    /// Per-platform transport for the bridge's JSON method-string protocol. Mirrors
    /// `BridgeAdapter.ts` in the Egret/Cocos bridge — the protocol itself
    /// (`{method, params, callbackId}` in, `{callbackId, success, data}` out) is identical
    /// across all three hosts (web, Egret/Cocos, Unity); only the transport differs.
    /// </summary>
    internal interface IBridgeTransport
    {
        /// <summary>One-time setup — wires the native SDK bootstrap and the result callback.</summary>
        void Initialize();

        /// <summary>Sends a serialized `{method, params, callbackId}` JSON payload to native.</summary>
        void SendToNative(string jsonPayload);
    }
}
