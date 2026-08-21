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
    /// Three things Unity does not do on its own:
    ///
    ///  1. SWIFT_VERSION — this package ships two .swift files (OEGBridge.swift, the shared
    ///     dispatch table, and AlogameUnityBridge.swift, the @_cdecl front door). No Unity
    ///     template target sets a Swift language version, so without this both fail with
    ///     "Swift language version not specified".
    ///
    ///  2. Embed + rpath — OegSdkV2 and AdjustSigSdk are *dynamic* frameworks. Unity copies
    ///     them into the project and links them, but does not embed them, and a dynamic
    ///     framework that is linked and not embedded is absent from the .app at runtime. Both
    ///     the embed and LD_RUNPATH_SEARCH_PATHS are required; missing either produces a build
    ///     that succeeds and then dies at launch with "Library not loaded".
    ///
    ///  3. oeg_config.json — the iOS SDK reads it from `Bundle.main`, but Unity puts
    ///     StreamingAssets under Data/Raw/, which is not the bundle root. See CopyConfig below.
    ///
    /// Unity 2019.3+ generates two targets: plugins compile into UnityFramework, while the
    /// .app is the main target and only the main target can embed.
    ///
    /// ── On the Adjust duplicate-class warning ────────────────────────────────────────────
    /// At runtime the console reports classes like ADJAdRevenue "implemented in both" the app
    /// binary and OegSdkV2.framework. That is a known consequence of how the xcframework is
    /// built, not a misconfiguration here, and it is not currently avoidable from Unity:
    ///
    ///   * OegSdkV2.xcframework links AdjustSdk, GoogleSignIn and FirebaseAnalytics
    ///     *statically* — `nm -gU` reports OBJC_CLASS_$_ADJAdRevenue, _GIDSignIn, _FIRAnalytics
    ///     and _FIRApp as defined, with no matching undefined symbols.
    ///   * But its `.swiftinterface` still imports AdjustSdk, FirebaseAnalytics, FirebaseCore
    ///     and GoogleSignIn, so anything doing `import OegSdkV2` — i.e. OEGBridge.swift — fails
    ///     to compile unless those modules are resolvable. Dropping the packages produces
    ///     "Unable to resolve module dependency: 'FirebaseAnalytics'".
    ///   * AdjustSigSdk is a genuine *dynamic* dependency (OegSdkV2 carries an
    ///     @rpath/AdjustSigSdk.framework load command and does not define ADJSigner), and the
    ///     Adjust package ships it. Vendoring our own copy as well collides at the Products
    ///     path — "Multiple commands produce .../AdjustSigSdk.framework".
    ///
    /// So the packages must be present to compile, and Adjust must reach the main target to get
    /// AdjustSigSdk embedded, which is exactly what makes the static copy duplicate. The real
    /// fix is upstream: build OegSdkV2.xcframework without statically absorbing its
    /// dependencies. Until then this configuration is the one verified to build, install and
    /// run on a device.
    /// </summary>
    public static class AlogameIosPostprocessBuild
    {
        private const string Tag = "[Alogame SDK]";

        /// <summary>
        /// Dynamic frameworks vendored under Runtime/Plugins/iOS that must be embedded.
        /// AdjustSigSdk is not here: it arrives with the Adjust package and is embedded via
        /// EmbedInApp below — see the class summary.
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
        /// Xcode embed that package's dynamic frameworks into the .app. Only Adjust needs it
        /// (for AdjustSigSdk.framework); GoogleSignIn and FirebaseAnalytics resolve as static
        /// libraries, and linking a static library into both targets would duplicate every
        /// symbol it defines.
        /// </summary>
        private static readonly (string Url, string Version, string[] Products, bool EmbedInApp)[] SwiftPackages =
        {
            ("https://github.com/google/GoogleSignIn-iOS", "7.0.0", new[] { "GoogleSignIn" }, false),
            ("https://github.com/adjust/ios_sdk", "5.0.0", new[] { "AdjustSdk" }, true),
            ("https://github.com/firebase/firebase-ios-sdk", "10.24.0", new[] { "FirebaseAnalytics" }, false),
        };

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

            AddSwiftPackages(project, mainTarget, frameworkTarget);
            EmbedFrameworks(project, mainTarget, pathToBuiltProject);
            CopyConfig(project, mainTarget, pathToBuiltProject);

            project.WriteToFile(projectPath);
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

        /// <summary>
        /// The iOS SDK loads its config with
        /// `Bundle.main.url(forResource: "oeg_config", withExtension: "json")` (see
        /// SdkConfig.swift). Unity places StreamingAssets under `Data/Raw/` inside the bundle,
        /// which that lookup does not search — so a game that follows the Android instructions
        /// and drops the file in StreamingAssets silently gets the default config, and the SDK
        /// initialises with `game_id = 0` and then fails remote config with a bundle-ID
        /// mismatch. Observed on device before this was added.
        ///
        /// Copying rather than asking the developer to add it to Xcode by hand keeps a single
        /// source of truth: StreamingAssets/oeg_config.json serves both platforms.
        /// </summary>
        private static void CopyConfig(PBXProject project, string mainTarget, string pathToBuiltProject)
        {
            const string fileName = "oeg_config.json";
            var source = Path.Combine(pathToBuiltProject, "Data/Raw", fileName);
            if (!File.Exists(source))
            {
                Debug.LogWarning(Tag + " no " + fileName + " in StreamingAssets — the SDK will start with " +
                                 "an empty config (game_id 0) and every call will fail. Add your game's " +
                                 fileName + " to Assets/StreamingAssets/.");
                return;
            }

            var destination = Path.Combine(pathToBuiltProject, fileName);
            File.Copy(source, destination, true);

            var guid = project.AddFile(destination, fileName, PBXSourceTree.Source);
            project.AddFileToBuild(mainTarget, guid);
            Debug.Log(Tag + " copied " + fileName + " to the app bundle root.");
        }
    }
}
#endif
