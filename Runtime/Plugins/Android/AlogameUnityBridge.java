package com.alogame.unity;

import android.app.Activity;
import com.unity3d.player.UnityPlayer;
import vn.oeg.sdk.bridge.OEGBridge;

/**
 * Thin Unity-facing front door for the shared alogame-sdk native bridge glue
 * ({@code vn.oeg.sdk.bridge.OEGBridge}) — the same class the Egret/Cocos {@code alogame-sdk}
 * npm package calls into. Unity has no EgretNativeAndroid instance, so this uses OEGBridge's
 * headless entry point instead and relays native results back into C# via UnitySendMessage.
 *
 * Called from C# via AndroidBridgeTransport (AndroidJavaClass — no AndroidManifest wiring
 * needed since Unity's own Activity hosts this).
 */
public final class AlogameUnityBridge {

    private static final String CALLBACK_GAME_OBJECT = "AlogameSDKCallbackReceiver";
    private static final String CALLBACK_METHOD = "OnNativeMessage";

    private AlogameUnityBridge() {}

    public static void setup(Activity activity) {
        OEGBridge.setupHeadless(activity, json ->
            UnityPlayer.UnitySendMessage(CALLBACK_GAME_OBJECT, CALLBACK_METHOD, json)
        );
    }

    public static void sendToNative(String jsonPayload) {
        OEGBridge.sendToNative(jsonPayload);
    }
}
