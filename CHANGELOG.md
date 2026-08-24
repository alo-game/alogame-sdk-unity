# Changelog

All notable changes to this package are documented here.

## [0.1.2] - 2026-08-24

First release since 0.1.0 to change the native binaries. 0.1.0 and 0.1.1 both shipped the
**1.3.17** natives; this ships Android **1.4.3** and iOS **1.4.1**.

The two platform versions differ on purpose. Everything between 1.4.1 and 1.4.3 is Android-only —
a layout fix and ProGuard rules — so the iOS `.xcframework` has no corresponding build. Android
1.4.2 is skipped here: it exists as a published release but predates the ProGuard rules below,
and a minified game cannot use it.

### Fixed — minified (R8) builds could not use the SDK at all

Four separate faults, none visible in a non-minified build, each hiding the next. Found by
building the sample with `minifyRelease` on and running it on a device; the first two also hit
plain native games, and were reported from production.

- **Retrofit's generic return type was stripped.** R8 full mode — the default since AGP 8.0 —
  drops a generic signature when the referenced type is not itself kept, so `Call<T>` reached
  Retrofit as a raw `Class` and the first API call died:
  `ClassCastException: java.lang.Class cannot be cast to java.lang.reflect.ParameterizedType`,
  through `java.lang.reflect.Proxy.invoke`. Retrofit 2.9.0 ships its own rules but predates full
  mode and lacks the three keeps upstream added afterwards. They now ship in the SDK's `.aar`, so
  no game has to know the SDK uses Retrofit. Confirmed by removing only those three lines and
  reproducing the exact stack.
- **The SDK's own response envelopes lost their generics too.** `ConfigApiResponse<T>` and
  `AuthApiResponse<T>` are in `…data.response`, which the existing rules did not cover (they
  covered `…data.dto`). Gson then parsed the payload into a `LinkedTreeMap` and the call site
  threw a bare, message-less `ClassCastException`. This one only surfaced *after* the Retrofit
  fix — it is the very next failure on the same request.
- **`AlogameUnityBridge` was removed outright.** It is reached only by name, from C#
  (`new AndroidJavaClass("com.alogame.unity.AlogameUnityBridge")`), so R8 sees no caller.
  `Initialize()` threw `ClassNotFoundException` and every later SDK call was a silent no-op. The
  Android build post-processor now emits `alogame-proguard.txt` and registers it on
  `consumerProguardFiles`, handling both the Groovy and Kotlin-DSL forms.
- **Keeping `OEGBridge` then broke the build itself.** It carries the Egret entry point, and
  Egret is a `compileOnly` dependency, so a Cocos or Unity game has no such class — R8 treats a
  missing class as a hard error, not a warning. The bridge now ships `-dontwarn org.egret.**`.

Also: `bridge/build.gradle.kts` had declared `consumerProguardFiles("consumer-rules.pro")` for a
file that never existed. Gradle does not fail on that, it just ships an `.aar` with no rules — so
the engine bridge had never contributed a single keep rule. The file now exists.

### Fixed — Android auth screen

- **The Login/Sign Up tab row no longer moves between tabs.** Native 1.4.2 anchored both screens
  to `top|center_horizontal`, but Register's root was the `ScrollView`, and `fillViewport="true"`
  stretches a ScrollView's direct child to the full viewport — which made that `layout_gravity`
  inert and let the shared stage style re-centre the taller card. Register's nesting now mirrors
  Login's. Measured on a 1080x2400 screen: the selected-tab pill starts at y=318 on both tabs,
  where it previously jumped by roughly 340px. iOS never had this.

### Known — `ShowLoginUI()` differs by platform

On iOS the returned `Task` completes when the login flow finishes and carries the signed-in user.
On Android it completes as soon as the screen is *launched*, with no user attached, so
`result.Success` is `false` while the login screen is still open. The Android bridge is
fire-and-forget (`OEGHelper.showLogin` takes a callback parameter it never invokes), and unifying
it needs `onActivityResult` plumbing that Unity's `GameActivity` makes awkward. Until then, do not
read an Android `ShowLoginUI()` result as the outcome of the login — poll `GetCurrentUser()` or
`IsLoggedIn()`.

### Verified

- Android, minified release build (`minifyRelease`, R8 full mode), Unity 6000.5.6f1, IL2CPP
  arm64, on an Android 16 (API 36) arm64 emulator with `game_id 52`: SDK init, remote config
  fetch, Adjust event with a token from that config, and `OEGLoginActivity` on screen — zero
  `ClassCastException`, `ClassNotFoundException` or `FATAL EXCEPTION` in logcat.
- Android, non-minified: same path, plus both auth tabs measured for the layout fix.
- iOS was not rebuilt for this release; its binary is unchanged from 0.1.1's 1.4.1.

### Still to verify

- iOS against these changes — nothing here touches it, but it has not been re-run.
- Android on physical hardware (emulator only so far).
- Purchase and floating-button flows end to end, on either platform.
- Landscape orientation for the auth screen fix.

## [0.1.1] - 2026-08-22

Packaging only — no runtime, C#, or native change. The package contents are byte-identical
to 0.1.0 apart from this file and `package.json`'s two rewritten fields.

- **`changelogUrl` now points at the published copy.** It pointed at the source repo,
  `gitlab.oeg.vn/alogame-tech/sdk/oeg-sdk`, which is not publicly readable — it 302s to the
  sign-in page. (The `gitlab.oeg.vn/release/*` projects *are* public; the source repo is the
  exception.) Unity renders this URL as the **Changelog** link in Package Manager, where every
  reader is a game developer outside the org, so it now points at
  `github.com/alo-game/alogame-sdk-unity/blob/main/CHANGELOG.md`. `deploy_unity_upm.sh` rewrites
  it at release time, the same way it already rewrote the README's install URL, and refuses to
  publish if any `gitlab.oeg.vn` reference survives in `package.json`.
- 0.1.0 shipped with its heading still marked `Unreleased`; it is now dated.
- **Fixed `deploy_unity_upm.sh --retag`.** It pushed the tag alone, leaving the default branch
  on the previous commit — so a retag moved `0.1.0` to the new package while anyone browsing
  the repo, or installing from `main` rather than a tag, still got the old one. Both modes now
  push `HEAD` with the tag.

## [0.1.0] - 2026-08-21

Initial Unity port of the Alogame native SDK — mirrors `alogame-sdk` (Egret/Cocos) 1:1:
`AlogameSDK`, `AlogameAuth`, `AlogamePayment`, `AlogameAnalytics`, `AlogamePush`,
`AlogameAccount`, over the same `OEGBridge` JSON dispatch table.

### Self-contained install

- Vendored `oeg-sdk.aar`, `oeg-bridge.aar` and `OegSdkV2.xcframework` into `Runtime/Plugins/`.
  Integrators no longer copy binaries by hand; the previous instructions also pointed at
  `oeg-sdk/native/android/oeg-sdk.aar`, which was a stale build.
- Added `AlogameAndroidPostprocessBuild`: declares the native SDK's Maven coordinates in the
  generated Gradle project. A vendored `.aar` carries no POM, so without this the APK builds
  and then throws `NoClassDefFoundError` on the first SDK call. The list is transcribed from
  `android/sdk/build.gradle.kts` rather than the POM generated by `release/build.gradle`,
  which is missing `androidx.credentials`, `googleid` and both Firebase artifacts.
- Added `AlogameIosPostprocessBuild`: sets `SWIFT_VERSION`, adds GoogleSignIn / AdjustSdk /
  FirebaseAnalytics as Swift Package references, embeds the dynamic frameworks, and sets
  `LD_RUNPATH_SEARCH_PATHS`. The device/simulator slice is selected explicitly so the
  simulator slice never reaches App Store validation.
- Facebook and TikTok Gradle coordinates are now gated on the same `AlogameSDKSettings` flags
  that gate their manifest/plist entries, so a manifest can no longer reference a class that
  is absent from the classpath.

### Fixed

- **Rebuilt `oeg-bridge.aar`.** The previously vendored build predated the headless entry
  points and exposed only `setup(Activity, EgretNativeAndroid)`, while
  `AlogameUnityBridge.java` calls `setupHeadless`/`sendToNative`. Because `AndroidJavaClass`
  resolves by name at runtime, this could not fail at build time — it shipped as a working
  APK that died on the first SDK call. `deploy_unity_upm.sh` now inspects the class bytes and
  refuses to publish a stale AAR.
- **Replaced `AlogameUnityShim.mm` with `AlogameUnityBridge.swift`.** The `.mm` had to
  `#import` the *generated* Swift interop header, whose name varies by Unity version and
  export layout (`UnityFramework-Swift.h` vs `Unity-iPhone-Swift.h`); the package README
  called this its single biggest unverified risk. A `.swift` file in the same module needs no
  header at all. The two `@_cdecl` symbol names are unchanged, so no C# changed.
- **Made `OEGBridge.swift` compile outside Egret.** It referenced `EgretNativeIOS`, a type
  that reaches it through the Egret sample's Objective-C bridging header and therefore does
  not exist in a Unity Xcode project. Those references are now behind `#if OEG_EGRET_HOST`;
  everything outside the guards is engine-agnostic and shared verbatim.
- Flattened `AlogameUnityBridge.androidlib` to a plain `AlogameUnityBridge.java`, matching the
  layout already device-verified in `alogame-kyc-sdk`. The `.androidlib` module depended on
  Unity adding sibling `.aar` files as dependencies of every generated module, which is not
  how package-vendored plugins resolve.

### Packaging

- Generated `.meta` files (38) so every consuming project sees the same GUIDs.
- Raised the minimum Unity version to **2022.3** — the SPM APIs the iOS hook uses do not
  exist before 2022.2.
- Added `v2/scripts/deploy_unity_upm.sh` + `github_common.sh` to publish the package as a
  git-URL UPM release.
- Wired the package into `unity/SampleGame` (`AlogameSDKDemo.cs`) so API drift breaks that
  project's compile.
- `dSYMs` are excluded from the package (~49 MB); take them from `v2/release-ios/` when
  symbolicating.

### Found by running on a physical device

- **`NSUserTrackingUsageDescription` is now written unconditionally.** The SDK requests App
  Tracking Transparency during analytics init (OegSdkV2 calls
  `requestTrackingAuthorizationWithCompletionHandler:`, driven by Adjust). With no usage string
  in Info.plist that is a TCC privacy violation, and iOS terminates the process rather than
  just denying it — an immediate `SIGABRT` right after "Analytics initialized from remote
  config", with `__TCC_CRASHING_DUE_TO_PRIVACY_VIOLATION__` on the aborting thread. It is
  written before the social-login check, because a game using neither Facebook nor TikTok still
  runs Adjust. Overridable via `AlogameSDKSettings.userTrackingUsageDescription`.

  This stayed hidden until the config fix below landed: with `game_id = 0` the remote-config
  fetch failed, so Adjust never initialised and ATT was never called.

- **`oeg_config.json` is copied to the app bundle root.** The iOS SDK reads it from
  `Bundle.main` (`SdkConfig.swift`), but Unity places StreamingAssets under `Data/Raw/`, which
  that lookup does not search. A game following the Android instructions therefore booted with
  the default config — observed on device as `OEGPayment initialized for gameId: 0` and a
  remote-config failure ("Bundle ID không khớp với game đã đăng ký").

### Known issue — duplicate Objective-C classes (upstream)

On device the runtime logs ~179 distinct classes "implemented in both" the app binary and
`OegSdkV2.framework` — Adjust (48), Firebase (31), GoogleSignIn (20), GTM (12).

This is not fixable from the Unity package. `OegSdkV2.xcframework` links those dependencies
**statically** while its `.swiftinterface` still imports them, so anything doing
`import OegSdkV2` must also supply the modules, which links a second copy. Dropping the Swift
packages instead fails the build with `Unable to resolve module dependency: 'FirebaseAnalytics'`.
The fix belongs upstream: build the xcframework without absorbing its dependencies (or stop
exposing them in the module interface). No misbehaviour has been traced to it so far — the
crash that first drew attention to it turned out to be the ATT issue above — but ObjC class
duplication is a genuine hazard.

### Found by the first real Xcode export

Both only surface during an actual export, not an in-Editor compile:

- **`DebugSymbolsPath` vs stripped dSYMs.** An `.xcframework` `Info.plist` names a `dSYMs`
  folder per slice. Deleting the folders to shrink the package left the key behind, and Xcode
  failed the consuming build with `Missing path (…/ios-arm64/dSYMs)`. The key is now removed
  from both slices, and `deploy_unity_upm.sh` refuses to publish a package where the two
  disagree.
- **`AdjustSigSdk.xcframework` is no longer vendored.** The Adjust SPM package (ios_sdk v5)
  already ships it, so having both produced
  `error: Multiple commands produce .../AdjustSigSdk.framework` — the collision
  `v2/ios/Package.swift` warns about. `v2/release-ios/Package.swift` still vendors it
  correctly, because that distribution declares no SPM dependencies at all.

### Verified

Built in the same Unity project the KYC package is tested in (Unity 6000.5.6f1), with both
Alogame packages installed side by side.

- **iOS: Xcode export + `xcodebuild -scheme UnityFramework` → BUILD SUCCEEDED.**
  `_AlogameUnity_Setup` and `_AlogameUnity_SendToNative` are exported from the built
  `UnityFramework` binary (`nm -gU`) — the exact symbols `IosBridgeTransport.cs` binds — and
  the build contains zero Egret references, confirming the `#if OEG_EGRET_HOST` split.
  `SWIFT_VERSION`, `LD_RUNPATH_SEARCH_PATHS`, all three SPM package references and the embed
  phase are present in the generated `project.pbxproj`. Device and simulator exports each
  select their own slice.
- **Android: full IL2CPP `.apk` builds (48.6 MB).** `vn/oeg/sdk/bridge/OEGBridge`,
  `com/alogame/unity/AlogameUnityBridge`, Retrofit, Credential Manager, Adjust and Billing are
  all present in the dex — so the injected coordinates really do resolve and dex, not just
  appear in `build.gradle`.
- Coexists with `com.alogame.kycsdk`: its Gradle hook correctly detects `kotlin-stdlib` is
  already declared and skips, and both frameworks embed without collision.

### Verified on device

iPhone 13 Pro, iOS 26, ad-hoc signed, `game_id 53` / `debug_env: true`:

```
[IAP]      OEGPayment initialized for gameId: 53
[NETWORK]  POST https://api-sdk.dev.alogame.vn/v2/config → 201 success
[GENERAL]  Remote config fetched successfully
[TRACKING] OEGAnalytics initialized (Adjust sandbox)
[GENERAL]  Analytics initialized from remote config (Adjust + Firebase)
```

So the full path — C# → `AlogameUnityBridge.swift` (`@_cdecl`) → `OEGBridge.setupHeadless` →
`OegSdkCore` → network — runs on real hardware, and the app stays up. `com.alogame.kycsdk` is
installed in the same project and initialises alongside it.

### Still to verify

- Android on a physical device (builds and dexes; no device was available).
- The interactive flows — login, playNow, purchase, floating button. Only SDK bootstrap has
  been exercised end-to-end so far.
- Firebase features need a `GoogleService-Info.plist`; without one the tracker self-no-ops
  ("Firebase Analytics disabled — no GoogleService-Info.plist; tracker is no-op").
