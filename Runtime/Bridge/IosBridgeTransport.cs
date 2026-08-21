#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;

namespace Alogame.SDK.Bridge
{
    /// <summary>
    /// Calls into `AlogameUnityBridge.swift` (Plugins/iOS), a thin `@_cdecl` wrapper around the
    /// shared `OEGBridge.swift` — the same bridge glue the Egret/Cocos `alogame-sdk` package
    /// uses, just with a Unity-flavored front door (see `OEGBridge.setupHeadless`).
    ///
    /// The two symbol names below are the contract with that file's `@_cdecl` attributes;
    /// renaming one without the other fails at link time, not compile time.
    /// </summary>
    internal sealed class IosBridgeTransport : IBridgeTransport
    {
        [DllImport("__Internal")]
        private static extern void AlogameUnity_Setup();

        [DllImport("__Internal")]
        private static extern void AlogameUnity_SendToNative(string jsonPayload);

        public void Initialize() => AlogameUnity_Setup();

        public void SendToNative(string jsonPayload) => AlogameUnity_SendToNative(jsonPayload);
    }
}
#endif
