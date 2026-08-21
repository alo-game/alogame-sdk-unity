using System.Threading.Tasks;

namespace Alogame.SDK
{
    /// <summary>
    /// Entry point — mirrors `AlogameSdk.ts`. Call <see cref="Initialize"/> once when your game
    /// boots (e.g. from a bootstrap MonoBehaviour's Awake), before any other Alogame.SDK.* call.
    /// </summary>
    public static class AlogameSDK
    {
        public const string Version = "0.1.0";

        private static bool _initialized;

        /// <summary>Wires the native bridge and bootstraps the SDK (Auth/Payment/Analytics/Push/Account).</summary>
        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            Bridge.AlogameBridge.Initialize();
        }

        // ── Floating Button ──────────────────────────────────────────────────────

        /// <summary>
        /// Show the floating account button. Forces it back even if the player previously hid it
        /// for the session — wire this to a "Show account" entry in your own in-game menu.
        /// </summary>
        public static Task ShowFloatingButton() => Bridge.AlogameBridge.CallNative("ui.showFloatingButton", null);

        /// <summary>Hide the floating account button.</summary>
        public static Task HideFloatingButton() => Bridge.AlogameBridge.CallNative("ui.hideFloatingButton", null);
    }
}
