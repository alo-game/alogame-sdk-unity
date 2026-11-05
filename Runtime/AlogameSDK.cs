using System.Threading.Tasks;

namespace Alogame.SDK
{
    /// <summary>
    /// Entry point — mirrors the native `AlogameSdk.init(context, gameId, debugEnv)` (Android) and
    /// `OegSdkCore.Builder().setGameId(_:).setDebugEnv(_:).build()` (iOS). Call
    /// <see cref="Initialize"/> once when your game boots (e.g. from a bootstrap MonoBehaviour's
    /// Awake), before any other Alogame.SDK.* call.
    /// </summary>
    public static class AlogameSDK
    {
        public const string Version = "1.0.0";

        private static bool _initialized;

        /// <summary>
        /// Wires the native bridge and bootstraps the SDK (Auth/Payment/Analytics/Push/Account).
        /// Only the game id is passed in; social login credentials, feature flags and tracking
        /// are configured per game by Alogame on the server and loaded at runtime.
        /// </summary>
        /// <param name="gameId">Your game id from the Alogame Operation Manager.</param>
        /// <param name="debugEnv">
        /// true = the dev backend (api-sdk.dev.alogame.vn) with verbose HTTP logging. Never ship a
        /// release build with it on.
        /// </param>
        public static void Initialize(int gameId, bool debugEnv = false)
        {
            if (gameId <= 0)
                throw new System.ArgumentOutOfRangeException(nameof(gameId), gameId,
                    "Alogame game id must be positive — get it from your Alogame Operation Manager.");
            if (_initialized) return;
            _initialized = true;
            Bridge.AlogameBridge.Initialize(gameId, debugEnv);

#if UNITY_ANDROID && !UNITY_EDITOR
            // Unity's activity receives App Links and URL schemes, so the SDK's own entry
            // activity never sees them. Forward them. On iOS AlogameAppDelegateListener.mm hands
            // every opened URL to OEGManager.handleOpenURL natively (social-login callbacks and
            // attribution), so forwarding here as well would report each link twice.
            UnityEngine.Application.deepLinkActivated += ForwardDeepLink;
            if (!string.IsNullOrEmpty(UnityEngine.Application.absoluteURL))
                ForwardDeepLink(UnityEngine.Application.absoluteURL);
#endif
        }

        private static async void ForwardDeepLink(string url)
        {
            try { await AlogameAnalytics.ProcessDeeplink(url); }
            catch (System.Exception e) { UnityEngine.Debug.LogWarning($"[Alogame SDK] Deep link not forwarded: {e.Message}"); }
        }

        // ── Floating Button ──────────────────────────────────────────────────────

        /// <summary>
        /// Show the floating account button. Forces it back even if the player previously hid it
        /// for the session — wire this to a "Show account" entry in your own in-game menu.
        /// </summary>
        public static Task ShowFloatingButton() => Bridge.AlogameBridge.CallNative("ui.showFloatingButton", null);

        /// <summary>Hide the floating account button.</summary>
        public static Task HideFloatingButton() => Bridge.AlogameBridge.CallNative("ui.hideFloatingButton", null);

        /// <summary>
        /// Open the SDK's account dashboard (profile, link account, change password, delete
        /// account, logout) — what the floating button opens. A logout there raises
        /// <see cref="AlogameAuth.SessionEnded"/>.
        /// </summary>
        public static Task ShowDashboard() => Bridge.AlogameBridge.CallNative("ui.showDashboard", null);
    }
}
