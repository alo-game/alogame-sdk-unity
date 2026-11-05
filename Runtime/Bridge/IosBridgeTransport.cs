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
        // C `int` + one-byte C `bool`, matching `AlogameUnity_Setup(_: Int32, _: Bool)` in
        // AlogameUnityBridge.swift. Without I1, a C# bool marshals as a 4-byte Win32 BOOL.
        [DllImport("__Internal")]
        private static extern void AlogameUnity_Setup(int gameId, [MarshalAs(UnmanagedType.I1)] bool debugEnv);

        [DllImport("__Internal")]
        private static extern void AlogameUnity_SendToNative(string jsonPayload);

        public void Initialize(int gameId, bool debugEnv) => AlogameUnity_Setup(gameId, debugEnv);

        public void SendToNative(string jsonPayload) => AlogameUnity_SendToNative(jsonPayload);
    }
}
#endif
