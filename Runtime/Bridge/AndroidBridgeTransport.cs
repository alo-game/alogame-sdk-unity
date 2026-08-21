#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine;

namespace Alogame.SDK.Bridge
{
    /// <summary>
    /// Calls into `com.alogame.unity.AlogameUnityBridge` (thin plugin class shipped as
    /// Plugins/Android/AlogameUnityBridge.java), which in turn calls the shared
    /// `vn.oeg.sdk.bridge.OEGBridge.setupHeadless`/`sendToNative` — the same bridge glue the
    /// Egret/Cocos `alogame-sdk` package uses, just with a Unity-flavored front door.
    /// </summary>
    internal sealed class AndroidBridgeTransport : IBridgeTransport
    {
        private const string BridgeClass = "com.alogame.unity.AlogameUnityBridge";

        public void Initialize()
        {
            using var activity = GetCurrentActivity();
            using var clazz = new AndroidJavaClass(BridgeClass);
            clazz.CallStatic("setup", activity);
        }

        public void SendToNative(string jsonPayload)
        {
            using var clazz = new AndroidJavaClass(BridgeClass);
            clazz.CallStatic("sendToNative", jsonPayload);
        }

        private static AndroidJavaObject GetCurrentActivity()
        {
            using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            return unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
        }
    }
}
#endif
