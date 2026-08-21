using UnityEngine;

namespace Alogame.SDK
{
    /// <summary>
    /// Per-project settings consumed by <c>AlogameBuildPostprocessor</c> (Editor-only) to inject
    /// the Android manifest / iOS Info.plist entries the native SDK's optional social login
    /// providers need. Create one via Assets → Create → Alogame → SDK Settings and place it
    /// anywhere under a `Resources` folder as `AlogameSDKSettings` (loaded via Resources.Load at
    /// build time, since ScriptableObjects aren't otherwise reachable from a build callback
    /// without a known asset path).
    /// </summary>
    [CreateAssetMenu(fileName = "AlogameSDKSettings", menuName = "Alogame/SDK Settings")]
    public sealed class AlogameSDKSettings : ScriptableObject
    {
        public const string ResourcePath = "AlogameSDKSettings";

        [Header("Facebook Login")]
        public bool enableFacebookLogin;
        public string facebookAppId;
        public string facebookClientToken;

        [Header("TikTok Login")]
        public bool enableTikTokLogin;
        public string tikTokClientKey;

        [Header("iOS — App Tracking Transparency")]
        [Tooltip("Shown in the iOS tracking-permission dialog. The SDK always requests App " +
                 "Tracking Transparency (Adjust attribution), and iOS terminates the app on the " +
                 "spot if Info.plist has no NSUserTrackingUsageDescription — so this is written " +
                 "into every build whether or not a settings asset exists. Override it here to " +
                 "match your game's wording or to localise it.")]
        [TextArea(2, 4)]
        public string userTrackingUsageDescription = DefaultUserTrackingUsageDescription;

        /// <summary>
        /// Used when the game ships no settings asset. Deliberately generic and honest about
        /// what the tracking is for — App Review rejects vague or misleading purpose strings.
        /// </summary>
        public const string DefaultUserTrackingUsageDescription =
            "This identifier will be used to measure advertising performance and deliver a better game experience.";

        /// <summary>
        /// Returns the project's settings asset, or null when the game has not created one.
        /// Null is a normal, supported state: a game using neither Facebook nor TikTok login
        /// needs no asset at all, and every caller treats null as "no optional providers".
        /// Callers that want to warn about a missing asset do so themselves — this stays
        /// silent so the Gradle and Info.plist passes don't each log the same warning.
        /// </summary>
        public static AlogameSDKSettings LoadOrNull()
        {
            return Resources.Load<AlogameSDKSettings>(ResourcePath);
        }
    }
}
