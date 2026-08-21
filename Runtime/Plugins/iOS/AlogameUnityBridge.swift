import Foundation

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

    static func setup() {
        guard bridge == nil else { return }
        bridge = OEGBridge.setupHeadless { json in
            deliver(json)
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

@_cdecl("AlogameUnity_Setup")
public func AlogameUnity_Setup() {
    AlogameUnityBridge.setup()
}

@_cdecl("AlogameUnity_SendToNative")
public func AlogameUnity_SendToNative(_ jsonPayload: UnsafePointer<CChar>?) {
    guard let jsonPayload = jsonPayload else { return }
    AlogameUnityBridge.send(String(cString: jsonPayload))
}
