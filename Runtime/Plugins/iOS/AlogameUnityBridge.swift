import Foundation
import UIKit

/// Unity-facing front door for the shared `OEGBridge` dispatch table — the same table the
/// Egret/Cocos `alogame-sdk` npm package drives. Unity has no `EgretNativeIOS`, so this uses
/// `OEGBridge.setupHeadless` and relays results back into C# via `UnitySendMessage`.
///
/// Swift rather than Objective-C++, and that is the whole point of this file. The previous
/// `AlogameUnityShim.mm` had to `#import` the *generated* Swift interop header to see
/// `OEGBridge`, and the name of that header is not stable: it is `UnityFramework-Swift.h` on
/// Unity 2019.3+ (plugins compile into the UnityFramework target) but `Unity-iPhone-Swift.h`
/// on older single-target exports, and it exists at all only when "Swift support" is enabled
/// for whichever target the file lands in. That import was flagged in the package README as
/// the single biggest unverified risk. A `.swift` file has no such problem: `OEGBridge.swift`
/// ships alongside this file and compiles into the same module, so the reference below is an
/// ordinary same-module call resolved by the compiler — no header, generated or otherwise.
///
/// The C symbols emitted by the `@_cdecl` functions at the bottom are what C#'s
/// `[DllImport("__Internal")]` binds to. Their names are a contract with
/// `Runtime/Bridge/IosBridgeTransport.cs` and must match it character for character.

/// Exported by Unity's iOS runtime. Declared rather than imported for the same reason this
/// file is Swift: pulling it in properly would mean a header, and `@_silgen_name` binds the
/// symbol directly at link time instead.
@_silgen_name("UnitySendMessage")
private func UnitySendMessage(
    _ gameObject: UnsafePointer<CChar>,
    _ method: UnsafePointer<CChar>,
    _ message: UnsafePointer<CChar>
)

private enum AlogameUnityBridge {

    /// Must match `AlogameCallbackReceiver.GameObjectName` and its `OnNativeMessage` method.
    /// The receiver is created with `DontDestroyOnLoad`, so this target stays valid for the
    /// process lifetime.
    private static let callbackGameObject = "AlogameSDKCallbackReceiver"
    private static let callbackMethod = "OnNativeMessage"

    /// `setupHeadless` hands back an instance the caller is expected to retain — it is the
    /// object `sendToNative` is invoked on, and nothing on the Swift side holds it otherwise.
    /// Letting it deallocate would silently drop every subsequent call.
    private static var bridge: OEGBridge?

    /// App-delegate events that can arrive before C# calls Initialize (a cold launch from a
    /// link, or a token on a relaunch). Held here and replayed once the bridge exists.
    private static var pendingPushToken: Data?
    private static var pendingURLs: [URL] = []

    static func setup(gameId: Int, debugEnv: Bool) {
        guard bridge == nil else { return }
        let created = OEGBridge.setupHeadless(gameId: gameId, debugEnv: debugEnv) { json in
            deliver(json)
        }
        bridge = created
        if let token = pendingPushToken {
            pendingPushToken = nil
            created.didRegisterPushToken(token)
        }
        let urls = pendingURLs
        pendingURLs = []
        urls.forEach { created.openURL($0) }
    }

    static func didRegisterPushToken(_ token: Data) {
        if let bridge = bridge { bridge.didRegisterPushToken(token) } else { pendingPushToken = token }
    }

    static func openURL(_ url: URL) {
        if let bridge = bridge { bridge.openURL(url) } else { pendingURLs.append(url) }
    }

    /// Facebook and TikTok must be started from application:didFinishLaunchingWithOptions:,
    /// which Unity owns. Mirrors the provider part of OEGManager.handleDidFinishLaunching —
    /// that method also initializes the SDK from oeg_config.json, which a Unity game does not
    /// have (it initializes with a game id from C#), so it cannot be called as a whole.
    /// Reflection only: both SDKs are optional packages.
    static func startSocialProviders(launchOptions: [UIApplication.LaunchOptionsKey: Any]?) {
        let app = UIApplication.shared
        typealias LaunchMethod = @convention(c) (AnyObject, Selector, UIApplication, [UIApplication.LaunchOptionsKey: Any]?) -> Bool
        let launchSel = NSSelectorFromString("application:didFinishLaunchingWithOptions:")

        func singleton(_ classNames: [String], _ selectors: [String]) -> NSObject? {
            for name in classNames {
                guard let cls = NSClassFromString(name) as? NSObject.Type else { continue }
                for selName in selectors {
                    let sel = NSSelectorFromString(selName)
                    if cls.responds(to: sel), let obj = cls.perform(sel)?.takeUnretainedValue() as? NSObject {
                        return obj
                    }
                }
            }
            return nil
        }

        // Facebook: Settings.appID / clientToken from Info.plist, then the app delegate hook.
        if let appId = Bundle.main.object(forInfoDictionaryKey: "FacebookAppID") as? String, !appId.isEmpty,
           let settings = singleton(["FBSDKCoreKit.Settings", "FBSDKSettings"], ["shared", "sharedSettings"]) {
            let setAppId = NSSelectorFromString("setAppID:")
            if settings.responds(to: setAppId) { settings.perform(setAppId, with: appId) }
            if let token = Bundle.main.object(forInfoDictionaryKey: "FacebookClientToken") as? String, !token.isEmpty {
                let setToken = NSSelectorFromString("setClientToken:")
                if settings.responds(to: setToken) { settings.perform(setToken, with: token) }
            }
        }
        if let fb = singleton(["FBSDKCoreKit.ApplicationDelegate", "FBSDKApplicationDelegate"], ["shared", "sharedInstance"]),
           fb.responds(to: launchSel) {
            let method = unsafeBitCast(fb.method(for: launchSel), to: LaunchMethod.self)
            _ = method(fb, launchSel, app, launchOptions)
        }

        // TikTok: needs TikTokClientKey in Info.plist.
        if let key = Bundle.main.object(forInfoDictionaryKey: "TikTokClientKey") as? String, !key.isEmpty,
           let tt = singleton(["TikTokOpenSDKApplicationDelegate"], ["sharedInstance", "shared"]),
           tt.responds(to: launchSel) {
            let method = unsafeBitCast(tt.method(for: launchSel), to: LaunchMethod.self)
            _ = method(tt, launchSel, app, launchOptions)
        }
    }

    static func send(_ json: String) {
        // No implicit setup here: a send arriving before setup means the C# side called
        // SendToNative before Initialize, and silently papering over that would hide the bug
        // while producing a bridge with no callback wired up.
        guard let bridge = bridge else {
            NSLog("[Alogame SDK] SendToNative before Initialize — message dropped: %@", json)
            return
        }
        bridge.sendToNative(json)
    }

    /// `UnitySendMessage` is only safe to call on the main thread; the SDK's completion
    /// handlers are not guaranteed to arrive there (network callbacks in particular). Hopping
    /// unconditionally is cheaper than auditing every one of the ~30 dispatch-table entries,
    /// and `async` on the main queue when already on main is a trivial cost here.
    private static func deliver(_ json: String) {
        DispatchQueue.main.async {
            callbackGameObject.withCString { obj in
                callbackMethod.withCString { method in
                    json.withCString { message in
                        UnitySendMessage(obj, method, message)
                    }
                }
            }
        }
    }
}

// MARK: - C entry points bound by IosBridgeTransport.cs

/// `gameId` is a C `int` and `debugEnv` a one-byte C `bool`, matching the `[DllImport]` in
/// IosBridgeTransport.cs (`int` and `[MarshalAs(UnmanagedType.I1)] bool`).
@_cdecl("AlogameUnity_Setup")
public func AlogameUnity_Setup(_ gameId: Int32, _ debugEnv: Bool) {
    AlogameUnityBridge.setup(gameId: Int(gameId), debugEnv: debugEnv)
}

@_cdecl("AlogameUnity_SendToNative")
public func AlogameUnity_SendToNative(_ jsonPayload: UnsafePointer<CChar>?) {
    guard let jsonPayload = jsonPayload else { return }
    AlogameUnityBridge.send(String(cString: jsonPayload))
}

// MARK: - App-delegate forwarding, called from AlogameAppDelegateListener.mm

@_cdecl("AlogameUnity_DidFinishLaunching")
public func AlogameUnity_DidFinishLaunching(_ launchOptions: NSDictionary?) {
    AlogameUnityBridge.startSocialProviders(launchOptions: launchOptions as? [UIApplication.LaunchOptionsKey: Any])
}

@_cdecl("AlogameUnity_DidRegisterPushToken")
public func AlogameUnity_DidRegisterPushToken(_ bytes: UnsafeRawPointer?, _ length: Int32) {
    guard let bytes = bytes, length > 0 else { return }
    AlogameUnityBridge.didRegisterPushToken(Data(bytes: bytes, count: Int(length)))
}

@_cdecl("AlogameUnity_OpenURL")
public func AlogameUnity_OpenURL(_ url: UnsafePointer<CChar>?) {
    guard let url = url, let parsed = URL(string: String(cString: url)) else { return }
    AlogameUnityBridge.openURL(parsed)
}
