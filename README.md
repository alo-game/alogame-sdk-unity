# Alogame SDK — Unity Package (`com.alogame.sdk`)

Unity port of the Alogame native SDK. Mirrors the [`alogame-sdk`](../../oeg-sdk) (Egret/Cocos)
TypeScript API 1:1 — `AlogameSDK`, `AlogameAuth`, `AlogamePayment`, `AlogameAnalytics`,
`AlogamePush`, `AlogameAccount` — over the **same** native dispatch table
(`OEGBridge.kt` / `OEGBridge.swift`) that every other engine wrapper uses.

Requires **Unity 2022.3+**. (The iOS build step adds Swift Package dependencies through
`PBXProject.AddRemotePackageReferenceAtVersion*`, which does not exist before 2022.2.)

## Install

Window → Package Manager → **+** → **Add package from git URL**:

```
https://github.com/alo-game/alogame-sdk-unity.git#0.1.3
```

Everything the SDK needs is inside the package. There is no `.aar` to copy, no
`.xcframework` to drag into Xcode, no Podfile, and no External Dependency Manager.

Two things are still yours to provide:

1. **`oeg_config.json`** — your game's config, from the SDK docs. Put it in
   `Assets/StreamingAssets/`. Both platforms read it during `AlogameSDK.Initialize()`;
   without it the SDK initializes into an unconfigured state (`game_id 0`) and every call
   fails. On iOS the build hook copies it to the app bundle root for you, because the SDK
   reads it from `Bundle.main` and Unity would otherwise leave it in `Data/Raw/`.

   ```json
   { "core": { "game_id": 53, "debug_env": true } }
   ```

   `debug_env` selects the dev backend; `api_base_url` is ignored (the host is derived from
   `debug_env`).
2. **Firebase** (only if you use push or Firebase analytics) — add your
   `google-services.json` / `GoogleService-Info.plist` and apply the
   `com.google.gms.google-services` Gradle plugin yourself. The SDK deliberately does
   not apply that plugin: doing so fails the build for every game that ships no Firebase
   config.

## Usage

```csharp
using Alogame.SDK;

async void Start()
{
    AlogameSDK.Initialize();          // once, at boot, before any other call

    var login = await AlogameAuth.ShowLoginUI();
    if (login.Success)
    {
        // Report server/role right after login — payment and analytics attribution keys on it.
        await AlogameAuth.SetGameRole(serverId: "1", roleId: login.User.Uuid, serverName: "S1");
        await AlogameAnalytics.LogEvent("game_start");

        var purchase = await AlogamePayment.Purchase(
            "gold_100", new GameData { ServerId = "1", RoleId = login.User.Uuid });

        if (purchase.Success)
            await AlogameAnalytics.LogRevenue("iap", 0.99, "USD", purchase.TransactionId);
    }
}
```

See `Samples~/BasicIntegration/LoginSample.cs` for a fuller walkthrough, and
`unity/SampleGame` in the SDK repo for a project this package is wired into.

> The native bridge does nothing in the Editor — `AndroidJavaClass` and
> `DllImport("__Internal")` are both no-ops there, so Play mode resolves calls through
> `MockBridgeTransport`. Test on a real device.

## Social login (Facebook / TikTok)

Both are **off** by default, and off means genuinely absent: no manifest entries, no plist
entries, and no Gradle dependency. To enable one, create an `AlogameSDKSettings` asset
(Assets → Create → Alogame → SDK Settings) under any `Resources` folder and fill in the
provider's fields. The Editor build hooks then inject that provider's
`AndroidManifest.xml` / `Info.plist` entries **and** its Gradle coordinate together, so the
manifest can never reference a class that isn't on the classpath.

Google login needs no such switch — it goes through Credential Manager, which is always on.

## What the build hooks do for you

Everything below is automatic. It is listed because when an iOS or Android build breaks, this
is the list of things to suspect.

**Android** (`AlogameAndroidPostprocessBuild`) appends the native SDK's ~25 Maven coordinates
to the generated `build.gradle`. A vendored `.aar` carries no POM, so nothing else would tell
Gradle that Retrofit, OkHttp, Credential Manager, Adjust, Billing and the Kotlin stdlib are
needed. Without it the APK builds and installs fine, then dies on the first SDK call with
`NoClassDefFoundError`.

**iOS** (`AlogameIosPostprocessBuild`):

- sets `SWIFT_VERSION` on both generated targets — the package ships `.swift` sources, and
  no Unity template target defines a Swift version;
- adds **GoogleSignIn**, **AdjustSdk** and **FirebaseAnalytics** as Swift Package references,
  because `OegSdkV2.xcframework` is built against them and a vendored xcframework carries no
  dependency manifest. *The first iOS build therefore needs network access* so Xcode can
  resolve them;
- embeds `OegSdkV2.framework` and sets `LD_RUNPATH_SEARCH_PATHS`. It is a dynamic framework:
  linked-but-not-embedded builds cleanly and then crashes at launch with "Library not
  loaded". `AdjustSigSdk` is **not** vendored here — the Adjust SPM package already ships it,
  and having both makes Xcode fail with "Multiple commands produce AdjustSigSdk.framework";
- picks the **device or simulator slice explicitly** to match your build target, rather than
  embedding the whole `.xcframework` — shipping the simulator slice fails App Store validation;
- writes **`NSUserTrackingUsageDescription`** into `Info.plist`. This is not optional: the SDK
  requests App Tracking Transparency during analytics init, and iOS *terminates* an app that
  calls it with no usage string. Set your own wording on the `AlogameSDKSettings` asset;
- copies `oeg_config.json` from StreamingAssets to the app bundle root.

### Known issue: duplicate Objective-C classes

On device you will see ~179 `Class … is implemented in both` warnings for Adjust, Firebase,
GoogleSignIn and GTM. `OegSdkV2.xcframework` links those statically but still imports them in
its `.swiftinterface`, so satisfying the module requirement links a second copy; removing the
packages instead fails compilation. This needs an upstream change to how the xcframework is
built — nothing in this package can resolve it.

## Architecture — why the JSON bridge

C# sends `{method, params, callbackId}` to native and receives `{callbackId, success, data}`
back. `AlogameUnityBridge.java` / `AlogameUnityBridge.swift` are thin relays (a few lines
each) into `OEGBridge`, the **same** dispatch table the Egret and Cocos wrappers drive.

Binding the native API directly instead would cost more, not less: it would mean one
`AndroidJavaObject` reflection call and one hand-written C shim per method across ~30 methods,
where every native signature change breaks at runtime with no compile-time warning. The JSON
relay lets every engine share one dispatch table with zero duplicated business logic.

`OEGBridge.swift` is shipped here as source rather than compiled into the xcframework, so its
Egret-specific entry point is compiled out via `#if OEG_EGRET_HOST` — a Unity build contains
no reference to Egret at all.

## Debug symbols

Release builds ship without `dSYMs` to keep the package (and every tagged release of it)
small — they are ~49 MB, roughly double the rest of the package. To symbolicate an iOS crash
report, take the `dSYMs` folder from `v2/release-ios/OegSdkV2.xcframework/<slice>/dSYMs`
for the matching SDK version.

> **When re-vendoring `OegSdkV2.xcframework`, delete the `DebugSymbolsPath` key from each
> slice in its `Info.plist`.** An xcframework that declares that key while shipping no
> `dSYMs` folder fails the *consuming game's* Xcode build outright:
> `error: Missing path (…/ios-arm64/dSYMs) … as defined by 'DebugSymbolsPath'`.
> `deploy_unity_upm.sh` refuses to publish a package where the two disagree.

## Distribution

Published as a git-URL UPM package by `v2/scripts/deploy_unity_upm.sh`, which copies this
folder to the root of a public release repo (so `package.json` sits at the root, as UPM
requires), stamps the version, and tags it. The script refuses to publish a payload with
missing binaries, missing `.meta` files, or a stale `oeg-bridge.aar`.
