import Foundation
import UIKit
import StoreKit
#if canImport(OegSdkV2)
import OegSdkV2
#endif

/// Bridges incoming JS SDK calls from the Egret game to the native OEG SDK.
///
/// Protocol:
/// - Receives JSON: `{ "method": "auth.login", "params": {...}, "callbackId": "cb_1_..." }`
/// - Routes to the appropriate OEGAuth method
/// - Sends result back: `[native callExternalInterface:@"oegNativeCallback" Value:json]`
@objcMembers
public class OEGBridge: NSObject {

    // `EgretNativeIOS` reaches this file through the Egret sample app's Objective-C bridging
    // header — it is not a module, so `canImport` cannot detect it and any target without that
    // header fails to compile. Hosts that embed the Egret runtime define OEG_EGRET_HOST
    // (see ios-template's build settings); Unity and any other headless host simply do not,
    // and compile the `setupHeadless` path below instead. Everything outside these guards is
    // engine-agnostic and shared verbatim by every host.
#if OEG_EGRET_HOST
    private weak var nativeIOS: EgretNativeIOS?
#endif

    /// Delivers a JSON response string back to the host engine. Set once, by whichever setup path is used.
    private var resultCallback: ((String) -> Void)?

    // `auth.showLoginUI` / `ui.showChangePassword` calls waiting for the SDK UI. OEGManager
    // keeps the showLogin callback as the session callback for the whole session and calls it
    // again later (re-login after the dashboard's logout, `.logout`), so the first result
    // resolves the pending call and later ones become engine events. Main thread only.
    private var pendingLoginCallbackId: String?
    private var pendingChangePasswordCallbackId: String?
    private var purchaseInFlight = false
    /// Hex APNs token, as the SDK sends it to the backend. OEGPush has no public getter.
    private var pushTokenHex: String?

#if OEG_EGRET_HOST
    @objc(setupWithNative:)
    public static func setup(with nativeIOS: EgretNativeIOS) {
        let bridge = OEGBridge()
        bridge.nativeIOS = nativeIOS
        bridge.resultCallback = { [weak nativeIOS] json in nativeIOS?.callExternalInterface("oegNativeCallback", value: json) }
        let bridgePointer = Unmanaged.passRetained(bridge).toOpaque()

        nativeIOS.setExternalInterface("oegNativeBridge") { [weak nativeIOS] message in
            guard let msg = message,
                  let selfBridge = Unmanaged<OEGBridge>.fromOpaque(bridgePointer).takeUnretainedValue() as OEGBridge?
            else { return }
            selfBridge.handleJSMessage(msg)
        }
        bridge.bootstrap {
#if canImport(OegSdkV2)
            OegSdkCore.shared.initialize()
#endif
        }
    }
#endif

    /// Engine-agnostic setup for hosts with no EgretNativeIOS instance (e.g. Unity). The
    /// returned instance stays retained by the caller (e.g. the Unity plugin shim); route
    /// messages in via `sendToNative(_:)` and receive responses via `callback`.
    ///
    /// Takes the game id like the native `OegSdkCore.Builder().setGameId(_:)`; everything else
    /// (social credentials, feature flags, tracking) comes from the server's v2/config. No
    /// `oeg_config.json` is read on this path.
    @objc
    public static func setupHeadless(gameId: Int, debugEnv: Bool, callback: @escaping (String) -> Void) -> OEGBridge {
        let bridge = OEGBridge()
        bridge.resultCallback = callback
        bridge.bootstrap {
#if canImport(OegSdkV2)
            OegSdkCore.Builder()
                .setGameId(gameId)
                .setDebugEnv(debugEnv)
                .build()
#endif
        }
        return bridge
    }

    /// Public entry point for hosts that route the JSON message themselves (e.g. the Unity iOS shim).
    @objc
    public func sendToNative(_ jsonString: String) {
        handleJSMessage(jsonString)
    }

    /// `initSdk` is the full SDK bootstrap (OegSdkCore.Builder().build() or
    /// OegSdkCore.shared.initialize(), both of which wire Auth, Payment, Analytics, Push and
    /// Account) — calling OEGAuth.shared.initialize() directly (as before) skips the
    /// Payment/Push/Account wiring, leaving those facades permanently unconfigured. See
    /// SDK-ALOGAME-BRIDGE-RELEASE-PLAN.md §2.2c. setup(with:) (Egret) still uses the
    /// oeg_config.json initializer; setupHeadless passes the game id.
    private func bootstrap(_ initSdk: () -> Void) {
        initSdk()
#if canImport(OegSdkV2)
        // `attributionDelegate` is weak; the host keeps this bridge alive for the whole session
        // (AlogameUnityBridge.bridge), which is what makes it a valid delegate. A deep link
        // resolved before this line is held by the SDK and delivered here once.
        OEGAnalytics.shared.attributionDelegate = self
        // Same lifetime argument: weak, and the host keeps the bridge alive.
        OEGPush.shared.delegate = self
#endif
    }

    // MARK: - App-delegate forwarding (Unity: AlogameAppDelegateListener.mm)

    /// APNs device token from the app delegate. Unity's app controller receives it, the SDK
    /// does not hook the app delegate, so the host forwards it here.
    @objc public func didRegisterPushToken(_ deviceToken: Data) {
#if canImport(OegSdkV2)
        OEGPush.shared.didRegisterWithToken(deviceToken)
#endif
        let hex = deviceToken.map { String(format: "%02.2hhx", $0) }.joined()
        pushTokenHex = hex
        sendEvent("push.tokenRefreshed", data: ["token": hex])
    }

    /// URL-scheme / universal links the host's app delegate received: social-login callbacks
    /// (Facebook, Google, TikTok) and attribution links. OEGManager.handleOpenURL also passes
    /// the link to the attribution provider, so the engine must not forward it again.
    @objc public func openURL(_ url: URL) {
#if canImport(OegSdkV2)
        _ = OEGManager.handleOpenURL(url, options: [:])
#endif
    }

    // No custom initializer: NSObject's `init()` is all either setup path needs now that the
    // Egret handle is assigned by `setup(with:)` rather than passed through an initializer.
    // Declaring `init(nativeIOS:)` here would also drag `EgretNativeIOS` back into the
    // engine-agnostic part of the file, which is exactly what the guards above remove.

    // MARK: - Incoming JS → Native

    private func handleJSMessage(_ jsonString: String) {
        guard let data = jsonString.data(using: .utf8),
              let payload = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let method = payload["method"] as? String,
              let callbackId = payload["callbackId"] as? String else {
            print("❌ [OEGBridge] Invalid payload: \(jsonString)")
            return
        }
        let params = payload["params"] as? [String: Any] ?? [:]

        switch method {
        case "auth.login":                  handleLogin(params: params, callbackId: callbackId)
        case "auth.register":               handleRegister(params: params, callbackId: callbackId)
        case "auth.playNow":                handlePlayNow(callbackId: callbackId)
        case "auth.logout":                 handleLogout(callbackId: callbackId)
        case "auth.socialLogin":            handleSocialLogin(params: params, callbackId: callbackId)
        case "auth.merge":                  handleMerge(params: params, callbackId: callbackId)
        case "auth.mergeSocial":            handleMergeSocial(params: params, callbackId: callbackId)
        case "auth.forgotPassword":         handleForgotPassword(params: params, callbackId: callbackId)
        case "auth.changePassword":         handleChangePassword(params: params, callbackId: callbackId)
        case "auth.updateProfile":          handleUpdateProfile(params: params, callbackId: callbackId)
        case "auth.fetchUserInfo":          handleFetchUserInfo(callbackId: callbackId)
        case "auth.checkEmailVerification": handleCheckEmailVerification(callbackId: callbackId)
        case "auth.requestEmailOtp":        handleRequestEmailOtp(callbackId: callbackId)
        case "auth.requestPhoneOtp":        handleRequestPhoneOtp(params: params, callbackId: callbackId)
        case "auth.verifyOtp":              handleVerifyOtp(params: params, callbackId: callbackId)
        case "auth.setGameRole":            handleSetGameRole(params: params, callbackId: callbackId)
        case "auth.logoutGameRole":         handleLogoutGameRole(callbackId: callbackId)
        case "auth.showLoginUI":            handleShowLoginUI(callbackId: callbackId)
        case "auth.getCurrentUser":         handleGetCurrentUser(callbackId: callbackId)
        case "auth.isLoggedIn":             handleIsLoggedIn(callbackId: callbackId)
        case "ui.showFloatingButton":       handleShowFloatingButton(callbackId: callbackId)
        case "ui.hideFloatingButton":       handleHideFloatingButton(callbackId: callbackId)
        case "ui.showDashboard":            handleShowDashboard(callbackId: callbackId)
        case "ui.showChangePassword":       handleShowChangePassword(callbackId: callbackId)
        case "ui.showUpdateProfile":        handleShowDashboard(callbackId: callbackId)
        case "auth.linkAloId":              handleLinkAloId(params: params, callbackId: callbackId)
        case "auth.getGameRole":            handleGetGameRole(callbackId: callbackId)
        case "payment.queryProducts":       handleQueryProducts(params: params, callbackId: callbackId)
        case "payment.purchase":            handlePurchase(params: params, callbackId: callbackId)
        case "payment.restorePurchases":    handleRestorePurchases(params: params, callbackId: callbackId)
        case "payment.hasPendingPurchases": handleHasPendingPurchases(callbackId: callbackId)
        case "analytics.logEvent":          handleLogEvent(params: params, callbackId: callbackId)
        case "analytics.logPurchase":       handleLogPurchase(params: params, callbackId: callbackId)
        case "analytics.logRevenue":        handleLogRevenue(params: params, callbackId: callbackId)
        case "analytics.setTrackingEnabled":handleSetTrackingEnabled(params: params, callbackId: callbackId)
        case "analytics.isInitialized":     handleAnalyticsIsInitialized(callbackId: callbackId)
        case "analytics.processDeeplink":   handleProcessDeeplink(params: params, callbackId: callbackId)
        case "push.requestPermission":      handleRequestPushPermission(callbackId: callbackId)
        case "push.getDeviceToken":         handleGetDeviceToken(callbackId: callbackId)
        case "push.subscribeToTopic":       handleUnsupportedPushTopic(callbackId: callbackId)
        case "push.unsubscribeFromTopic":   handleUnsupportedPushTopic(callbackId: callbackId)
        case "account.requestDeletion":     handleAccountRequestDeletion(callbackId: callbackId)
        case "account.cancelDeletion":      handleAccountCancelDeletion(callbackId: callbackId)
        case "account.getStatus":           handleAccountGetStatus(callbackId: callbackId)
        default:
            sendError(callbackId: callbackId, code: 404, message: "Unknown method: \(method)")
        }
    }

    // MARK: - Auth Handlers

    private func handleLogin(params: [String: Any], callbackId: String) {
        // Web SDK sends "pass" (not "password") — match key name
        guard let username = params["username"] as? String,
              let pass = params["pass"] as? String else {
            sendError(callbackId: callbackId, code: 400, message: "Missing username or pass")
            return
        }
#if canImport(OegSdkV2)
        Task { @MainActor in
            do {
                let user = try await OEGAuth.shared.login(username: username, password: pass)
                self.sendUserSuccess(user, callbackId: callbackId)
            } catch let e as NSError {
                self.sendError(callbackId: callbackId, code: e.code, message: e.localizedDescription)
            } catch {
                self.sendError(callbackId: callbackId, code: 500, message: "Unknown error")
            }
        }
#else
        sendSuccess(callbackId: callbackId, data: ["id": "mock", "username": username, "token": "mock-token"])
#endif
    }

    private func handleRegister(params: [String: Any], callbackId: String) {
        // Web SDK sends "fullname" (standardized). Accept "name" as legacy fallback.
        let fullname = (params["fullname"] as? String) ?? (params["name"] as? String) ?? ""
        guard let username = params["username"] as? String,
              let pass = params["pass"] as? String else {
            sendError(callbackId: callbackId, code: 400, message: "Missing username or pass")
            return
        }
#if canImport(OegSdkV2)
        Task { @MainActor in
            do {
                let user = try await OEGAuth.shared.register(fullname: fullname, username: username, password: pass)
                self.sendUserSuccess(user, callbackId: callbackId)
            } catch let e as NSError {
                self.sendError(callbackId: callbackId, code: e.code, message: e.localizedDescription)
            } catch {
                self.sendError(callbackId: callbackId, code: 500, message: "Unknown error")
            }
        }
#else
        sendSuccess(callbackId: callbackId, data: ["id": "mock", "username": username, "token": "mock-token"])
#endif
    }

    private func handlePlayNow(callbackId: String) {
#if canImport(OegSdkV2)
        Task { @MainActor in
            do {
                let user = try await OEGAuth.shared.playNow()
                self.sendUserSuccess(user, callbackId: callbackId)
            } catch let e as NSError {
                self.sendError(callbackId: callbackId, code: e.code, message: e.localizedDescription)
            } catch {
                self.sendError(callbackId: callbackId, code: 500, message: "Unknown error")
            }
        }
#else
        sendSuccess(callbackId: callbackId, data: ["id": "mock-guest", "username": "guest", "token": "mock"])
#endif
    }

    private func handleLogout(callbackId: String) {
#if canImport(OegSdkV2)
        OEGAuth.shared.logout()
#endif
        sendSuccess(callbackId: callbackId, data: [:])
    }

    private func handleShowFloatingButton(callbackId: String) {
#if canImport(OegSdkV2)
        if #available(iOS 14.0, *) {
            DispatchQueue.main.async {
                FloatingBubbleManager.shared.forceShow()
            }
        }
#endif
        sendSuccess(callbackId: callbackId, data: [:])
    }

    private func handleHideFloatingButton(callbackId: String) {
#if canImport(OegSdkV2)
        if #available(iOS 14.0, *) {
            DispatchQueue.main.async {
                FloatingBubbleManager.shared.hide()
            }
        }
#endif
        sendSuccess(callbackId: callbackId, data: [:])
    }

    private func handleSocialLogin(params: [String: Any], callbackId: String) {
        guard let providerStr = params["provider"] as? String,
              let accessToken = params["accessToken"] as? String else {
            sendError(callbackId: callbackId, code: 400, message: "Missing provider or accessToken")
            return
        }
        let authCode    = params["authorizationCode"] as? String
        let nonce       = params["nonce"] as? String
        let codeVerifier = params["codeVerifier"] as? String
#if canImport(OegSdkV2)
        guard let provider = SocialProvider(rawValue: providerStr.lowercased()) else {
            sendError(callbackId: callbackId, code: 400, message: "Unknown provider: \(providerStr)")
            return
        }
        OEGAuth.shared.socialLogin(
            provider: provider,
            accessToken: accessToken,
            authorizationCode: authCode,
            nonce: nonce,
            codeVerifier: codeVerifier
        ) { result in
            self.handleAuthResult(result, callbackId: callbackId)
        }
#else
        sendSuccess(callbackId: callbackId, data: ["id": "mock", "provider": providerStr, "token": "mock-token"])
#endif
    }

    private func handleMerge(params: [String: Any], callbackId: String) {
        let uuid     = params["uuid"] as? String ?? ""
        let username = params["username"] as? String ?? ""
        let pass     = params["pass"] as? String ?? ""
        let fullname = params["fullname"] as? String
        let email    = params["email"] as? String
#if canImport(OegSdkV2)
        OEGAuth.shared.merge(uuid: uuid, username: username, password: pass, fullname: fullname, email: email) { result in
            self.handleAuthResult(result, callbackId: callbackId)
        }
#else
        sendSuccess(callbackId: callbackId, data: ["merged": true])
#endif
    }

    private func handleMergeSocial(params: [String: Any], callbackId: String) {
        let uuid        = params["uuid"] as? String ?? ""
        let providerStr = params["provider"] as? String ?? ""
        let accessToken = params["accessToken"] as? String ?? ""
#if canImport(OegSdkV2)
        guard let provider = SocialProvider(rawValue: providerStr.lowercased()) else {
            sendError(callbackId: callbackId, code: 400, message: "Unknown provider: \(providerStr)")
            return
        }
        OEGAuth.shared.mergeSocial(uuid: uuid, provider: provider, accessToken: accessToken) { result in
            self.handleAuthResult(result, callbackId: callbackId)
        }
#else
        sendSuccess(callbackId: callbackId, data: ["merged": true])
#endif
    }

    private func handleForgotPassword(params: [String: Any], callbackId: String) {
        let email = params["email"] as? String ?? ""
#if canImport(OegSdkV2)
        OEGAuth.shared.forgotPassword(email: email) { result in
            self.handleAuthResult(result, callbackId: callbackId)
        }
#else
        sendSuccess(callbackId: callbackId, data: ["sent": true])
#endif
    }

    private func handleChangePassword(params: [String: Any], callbackId: String) {
        let oldPass = params["oldPass"] as? String ?? ""
        let newPass = params["newPass"] as? String ?? ""
#if canImport(OegSdkV2)
        OEGAuth.shared.changePassword(oldPass: oldPass, newPass: newPass) { result in
            self.handleAuthResult(result, callbackId: callbackId)
        }
#else
        sendSuccess(callbackId: callbackId, data: ["changed": true])
#endif
    }

    private func handleUpdateProfile(params: [String: Any], callbackId: String) {
#if canImport(OegSdkV2)
        OEGAuth.shared.updateProfile(
            fullname:    params["fullname"] as? String,
            displayName: params["displayName"] as? String,
            email:       params["email"] as? String,
            phone:       params["phone"] as? String,
            birthday:    params["birthday"] as? String,
            gender:      params["gender"] as? Int,
            address:     params["address"] as? String,
            idNo:        params["idNo"] as? String,
            idDate:      params["idDate"] as? String,
            idAddress:   params["idAddress"] as? String,
            country:     params["country"] as? String,
            province:    params["province"] as? String,
            avatar:      params["avatar"] as? String
        ) { result in
            self.handleAuthResult(result, callbackId: callbackId)
        }
#else
        sendSuccess(callbackId: callbackId, data: ["updated": true])
#endif
    }

    private func handleFetchUserInfo(callbackId: String) {
#if canImport(OegSdkV2)
        OEGAuth.shared.fetchUserInfo { result in
            self.handleAuthResult(result, callbackId: callbackId)
        }
#else
        sendSuccess(callbackId: callbackId, data: [:])
#endif
    }

    private func handleCheckEmailVerification(callbackId: String) {
#if canImport(OegSdkV2)
        OEGAuth.shared.checkEmailVerification { status in
            switch status {
            case .verified:
                self.sendSuccess(callbackId: callbackId, data: ["verified": true])
            case .notVerified:
                self.sendSuccess(callbackId: callbackId, data: ["verified": false])
            case .error(let code, let msg):
                self.sendError(callbackId: callbackId, code: code, message: msg)
            }
        }
#else
        sendSuccess(callbackId: callbackId, data: ["verified": false])
#endif
    }

    private func handleRequestEmailOtp(callbackId: String) {
#if canImport(OegSdkV2)
        OEGAuth.shared.requestEmailOtp { result in
            self.handleOtpResult(result, callbackId: callbackId)
        }
#else
        sendSuccess(callbackId: callbackId, data: ["success": true, "expiresIn": 90])
#endif
    }

    private func handleRequestPhoneOtp(params: [String: Any], callbackId: String) {
        let phone = params["phone"] as? String
#if canImport(OegSdkV2)
        OEGAuth.shared.requestPhoneOtp(phone: phone) { result in
            self.handleOtpResult(result, callbackId: callbackId)
        }
#else
        sendSuccess(callbackId: callbackId, data: ["success": true, "expiresIn": 90])
#endif
    }

    private func handleVerifyOtp(params: [String: Any], callbackId: String) {
        let otp = params["otp"] as? String ?? ""
#if canImport(OegSdkV2)
        OEGAuth.shared.verifyOtp(otp: otp) { result in
            self.handleOtpResult(result, callbackId: callbackId)
        }
#else
        sendSuccess(callbackId: callbackId, data: ["success": true, "verified": true])
#endif
    }

    private func handleSetGameRole(params: [String: Any], callbackId: String) {
        let serverId   = params["serverId"] as? String ?? ""
        let roleId     = params["roleId"] as? String ?? ""
        let serverName = params["serverName"] as? String
        let roleName   = params["roleName"] as? String
        let level      = (params["level"] as? NSNumber)?.intValue
#if canImport(OegSdkV2)
        OEGAuth.shared.setGameRole(serverId: serverId, serverName: serverName, roleId: roleId, roleName: roleName, level: level)
#endif
        sendSuccess(callbackId: callbackId, data: [:])
    }

    private func handleLogoutGameRole(callbackId: String) {
#if canImport(OegSdkV2)
        OEGAuth.shared.logoutGameRole {
            self.sendSuccess(callbackId: callbackId, data: [:])
        }
#else
        sendSuccess(callbackId: callbackId, data: [:])
#endif
    }

    /// Resolves when the player is in — after the login screen, or straight away for a stored
    /// session (OEGManager.showLogin runs the maintain check and the link gates first).
    private func handleShowLoginUI(callbackId: String) {
#if canImport(OegSdkV2)
        DispatchQueue.main.async {
            if let previous = self.pendingLoginCallbackId {
                self.sendError(callbackId: previous, code: 409, message: "Superseded by a newer showLoginUI call")
            }
            self.pendingLoginCallbackId = callbackId
            OEGManager.sharedManager.showLogin(viewController: nil) { [weak self] type, _, success, error in
                DispatchQueue.main.async { self?.handleSessionCallback(type: type, success: success, error: error) }
            }
        }
#else
        sendSuccess(callbackId: callbackId, data: ["launched": true])
#endif
    }

#if canImport(OegSdkV2)
    /// See `pendingLoginCallbackId`. `.logout` comes from the dashboard's logout, account
    /// deletion and OEGManager.logout(); a later successful login comes from the login screen
    /// the SDK shows right after those.
    private func handleSessionCallback(type: OEGActionType, success: Bool, error: NSError?) {
        if type == .logout {
            if let pending = pendingLoginCallbackId {
                pendingLoginCallbackId = nil
                sendError(callbackId: pending, code: 401, message: "Logged out")
            }
            sendEvent("auth.sessionEnded", data: ["message": "Logged out"])
            return
        }
        let loginTypes: [OEGActionType] = [.register, .loginOEGID, .loginFacebook, .loginGoogle, .loginApple, .playnow]
        let pending = pendingLoginCallbackId
        if success, let user = OEGAuth.shared.currentUser {
            if let pending = pending {
                pendingLoginCallbackId = nil
                sendUserSuccess(user, callbackId: pending)
            } else if loginTypes.contains(type) {
                sendEvent("auth.loggedIn", data: userDictionary(user))
            }
        } else if let pending = pending {
            pendingLoginCallbackId = nil
            sendError(callbackId: pending, code: error?.code ?? 400, message: error?.localizedDescription ?? "User cancelled login")
        }
    }
#endif

    /// The account dashboard (profile, link account, change password, logout). Session changes
    /// made there reach the engine through the showLogin session callback, not this call.
    private func handleShowDashboard(callbackId: String) {
#if canImport(OegSdkV2)
        DispatchQueue.main.async {
            OEGManager.sharedManager.showProfile(viewController: nil) { _, _, _, _ in }
            self.sendSuccess(callbackId: callbackId, data: [:])
        }
#else
        sendSuccess(callbackId: callbackId, data: [:])
#endif
    }

    private func handleShowChangePassword(callbackId: String) {
#if canImport(OegSdkV2)
        DispatchQueue.main.async {
            if let previous = self.pendingChangePasswordCallbackId {
                self.sendError(callbackId: previous, code: 409, message: "Superseded by a newer showChangePassword call")
            }
            self.pendingChangePasswordCallbackId = callbackId
            OEGManager.sharedManager.showResetPassword(viewController: nil) { [weak self] _, _, success, error in
                DispatchQueue.main.async {
                    guard let self = self, let pending = self.pendingChangePasswordCallbackId else { return }
                    self.pendingChangePasswordCallbackId = nil
                    if success {
                        self.sendSuccess(callbackId: pending, data: ["success": true])
                    } else {
                        self.sendError(callbackId: pending, code: error?.code ?? 400, message: error?.localizedDescription ?? "Password not changed")
                    }
                }
            }
        }
#else
        sendSuccess(callbackId: callbackId, data: ["success": true])
#endif
    }

    /// Attach an Alo ID (username + password) to the current social/guest account.
    private func handleLinkAloId(params: [String: Any], callbackId: String) {
        guard let username = params["username"] as? String, !username.isEmpty,
              let pass = params["pass"] as? String, !pass.isEmpty else {
            sendError(callbackId: callbackId, code: 400, message: "Username and password are required")
            return
        }
#if canImport(OegSdkV2)
        OEGAuth.shared.linkOegId(username: username, password: pass,
                                 fullname: params["fullname"] as? String,
                                 email: params["email"] as? String) { [weak self] result in
            self?.handleAuthResult(result, callbackId: callbackId)
        }
#else
        sendSuccess(callbackId: callbackId, data: ["id": "mock", "username": username])
#endif
    }

    private func handleGetGameRole(callbackId: String) {
#if canImport(OegSdkV2)
        guard let role = OegSdkCore.getGameRole() else {
            sendSuccess(callbackId: callbackId, data: ["hasRole": false])
            return
        }
        var data = role
        data["hasRole"] = true
        sendSuccess(callbackId: callbackId, data: data)
#else
        sendSuccess(callbackId: callbackId, data: ["hasRole": false])
#endif
    }

    private func handleGetCurrentUser(callbackId: String) {
#if canImport(OegSdkV2)
        if let user = OEGAuth.shared.currentUser {
            sendUserSuccess(user, callbackId: callbackId)
        } else {
            sendSuccess(callbackId: callbackId, data: ["loggedIn": false])
        }
#else
        sendSuccess(callbackId: callbackId, data: ["loggedIn": false])
#endif
    }

    private func handleIsLoggedIn(callbackId: String) {
#if canImport(OegSdkV2)
        sendSuccess(callbackId: callbackId, data: ["loggedIn": OEGAuth.shared.isLoggedIn])
#else
        sendSuccess(callbackId: callbackId, data: ["loggedIn": false])
#endif
    }

    // MARK: - Payment Handlers

#if canImport(OegSdkV2)
    private func parseGameData(_ dict: [String: Any]?) -> GameData {
        let d = dict ?? [:]
        let serverId = d["serverId"] as? String ?? ""
        let roleId = d["roleId"] as? String ?? ""
        let roleName = d["roleName"] as? String ?? roleId
        let level = (d["level"] as? NSNumber)?.intValue ?? 1
        let extInfo = d["extInfo"] as? String
        return GameData(serverId: serverId, characterId: roleId, characterName: roleName, level: level, extraData: extInfo)
    }
#endif

    private func handleQueryProducts(params: [String: Any], callbackId: String) {
        guard let productIds = params["productIds"] as? [String], !productIds.isEmpty else {
            sendError(callbackId: callbackId, code: 400, message: "productIds is required")
            return
        }
        guard #available(iOS 15.0, *) else {
            sendError(callbackId: callbackId, code: 501, message: "queryProducts requires iOS 15+")
            return
        }
        Task {
            do {
                let products = try await Product.products(for: productIds)
                let arr: [[String: Any]] = products.map { p in
                    var item: [String: Any] = [
                        "productId": p.id,
                        "title": p.displayName,
                        "description": p.description,
                        "price": p.displayPrice
                    ]
                    if #available(iOS 16.0, *) {
                        item["currency"] = p.priceFormatStyle.currencyCode
                    }
                    return item
                }
                DispatchQueue.main.async { self.sendSuccessArray(callbackId: callbackId, data: arr) }
            } catch {
                DispatchQueue.main.async {
                    self.sendError(callbackId: callbackId, code: 500, message: error.localizedDescription)
                }
            }
        }
    }

    /// Goes through OEGManager.inAppPurchase — the path native iOS games use — so a guest is
    /// offered to link the account before paying, the loading overlay shows, and concurrent
    /// purchases are refused. Its callback reports `.purchasing` first (and again for Ask to
    /// Buy), then one terminal status.
    private func handlePurchase(params: [String: Any], callbackId: String) {
        guard let productId = params["productId"] as? String, !productId.isEmpty else {
            sendError(callbackId: callbackId, code: 400, message: "productId is required")
            return
        }
#if canImport(OegSdkV2)
        let d = params["gameData"] as? [String: Any] ?? [:]
        DispatchQueue.main.async {
            // OEGManager ignores a second purchase without calling back (v2 ≤ 1.5.5), which
            // would leave this call pending forever — refuse it here instead.
            guard !self.purchaseInFlight else {
                self.sendError(callbackId: callbackId, code: 409, message: "Another purchase is in progress")
                return
            }
            self.purchaseInFlight = true
            var purchasingSeen = 0
            var finished = false
            let finish: (Bool, Int, String) -> Void = { [weak self] ok, code, message in
                guard let self = self, !finished else { return }
                finished = true
                self.purchaseInFlight = false
                if ok {
                    self.sendSuccess(callbackId: callbackId, data: ["success": true, "productId": productId])
                } else {
                    self.sendError(callbackId: callbackId, code: code, message: message)
                }
            }
            let level = (d["level"] as? NSNumber)?.intValue ?? 1
            OEGManager.sharedManager.inAppPurchase(
                serverID: d["serverId"] as? String ?? "",
                roleID: d["roleId"] as? String ?? "",
                levels: String(level),
                accountID: nil,
                productID: productId,
                extInfo: d["extInfo"] as? String ?? ""
            ) { status, message, error in
                DispatchQueue.main.async {
                    switch status {
                    case .purchasing:
                        purchasingSeen += 1
                        // The second .purchasing is Ask to Buy: terminal for now.
                        if purchasingSeen >= 2 { finish(false, 202, message) }
                    case .verified, .restored:
                        finish(true, 0, message)
                    case .purchased:
                        // Paid, server verification pending: the SDK retries in the background.
                        finish(false, 202, message)
                    case .failed, .requestError:
                        finish(false, error?.code ?? 400, message)
                    @unknown default:
                        finish(false, 500, message)
                    }
                }
            }
        }
#else
        sendSuccess(callbackId: callbackId, data: ["success": true, "productId": productId, "transactionId": "mock-tx"])
#endif
    }

    private func handleRestorePurchases(params: [String: Any], callbackId: String) {
#if canImport(OegSdkV2)
        let gameData = parseGameData(params["gameData"] as? [String: Any])
        OEGPayment.shared.restorePurchases(gameData: gameData) { [weak self] result in
            guard let self else { return }
            switch result {
            case .success, .alreadyGranted:
                self.sendSuccess(callbackId: callbackId, data: ["success": true])
            case .noItems:
                self.sendError(callbackId: callbackId, code: 404, message: "No pending purchases")
            case .authError:
                self.sendError(callbackId: callbackId, code: 401, message: "Not logged in")
            case .alreadyProcessing:
                self.sendError(callbackId: callbackId, code: 409, message: "Already processing")
            case .serverError(let code, let message):
                self.sendError(callbackId: callbackId, code: code, message: message ?? "Restore failed")
            case .networkError(let error):
                self.sendError(callbackId: callbackId, code: 500, message: error.localizedDescription)
            }
        }
#else
        sendSuccess(callbackId: callbackId, data: ["success": true])
#endif
    }

    private func handleHasPendingPurchases(callbackId: String) {
#if canImport(OegSdkV2)
        sendSuccess(callbackId: callbackId, data: ["hasPending": OEGPayment.shared.hasPendingPurchasesForCurrentUser()])
#else
        sendSuccess(callbackId: callbackId, data: ["hasPending": false])
#endif
    }

    // MARK: - Analytics Handlers

    private func stringMap(_ dict: [String: Any]?) -> [String: String]? {
        guard let dict = dict else { return nil }
        var map: [String: String] = [:]
        for (k, v) in dict { map[k] = "\(v)" }
        return map
    }

    private func handleLogEvent(params: [String: Any], callbackId: String) {
        let eventNameOrToken = params["eventNameOrToken"] as? String ?? ""
#if canImport(OegSdkV2)
        OEGAnalytics.shared.logEvent(eventNameOrToken, parameters: stringMap(params["parameters"] as? [String: Any]))
#endif
        sendSuccess(callbackId: callbackId, data: [:])
    }

    private func handleLogPurchase(params: [String: Any], callbackId: String) {
        let eventToken = params["eventToken"] as? String ?? ""
        let price = params["price"] as? Double ?? 0
        let currency = params["currency"] as? String ?? ""
        let transactionId = params["transactionId"] as? String
        let productId = params["productId"] as? String
#if canImport(OegSdkV2)
        OEGAnalytics.shared.logPurchase(eventToken: eventToken, price: price, currency: currency, transactionId: transactionId, productId: productId)
#endif
        sendSuccess(callbackId: callbackId, data: [:])
    }

    private func handleLogRevenue(params: [String: Any], callbackId: String) {
        let eventNameOrToken = params["eventNameOrToken"] as? String ?? ""
        let revenue = params["revenue"] as? Double ?? 0
        let currency = params["currency"] as? String ?? ""
        let orderId = params["orderId"] as? String
        let parameters = stringMap(params["parameters"] as? [String: Any])
#if canImport(OegSdkV2)
        OEGAnalytics.shared.logRevenue(eventNameOrToken, revenue: revenue, currency: currency, orderId: orderId, parameters: parameters)
#endif
        sendSuccess(callbackId: callbackId, data: [:])
    }

    private func handleSetTrackingEnabled(params: [String: Any], callbackId: String) {
        let enabled = params["enabled"] as? Bool ?? true
#if canImport(OegSdkV2)
        OEGAnalytics.shared.setTrackingEnabled(enabled)
#endif
        sendSuccess(callbackId: callbackId, data: [:])
    }

    /// For engines that receive links themselves (Unity's Application.deepLinkActivated) —
    /// Unity's own app controller takes `openURL`/`continueUserActivity`, so the SDK's
    /// `OEGManager.handleOpenURL`/`handleContinueUserActivity` never see them.
    private func handleProcessDeeplink(params: [String: Any], callbackId: String) {
        guard let string = params["url"] as? String, let url = URL(string: string) else {
            sendError(callbackId: callbackId, code: 400, message: "url is required")
            return
        }
#if canImport(OegSdkV2)
        OEGAnalytics.shared.processDeeplink(url)
#endif
        sendSuccess(callbackId: callbackId, data: [:])
    }

    private func handleAnalyticsIsInitialized(callbackId: String) {
#if canImport(OegSdkV2)
        sendSuccess(callbackId: callbackId, data: ["initialized": OEGAnalytics.shared.isInitialized()])
#else
        sendSuccess(callbackId: callbackId, data: ["initialized": false])
#endif
    }

    // MARK: - Push Handlers

    private func handleRequestPushPermission(callbackId: String) {
#if canImport(OegSdkV2)
        OEGPush.shared.requestNotificationPermission { [weak self] granted in
            self?.sendSuccess(callbackId: callbackId, data: ["granted": granted])
        }
#else
        sendSuccess(callbackId: callbackId, data: ["granted": false])
#endif
    }

    /// OEGPush.swift has no public getter for the current token (it's registration-driven via
    /// didRegisterForRemoteNotifications, not pull-based like Android's getDeviceToken()) — always
    /// returns null until a getter is added to the core SDK. See SDK-ALOGAME-BRIDGE-RELEASE-PLAN.md §2.2c.
    private func handleGetDeviceToken(callbackId: String) {
        // Null until iOS hands the app a token (after push permission is granted).
        sendSuccess(callbackId: callbackId, data: ["token": (pushTokenHex as Any?) ?? NSNull()])
    }

    /// FCM topic subscription has no iOS/APNs equivalent — Android-only.
    private func handleUnsupportedPushTopic(callbackId: String) {
        sendError(callbackId: callbackId, code: 501, message: "Topic subscription is Android-only")
    }

    // MARK: - Account Handlers

#if canImport(OegSdkV2)
    private func accountErrorInfo(_ error: AccountError) -> (Int, String) {
        switch error {
        case .alreadyPending: return (409, "Deletion already pending")
        case .noPendingDeletion: return (404, "No pending deletion")
        case .guestNotAllowed: return (403, "Guest accounts cannot be deleted")
        case .unauthorized: return (401, "Not logged in")
        case .networkError(let m): return (500, m)
        case .serverError(let m): return (500, m)
        case .unknown(let m): return (500, m)
        }
    }
#endif

    private func handleAccountRequestDeletion(callbackId: String) {
#if canImport(OegSdkV2)
        OEGAccount.shared.requestDeletion { [weak self] result in
            guard let self else { return }
            switch result {
            case .success:
                self.sendSuccess(callbackId: callbackId, data: ["success": true])
            case .failure(let error):
                let (code, message) = self.accountErrorInfo(error)
                self.sendError(callbackId: callbackId, code: code, message: message)
            }
        }
#else
        sendSuccess(callbackId: callbackId, data: ["success": true])
#endif
    }

    private func handleAccountCancelDeletion(callbackId: String) {
#if canImport(OegSdkV2)
        OEGAccount.shared.cancelDeletion { [weak self] result in
            guard let self else { return }
            switch result {
            case .success:
                self.sendSuccess(callbackId: callbackId, data: ["success": true])
            case .failure(let error):
                let (code, message) = self.accountErrorInfo(error)
                self.sendError(callbackId: callbackId, code: code, message: message)
            }
        }
#else
        sendSuccess(callbackId: callbackId, data: ["success": true])
#endif
    }

    private func handleAccountGetStatus(callbackId: String) {
#if canImport(OegSdkV2)
        OEGAccount.shared.getAccountStatus { [weak self] result in
            guard let self else { return }
            switch result {
            case .success(let status):
                self.sendSuccess(callbackId: callbackId, data: [
                    "status": status.status,
                    "scheduledAt": (status.scheduledAt as Any?) ?? NSNull(),
                    "daysRemaining": (status.daysRemaining as Any?) ?? NSNull()
                ])
            case .failure(let error):
                let (code, message) = self.accountErrorInfo(error)
                self.sendError(callbackId: callbackId, code: code, message: message)
            }
        }
#else
        sendSuccess(callbackId: callbackId, data: ["status": "none"])
#endif
    }

    // MARK: - Result Helpers

#if canImport(OegSdkV2)
    private func handleAuthResult(_ result: AuthResult, callbackId: String) {
        switch result {
        case .success(let user, _):
            sendUserSuccess(user, callbackId: callbackId)
        case .error(let code, let message):
            sendError(callbackId: callbackId, code: code, message: message)
        }
    }

    private func handleOtpResult(_ result: OtpResult, callbackId: String) {
        switch result {
        case .success(let expiresIn):
            sendSuccess(callbackId: callbackId, data: ["success": true, "expiresIn": expiresIn])
        case .verified:
            sendSuccess(callbackId: callbackId, data: ["success": true, "verified": true])
        case .error(let code, let msg):
            sendError(callbackId: callbackId, code: code, message: msg)
        }
    }

    private func sendUserSuccess(_ user: AuthUser, callbackId: String) {
        sendSuccess(callbackId: callbackId, data: userDictionary(user))
    }

    private func userDictionary(_ user: AuthUser) -> [String: Any] {
        var userData: [String: Any] = [
            "id":           user.uuid,
            "userId":       user.userId,
            "username":     user.username ?? "",
            "displayName":  user.displayName ?? "",
            "fullname":     user.fullname ?? "",
            "email":        user.email ?? "",
            "avatar":       user.avatar ?? "",
            "phone":        user.phone ?? "",
            "uuid":         user.uuid,
            "token":        user.token,
            "birthday":     user.birthday ?? "",
            "emailVerified": user.emailVerified ?? false,
            "phoneVerified": user.phoneVerified ?? false,
            "isPlayNow":    user.isGuest,
            "loginType":    user.loginType.rawValue,
            "accountStatus": user.accountStatus ?? "active",
        ]
        if let g = user.gender   { userData["gender"] = g }
        if let a = user.address  { userData["address"] = a }
        if let n = user.idNo     { userData["idNo"] = n }
        if let d = user.idDate   { userData["idDate"] = d }
        if let ia = user.idAddress { userData["idAddress"] = ia }
        if let c = user.country  { userData["country"] = c }
        if let p = user.province { userData["province"] = p }
        return userData
    }
#endif

    // MARK: - JS Communication

    private func sendSuccess(callbackId: String, data: [String: Any]) {
        sendToJS(["callbackId": callbackId, "success": true, "data": data])
    }

    /// Overload for methods whose resolved value is an array (e.g. payment.queryProducts).
    private func sendSuccessArray(callbackId: String, data: [[String: Any]]) {
        sendToJS(["callbackId": callbackId, "success": true, "data": data])
    }

    private func sendError(callbackId: String, code: Int, message: String) {
        sendToJS(["callbackId": callbackId, "success": false, "data": ["code": code, "message": message]])
    }

    private func sendToJS(_ response: [String: Any]) {
        guard let jsonData = try? JSONSerialization.data(withJSONObject: response),
              let jsonStr = String(data: jsonData, encoding: .utf8) else { return }
        DispatchQueue.main.async { [weak self] in
            self?.resultCallback?(jsonStr)
        }
    }

    /// Native-initiated message: carries `event` instead of a `callbackId`, so the engine side
    /// routes it to its event handlers rather than to a pending call.
    fileprivate func sendEvent(_ event: String, data: [String: Any]) {
        let message: [String: Any] = ["event": event, "data": data]
        // `data(withJSONObject:)` raises an Objective-C exception (not a Swift error) on a
        // non-JSON value, which `try?` cannot catch — check first.
        guard JSONSerialization.isValidJSONObject(message) else {
            NSLog("[Alogame SDK] Dropped native event %@: payload is not valid JSON", event)
            return
        }
        sendToJS(message)
    }
}

#if canImport(OegSdkV2)
extension OEGBridge: OEGPushDelegate {
    public func oegPushDidReceiveNotification(userInfo: [AnyHashable: Any]) {
        var data: [String: Any] = [:]
        for (key, value) in userInfo {
            let k = "\(key)"
            data[k] = JSONSerialization.isValidJSONObject([k: value]) ? value : "\(value)"
        }
        sendEvent("push.messageReceived", data: data)
    }
}

extension OEGBridge: OEGAttributionDelegate {
    public func oegDidResolveDeepLink(_ data: [String: Any]) {
        sendEvent("analytics.deepLinkResolved", data: data)
    }
}
#endif
