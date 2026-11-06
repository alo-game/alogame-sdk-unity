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
https://github.com/alo-game/alogame-sdk-unity.git#1.0.1
```

Everything the SDK needs is inside the package. There is no `.aar` to copy, no
`.xcframework` to drag into Xcode, no Podfile, and no External Dependency Manager.

Two things are still yours to provide:

1. **Your game id**, from your Alogame Operation Manager, passed to
   `AlogameSDK.Initialize(gameId)`. It is the only value the game passes; social login
   credentials, feature flags and tracking are configured per game by Alogame on the server
   and loaded at runtime. No config file is needed (`oeg_config.json` is not read).
2. **Firebase config files** — put `google-services.json` (Android) and
   `GoogleService-Info.plist` (iOS) anywhere under `Assets`. The build hooks pick them up:
   Android gets the Firebase resources the `google-services` Gradle plugin would generate (FCM
   push, Firebase Analytics); iOS gets the plist in the app bundle plus its
   `REVERSED_CLIENT_ID` URL scheme (needed by **Google login on iOS**). No Gradle template
   edits. If the Firebase Unity SDK is in the project, the Android step stands aside for it.

## Usage

```csharp
using Alogame.SDK;

async void Start()
{
    AlogameSDK.Initialize(53);        // your game id; once, at boot, before any other call
    // AlogameSDK.Initialize(53, debugEnv: true);  // dev backend + verbose HTTP logs — never in release

    // The SDK dashboard (floating button) can log the player out — or delete the account —
    // on its own, then shows its login screen again. Follow it:
    AlogameAuth.SessionEnded += _ => ReturnToTitleScreen();
    AlogameAuth.LoggedIn += user => EnterGame(user);

    try
    {
        // Completes when the player is in: after the login screen, or at once for a stored
        // session (after the server maintenance check).
        var login = await AlogameAuth.ShowLoginUI();
        EnterGame(login.User);
    }
    catch (AlogameSdkException e)
    {
        Debug.Log($"Login not completed: [{e.Error.Code}] {e.Message}");   // closed, maintenance, ...
    }
}

async void EnterGame(AuthUser user)
{
    // When the player picks a character — payment and analytics attribution key on it.
    await AlogameAuth.SetGameRole(serverId: "1", roleId: "10001", serverName: "S1", roleName: "Hero", level: 12);

    var purchase = await AlogamePayment.Purchase(
        "gold_100", new GameData { ServerId = "1", RoleId = "10001", RoleName = "Hero", Level = 12 });
}
```

Every call returns a `Task`. A call the SDK rejects throws `AlogameSdkException`
(`e.Error.Code`, `e.Message`); a call that resolves returns its result (`AuthResult.User`,
`PurchaseResult.ProductId`, ...).

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

On iOS, enabling a provider also adds its Swift package (`facebook-ios-sdk` → `FacebookLogin`,
`tiktok-opensdk-ios`), writes `FacebookAppID` / `FacebookClientToken` / `TikTokClientKey` and
the URL schemes, and the package starts both SDKs at launch and passes their callback URLs to
the native SDK (`AlogameAppDelegateListener.mm`).

Google login needs no switch. Android uses Credential Manager (always on); iOS needs
`GoogleService-Info.plist` under `Assets` (see Install).

## Session events

The SDK's own UI can change the session without a call from the game: the dashboard's logout
and account deletion end it, and the login screen it then shows starts a new one.

| Event | When | Game should |
|---|---|---|
| `AlogameAuth.SessionEnded` | logout / account deletion from the SDK dashboard | leave the game, go to the title screen |
| `AlogameAuth.LoggedIn` | the player logged in again through the SDK UI | enter the game as that user |

Both start working after the first `ShowLoginUI()` call (the SDK attaches its session listener
there). All events are raised on the main thread.

## SDK screens

| Call | Opens |
|---|---|
| `AlogameAuth.ShowLoginUI()` | login / register / guest |
| `AlogameSDK.ShowDashboard()` | account hub: profile, link account, change password, delete, logout |
| `AlogameAuth.ShowChangePassword()` | change password |
| `AlogameAuth.ShowUpdateProfile()` | profile editor (iOS: the dashboard, which contains it) |
| `AlogameSDK.ShowFloatingButton()` / `HideFloatingButton()` | the floating account button |

`AlogameAuth.LinkAloId(username, pass)` attaches an Alo ID to a guest or social account without
UI; `AlogameAuth.GetGameRole()` returns the character last set.

## Push notifications

- **Android**: FCM, from `google-services.json` (see Install). Nothing else.
- **iOS**: tick **Enable iOS Push Notifications** on the `AlogameSDKSettings` asset once the
  App ID has Push enabled in the Apple Developer portal. The hook then adds the Push capability
  (`aps-environment`; development or production per the setting), the remote-notification
  background mode, and turns on Unity's `UNITY_USES_REMOTE_NOTIFICATIONS` so the APNs token
  reaches the SDK. It is off by default because the entitlement makes signing fail with a
  provisioning profile that has no push.

Ask for permission when it suits the game: `await AlogamePush.RequestPermission()`. The SDK
registers the token with the backend and shows Alogame's pushes itself.
`AlogamePush.MessageReceived` and `AlogamePush.TokenRefreshed` report them to the game.

## Platform differences

| | Android | iOS |
|---|---|---|
| `PurchaseResult.TransactionId` | Google order id | null (the native purchase flow does not return it) |
| `GameData.ServerId` in `Purchase` | must be numeric (the Android purchase API takes an int) | any string |
| `SocialLogin` `nonce` / `codeVerifier` | ignored | used |
| `AlogamePush.SubscribeToTopic` | FCM topics | rejects (no APNs equivalent) |
| `AlogamePush.GetDeviceToken` | FCM token | APNs token (hex), after permission is granted |
| `ShowUpdateProfile` | profile editor, resolves with the updated user | opens the dashboard |

## Deep links (AppsFlyer OneLink)

Requires AppsFlyer as the game's attribution provider. Subscribe once, early:

```csharp
AlogameAnalytics.DeepLinkResolved += data =>
{
    if (data.TryGetValue("deep_link_value", out var target) && target is string value)
    {
        // Route the player, e.g. value = "shop", "event_halloween", "invite"
    }
};
```

A link resolved before you subscribe (a deferred deep link on first launch) is delivered once
to the first subscriber. Incoming links are forwarded to the SDK for you — on Android
`Initialize` listens to `Application.deepLinkActivated`; on iOS `AlogameAppDelegateListener.mm`
passes every opened URL to the native SDK — so the only per-game setup is the domain:

- **Android**: add the SDK's entry activity with your OneLink domain to a custom main manifest
  (Player Settings → Publishing Settings → Custom Main Manifest):
  ```xml
  <activity android:name="vn.oeg.sdk.v2.core.analytics.OneLinkEntryActivity" android:exported="true">
      <intent-filter android:autoVerify="true">
          <action android:name="android.intent.action.VIEW" />
          <category android:name="android.intent.category.DEFAULT" />
          <category android:name="android.intent.category.BROWSABLE" />
          <data android:scheme="https" android:host="yourgame.onelink.me" />
      </intent-filter>
  </activity>
  ```
- **iOS**: add `applinks:yourgame.onelink.me` to the app's Associated Domains.

Do not also register `OEGAnalytics.setAttributionListener` / `attributionDelegate` in native
code: the bridge owns the SDK's single listener.

## What the build hooks do for you

Everything below is automatic. It is listed because when an iOS or Android build breaks, this
is the list of things to suspect.

**Android** (`AlogameAndroidGoogleServices`) fails the build early if the minimum API level is
below 24, and turns `google-services.json` into Firebase resources.

**Android** (`AlogameAndroidPostprocessBuild`) appends the native SDK's ~25 Maven coordinates
to the generated `build.gradle`. A vendored `.aar` carries no POM, so nothing else would tell
Gradle that Retrofit, OkHttp, Credential Manager, Adjust, Billing and the Kotlin stdlib are
needed. Without it the APK builds and installs fine, then dies on the first SDK call with
`NoClassDefFoundError`.

**iOS** (`AlogameIosPostprocessBuild`):

- sets `SWIFT_VERSION` on both generated targets — the package ships `.swift` sources, and
  no Unity template target defines a Swift version;
- adds **GoogleSignIn**, **FirebaseAnalytics** and **AppsFlyerLib-Static** as Swift Package references,
  because `OegSdkV2.xcframework` is built against them and a vendored xcframework carries no
  dependency manifest. *The first iOS build therefore needs network access* so Xcode can
  resolve them;
- embeds `OegSdkV2.framework` and sets `LD_RUNPATH_SEARCH_PATHS`. It is a dynamic framework:
  linked-but-not-embedded builds cleanly and then crashes at launch with "Library not
  loaded". Adjust is not a dependency of the iOS SDK any more, so no Adjust package or
  `AdjustSigSdk` is added;
- picks the **device or simulator slice explicitly** to match your build target, rather than
  embedding the whole `.xcframework` — shipping the simulator slice fails App Store validation;
- copies `GoogleService-Info.plist` into the app and registers its `REVERSED_CLIENT_ID` scheme;
- adds the Facebook / TikTok packages and `Info.plist` keys when enabled, appending to
  `CFBundleURLTypes` / `LSApplicationQueriesSchemes` rather than replacing them;
- with **Enable iOS Push Notifications**: Push capability, background mode,
  `UNITY_USES_REMOTE_NOTIFICATIONS 1`;
- writes **`NSUserTrackingUsageDescription`** into `Info.plist`. This is not optional: the SDK
  requests App Tracking Transparency during analytics init, and iOS *terminates* an app that
  calls it with no usage string. Set your own wording on the `AlogameSDKSettings` asset.

### Known issue: duplicate Objective-C classes

On device you will see ~179 `Class … is implemented in both` warnings for Adjust, Firebase,
GoogleSignIn and GTM. `OegSdkV2.xcframework` links those statically but still imports them in
its `.swiftinterface`, so satisfying the module requirement links a second copy; removing the
packages instead fails compilation. This needs an upstream change to how the xcframework is
built — nothing in this package can resolve it.

## Architecture — why the JSON bridge

C# sends `{method, params, callbackId}` to native and receives `{callbackId, success, data}`
back. Native-initiated events (`auth.loggedIn`, `auth.sessionEnded`, `push.messageReceived`,
`push.tokenRefreshed`, `analytics.deepLinkResolved`) arrive as `{event, data}` with no
`callbackId`. `AlogameUnityBridge.java` / `AlogameUnityBridge.swift` are thin relays (a few lines
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
