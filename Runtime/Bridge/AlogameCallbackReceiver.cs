using UnityEngine;

namespace Alogame.SDK.Bridge
{
    /// <summary>
    /// `UnitySendMessage` target for native → Unity delivery. The Android plugin
    /// (`com.alogame.unity.AlogameUnityBridge`) and the iOS bridge (`AlogameUnityBridge.swift`) both call
    /// `UnitySendMessage(GameObjectName, "OnNativeMessage", json)` on this object's name — it must
    /// stay alive (and keep this exact name) for the lifetime of the app.
    /// </summary>
    [AddComponentMenu("")]
    internal sealed class AlogameCallbackReceiver : MonoBehaviour
    {
        public const string GameObjectName = "AlogameSDKCallbackReceiver";

        private static AlogameCallbackReceiver _instance;

        internal static void EnsureExists()
        {
            if (_instance != null) return;
            var go = new GameObject(GameObjectName);
            Object.DontDestroyOnLoad(go);
            _instance = go.AddComponent<AlogameCallbackReceiver>();
        }

        // Called via UnitySendMessage from native — method name and signature are load-bearing.
        public void OnNativeMessage(string json)
        {
            AlogameBridge.HandleNativeResult(json);
        }
    }
}
