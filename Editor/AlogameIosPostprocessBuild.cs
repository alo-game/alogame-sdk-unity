#if UNITY_IOS
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEditor.iOS.Xcode.Extensions;
using UnityEngine;

namespace Alogame.SDK.Editor
{
    /// <summary>
    /// Configures the generated Xcode project so a game developer never has to open it.
    ///
    /// Two things Unity does not do on its own:
    ///
    ///  1. SWIFT_VERSION — this package ships two .swift files (OEGBridge.swift, the shared
    ///     dispatch table, and AlogameUnityBridge.swift, the @_cdecl front door). No Unity
    ///     template target sets a Swift language version, so without this both fail with
    ///     "Swift language version not specified".
    ///
    ///  2. Embed + rpath — OegSdkV2 is a *dynamic* framework. Unity copies it into the project
    ///     and links it, but does not embed it, and a dynamic framework that is linked and not
    ///     embedded is absent from the .app at runtime. Both the embed and
    ///     LD_RUNPATH_SEARCH_PATHS are required; missing either produces a build that succeeds
    ///     and then dies at launch with "Library not loaded".
    ///
    /// Unity 2019.3+ generates two targets: plugins compile into UnityFramework, while the
    /// .app is the main target and only the main target can embed.
    ///
    /// Adjust is intentionally NOT one of the packages below — OegSdkV2 no longer depends on it
    /// (v2/ios/Package.swift), so there is no AdjustSigSdk dynamic dependency to embed and no
    /// duplicate-class workaround needed here.
    /// </summary>
    public static class AlogameIosPostprocessBuild
    {
        private const string Tag = "[Alogame SDK]";

        /// <summary>
        /// Dynamic frameworks vendored under Runtime/Plugins/iOS that must be embedded.
        /// </summary>
        private static readonly string[] EmbeddedFrameworks =
        {
            "OegSdkV2.framework",
        };

        /// <summary>
        /// Required so `import OegSdkV2` compiles — see the class summary. Versions match
        /// v2/ios/Package.swift; "up to next major" rather than an exact pin, because an exact
        /// pin conflicts hard with any game already depending on a newer patch of the same
        /// package, and all three are common in mobile games.
        ///
        /// EmbedInApp additionally links the product into the main target, which is what makes
        /// Xcode embed that package's dynamic frameworks into the .app. Neither package below
        /// currently needs it — both resolve as static libraries, and linking a static library
        /// into both targets would duplicate every symbol it defines. Kept as a per-entry flag
        /// for any future dynamic-framework dependency.
        /// </summary>
        private static readonly (string Url, string Version, string[] Products, bool EmbedInApp)[] SwiftPackages =
        {
            ("https://github.com/google/GoogleSignIn-iOS", "7.1.0", new[] { "GoogleSignIn" }, false),
            ("https://github.com/firebase/firebase-ios-sdk", "12.19.2", new[] { "FirebaseAnalytics" }, false),
            // Product is "AppsFlyerLib-Static", not the package name — same as v2/ios/Package.swift.
            ("https://github.com/AppsFlyerSDK/AppsFlyerFramework-Static", "7.0.2", new[] { "AppsFlyerLib-Static" }, false),
        };

        /// <summary>
        /// Optional social-login SDKs, added only when enabled in AlogameSDKSettings. The native
        /// SDK reaches them by reflection, so they are linked into the app target only (nothing
        /// in UnityFramework imports them); Xcode embeds their dynamic frameworks from there.
        /// Versions match what native Alogame games ship with (OEGM2: facebook-ios-sdk 19.x;
        /// tiktok-opensdk-ios 2.5.x).
        /// </summary>
        private static readonly (string Url, string Version, string[] Products) FacebookPackage =
            ("https://github.com/facebook/facebook-ios-sdk", "19.0.0", new[] { "FacebookLogin" });

        private static readonly (string Url, string Version, string[] Products) TikTokPackage =
            ("https://github.com/tiktok/tiktok-opensdk-ios", "2.5.0", new[] { "TikTokOpenAuthSDK", "TikTokOpenSDKCore" });

        // 100 so this runs after Unity's own project generation and after
        // AlogameBuildPostprocessor (order 0), which edits Info.plist rather than the pbxproj.
        [PostProcessBuild(100)]
        public static void OnPostprocessBuild(BuildTarget target, string pathToBuiltProject)
        {
            if (target != BuildTarget.iOS) return;

            var projectPath = PBXProject.GetPBXProjectPath(pathToBuiltProject);
            var project = new PBXProject();
            project.ReadFromFile(projectPath);

            var mainTarget = project.GetUnityMainTargetGuid();
            var frameworkTarget = project.GetUnityFrameworkTargetGuid();

            // @executable_path/Frameworks is the entry that matters, and it is needed on the
            // *framework* target too, not just the app: the binary carrying the
            // @rpath/... load command is UnityFramework, since that is what the plugin sources
            // compile into. @executable_path is still the right anchor there — it resolves
            // against the .app regardless of which binary does the loading — whereas
            // @loader_path would resolve inside UnityFramework.framework and find nothing.
            foreach (var t in new[] { mainTarget, frameworkTarget })
            {
                project.SetBuildProperty(t, "SWIFT_VERSION", "5.0");
                project.AddBuildProperty(t, "LD_RUNPATH_SEARCH_PATHS", "@executable_path/Frameworks");
            }

            var settings = AlogameSDKSettings.LoadOrNull();

            AddSwiftPackages(project, mainTarget, frameworkTarget);
            AddSocialPackages(project, mainTarget, settings);
            EmbedFrameworks(project, mainTarget, pathToBuiltProject);
            FixConfigurationBuildDir(project, mainTarget);
            AddGoogleServiceInfo(project, mainTarget, pathToBuiltProject);

            project.WriteToFile(projectPath);

            // After the PBXProject write: ProjectCapabilityManager reads and writes the project
            // file itself.
            if (settings != null && settings.enableIosPushNotifications)
                EnablePush(projectPath, mainTarget, pathToBuiltProject, settings.iosPushDevelopmentEnvironment);
        }

        private static void AddSocialPackages(PBXProject project, string mainTarget, AlogameSDKSettings settings)
        {
            if (settings == null) return;
            var packages = new List<(string Url, string Version, string[] Products)>();
            if (settings.enableFacebookLogin) packages.Add(FacebookPackage);
            if (settings.enableTikTokLogin) packages.Add(TikTokPackage);
            foreach (var (url, version, products) in packages)
            {
                var packageGuid = project.AddRemotePackageReferenceAtVersionUpToNextMajor(url, version);
                foreach (var product in products)
                    project.AddRemotePackageFrameworkToProject(mainTarget, product, packageGuid, false);
                Debug.Log(Tag + " added " + url + " (" + string.Join(", ", products) + ").");
            }
        }

        /// <summary>
        /// Google Sign-In on iOS needs the client id (the SDK reads CLIENT_ID from
        /// GoogleService-Info.plist) and the reversed client id as a URL scheme. Firebase reads
        /// the same file. Copies Assets/**/GoogleService-Info.plist into the app bundle and
        /// registers the scheme; without the file Google login cannot start.
        /// </summary>
        private static void AddGoogleServiceInfo(PBXProject project, string mainTarget, string pathToBuiltProject)
        {
            var source = AlogameAssetSearch.FindInAssets("GoogleService-Info.plist");
            if (source == null)
            {
                Debug.LogWarning(Tag + " no GoogleService-Info.plist under Assets — Google login and Firebase " +
                                 "will not work on iOS. Download it from the Firebase console (iOS app with this " +
                                 "bundle id) and put it anywhere under Assets.");
                return;
            }

            const string fileName = "GoogleService-Info.plist";
            File.Copy(source, Path.Combine(pathToBuiltProject, fileName), true);
            if (string.IsNullOrEmpty(project.FindFileGuidByProjectPath(fileName)))
            {
                var fileGuid = project.AddFile(fileName, fileName, PBXSourceTree.Source);
                project.AddFileToBuild(mainTarget, fileGuid);
            }

            var google = new PlistDocument();
            google.ReadFromFile(source);
            var reversed = google.root["REVERSED_CLIENT_ID"]?.AsString();
            var bundleId = google.root["BUNDLE_ID"]?.AsString();
            if (!string.IsNullOrEmpty(bundleId) && bundleId != PlayerSettings.applicationIdentifier)
                Debug.LogWarning(Tag + " GoogleService-Info.plist is for bundle id '" + bundleId + "' but the app is '" +
                                 PlayerSettings.applicationIdentifier + "'. Google login will fail.");
            if (string.IsNullOrEmpty(reversed))
            {
                Debug.LogWarning(Tag + " GoogleService-Info.plist has no REVERSED_CLIENT_ID — enable Google Sign-In " +
                                 "for the iOS app in Firebase and download the file again.");
                return;
            }

            var infoPath = Path.Combine(pathToBuiltProject, "Info.plist");
            var info = new PlistDocument();
            info.ReadFromFile(infoPath);
            AlogameBuildPostprocessor.AddUrlScheme(info.root, reversed);
            File.WriteAllText(infoPath, info.WriteToString());
            Debug.Log(Tag + " copied GoogleService-Info.plist and registered the Google URL scheme.");
        }

        /// <summary>
        /// Push capability + remote-notification background mode, and turns on Unity's
        /// remote-notification app-delegate methods: with UNITY_USES_REMOTE_NOTIFICATIONS 0
        /// UnityAppController does not implement didRegisterForRemoteNotificationsWithDeviceToken:,
        /// so the APNs token never reaches AlogameAppDelegateListener.mm and the backend never
        /// learns the device.
        /// </summary>
        private static void EnablePush(string projectPath, string mainTarget, string pathToBuiltProject, bool development)
        {
            var entitlements = "Unity-iPhone/" + PlayerSettings.productName.Replace(" ", "") + ".entitlements";
            var capabilities = new ProjectCapabilityManager(projectPath, entitlements, null, mainTarget);
            capabilities.AddPushNotifications(development);
            capabilities.AddBackgroundModes(BackgroundModesOptions.RemoteNotifications);
            capabilities.WriteToFile();

            var preprocessor = Path.Combine(pathToBuiltProject, "Classes", "Preprocessor.h");
            if (File.Exists(preprocessor))
            {
                var text = File.ReadAllText(preprocessor);
                var updated = text.Replace("#define UNITY_USES_REMOTE_NOTIFICATIONS 0", "#define UNITY_USES_REMOTE_NOTIFICATIONS 1");
                if (updated != text) File.WriteAllText(preprocessor, updated);
            }
            else
            {
                Debug.LogError(Tag + " Classes/Preprocessor.h not found — the APNs token will not reach the SDK.");
            }
            Debug.Log(Tag + " enabled Push Notifications (aps-environment " + (development ? "development" : "production") + ").");
        }

        /// <summary>
        /// Unity's iOS export template sets CONFIGURATION_BUILD_DIR = "$(BUILD_DIR)" on the
        /// main target for *every* configuration, so Debug and Release (and therefore a
        /// regular build vs. an Archive) all resolve SPM binary targets into the same output
        /// directory. OegSdkV2 is a signed binary xcframework, so the two configs each try to
        /// copy their own "OegSdkV2.xcframework-ios.signature" into that shared path — the
        /// second one to run fails with "couldn't be copied ... because an item with the same
        /// name already exists". Re-expanding it to Xcode's own per-configuration default
        /// restores the separation without disturbing anything else Unity relies on BUILD_DIR
        /// for. This has to be re-applied on every export because Unity regenerates the whole
        /// pbxproj from its template each time.
        /// </summary>
        private static void FixConfigurationBuildDir(PBXProject project, string mainTarget)
        {
            project.SetBuildProperty(mainTarget, "CONFIGURATION_BUILD_DIR",
                "$(BUILD_DIR)/$(CONFIGURATION)$(EFFECTIVE_PLATFORM_NAME)");
        }

        private static void AddSwiftPackages(PBXProject project, string mainTarget, string frameworkTarget)
        {
            foreach (var (url, version, products, embedInApp) in SwiftPackages)
            {
                var packageGuid = project.AddRemotePackageReferenceAtVersionUpToNextMajor(url, version);
                foreach (var product in products)
                {
                    // UnityFramework is where the plugin sources compile, so it always needs
                    // the product for `import OegSdkV2` to resolve.
                    project.AddRemotePackageFrameworkToProject(frameworkTarget, product, packageGuid, false);
                    if (embedInApp)
                        project.AddRemotePackageFrameworkToProject(mainTarget, product, packageGuid, false);
                }
            }

            Debug.Log(Tag + " added " + SwiftPackages.Length + " Swift Package dependencies. " +
                      "Xcode resolves them on first build — that build needs network access.");
        }

        /// <summary>
        /// A slice is chosen deliberately rather than handing the embed phase the umbrella
        /// .xcframework: an embed phase copies what it is given verbatim, so the umbrella would
        /// put every other slice inside the shipped bundle too, which App Store validation
        /// rejects.
        /// </summary>
        private static void EmbedFrameworks(PBXProject project, string mainTarget, string pathToBuiltProject)
        {
            var wantSimulator = PlayerSettings.iOS.sdkVersion == iOSSdkVersion.SimulatorSDK;
            var searchPaths = new HashSet<string>();

            foreach (var frameworkName in EmbeddedFrameworks)
            {
                var candidates = Directory.GetDirectories(pathToBuiltProject, frameworkName, SearchOption.AllDirectories);
                if (candidates.Length == 0)
                {
                    Debug.LogError(Tag + " " + frameworkName + " not found in the generated Xcode project. " +
                                   "The app will crash at launch with a missing-dylib error. Check that the " +
                                   "matching .xcframework under Runtime/Plugins/iOS is included in the build.");
                    continue;
                }

                string chosen = null;
                foreach (var candidate in candidates)
                {
                    if (IsWantedSlice(candidate, wantSimulator)) { chosen = candidate; break; }
                }
                if (chosen == null)
                {
                    Debug.LogError(Tag + " no iOS " + (wantSimulator ? "simulator" : "device") +
                                   " slice found inside " + frameworkName + " (looked at " +
                                   candidates.Length + " candidate(s)).");
                    continue;
                }

                // PBXProject paths are relative to the generated project root.
                var relative = chosen.Substring(pathToBuiltProject.Length).TrimStart('/', '\\');
                var fileGuid = project.AddFile(chosen, relative, PBXSourceTree.Absolute);
                project.AddFileToEmbedFrameworks(mainTarget, fileGuid);

                var frameworkDir = Path.GetDirectoryName(relative);
                if (!string.IsNullOrEmpty(frameworkDir)) searchPaths.Add(frameworkDir);

                Debug.Log(Tag + " embedded the " + (wantSimulator ? "simulator" : "device") +
                          " slice of " + frameworkName + " (" + relative + ").");
            }

            foreach (var dir in searchPaths)
            {
                project.AddBuildProperty(mainTarget, "FRAMEWORK_SEARCH_PATHS", "$(PROJECT_DIR)/" + dir);
            }
        }

        /// <summary>
        /// Slice directories are named by platform, e.g. "ios-arm64",
        /// "ios-arm64_x86_64-simulator", "ios-arm64_x86_64-maccatalyst", "tvos-arm64".
        ///
        /// Both halves of this test are load-bearing. Matching only on "-simulator" would accept
        /// the **maccatalyst** slice for a device build — it is not a simulator slice, so the
        /// naive test passes — and AdjustSigSdk.xcframework really does ship maccatalyst and
        /// tvos slices alongside the iOS ones. Requiring the "ios-" prefix and rejecting
        /// maccatalyst explicitly leaves exactly the two iOS slices to choose between.
        /// </summary>
        private static bool IsWantedSlice(string frameworkPath, bool wantSimulator)
        {
            var slice = Path.GetFileName(Path.GetDirectoryName(frameworkPath)) ?? string.Empty;
            if (!slice.StartsWith("ios-")) return false;
            if (slice.Contains("maccatalyst")) return false;
            return slice.Contains("-simulator") == wantSimulator;
        }
    }
}
#endif
