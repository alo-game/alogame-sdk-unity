using System.Collections.Generic;
using System.Threading.Tasks;

namespace Alogame.SDK
{
    /// <summary>Mirrors `AlogameAuth.ts` 1:1 — same method names, same parameter order.</summary>
    public static class AlogameAuth
    {
        // ── Core Auth ────────────────────────────────────────────────────────────

        public static async Task<AuthResult> Login(string username, string pass)
        {
            var raw = await Bridge.AlogameBridge.CallNative("auth.login", new Dictionary<string, object>
            {
                ["username"] = username,
                ["pass"] = pass,
            });
            return AuthResult.FromJson(raw);
        }

        /// <param name="fullname">Display name for the new account</param>
        public static async Task<AuthResult> Register(string fullname, string username, string pass)
        {
            var raw = await Bridge.AlogameBridge.CallNative("auth.register", new Dictionary<string, object>
            {
                ["fullname"] = fullname,
                ["username"] = username,
                ["pass"] = pass,
            });
            return AuthResult.FromJson(raw);
        }

        public static async Task<AuthResult> PlayNow()
        {
            var raw = await Bridge.AlogameBridge.CallNative("auth.playNow", null);
            return AuthResult.FromJson(raw);
        }

        public static Task Logout() => Bridge.AlogameBridge.CallNative("auth.logout", null);

        // ── Social Login (SNS) ───────────────────────────────────────────────────

        /// <summary>
        /// Login with a social provider (Google, Apple, Facebook, TikTok). The game must
        /// complete the OAuth flow itself and supply the resulting accessToken.
        /// </summary>
        /// <param name="authorizationCode">Optional authorization code (Apple Sign-In)</param>
        /// <param name="nonce">Optional nonce (Apple Sign-In)</param>
        /// <param name="codeVerifier">Optional PKCE code verifier (TikTok on Android)</param>
        public static async Task<AuthResult> SocialLogin(
            SocialProvider provider,
            string accessToken,
            string authorizationCode = null,
            string nonce = null,
            string codeVerifier = null)
        {
            var raw = await Bridge.AlogameBridge.CallNative("auth.socialLogin", new Dictionary<string, object>
            {
                ["provider"] = provider.ToWireValue(),
                ["accessToken"] = accessToken,
                ["authorizationCode"] = authorizationCode,
                ["nonce"] = nonce,
                ["codeVerifier"] = codeVerifier,
            });
            return AuthResult.FromJson(raw);
        }

        // ── Account Merge ────────────────────────────────────────────────────────

        /// <summary>Merge a guest (PlayNow) account into a full account using username/password.</summary>
        public static async Task<AuthResult> Merge(string uuid, string username, string pass, string fullname = null, string email = null)
        {
            var raw = await Bridge.AlogameBridge.CallNative("auth.merge", new Dictionary<string, object>
            {
                ["uuid"] = uuid,
                ["username"] = username,
                ["pass"] = pass,
                ["fullname"] = fullname,
                ["email"] = email,
            });
            return AuthResult.FromJson(raw);
        }

        /// <summary>Merge a guest account with a social provider account.</summary>
        public static async Task<AuthResult> MergeSocial(string uuid, SocialProvider provider, string accessToken)
        {
            var raw = await Bridge.AlogameBridge.CallNative("auth.mergeSocial", new Dictionary<string, object>
            {
                ["uuid"] = uuid,
                ["provider"] = provider.ToWireValue(),
                ["accessToken"] = accessToken,
            });
            return AuthResult.FromJson(raw);
        }

        // ── Password Management ──────────────────────────────────────────────────

        public static async Task<AuthResult> ForgotPassword(string email)
        {
            var raw = await Bridge.AlogameBridge.CallNative("auth.forgotPassword", new Dictionary<string, object> { ["email"] = email });
            return AuthResult.FromJson(raw);
        }

        public static async Task<AuthResult> ChangePassword(string oldPass, string newPass)
        {
            var raw = await Bridge.AlogameBridge.CallNative("auth.changePassword", new Dictionary<string, object>
            {
                ["oldPass"] = oldPass,
                ["newPass"] = newPass,
            });
            return AuthResult.FromJson(raw);
        }

        // ── Profile ──────────────────────────────────────────────────────────────

        public static async Task<AuthResult> UpdateProfile(UpdateProfileParams profile)
        {
            var raw = await Bridge.AlogameBridge.CallNative("auth.updateProfile", profile.ToJson());
            return AuthResult.FromJson(raw);
        }

        public static async Task<AuthResult> FetchUserInfo()
        {
            var raw = await Bridge.AlogameBridge.CallNative("auth.fetchUserInfo", null);
            return AuthResult.FromJson(raw);
        }

        // ── OTP / Verification ────────────────────────────────────────────────────

        public static async Task<VerificationStatus> CheckEmailVerification()
        {
            var raw = await Bridge.AlogameBridge.CallNative("auth.checkEmailVerification", null);
            return VerificationStatus.FromJson(raw);
        }

        public static async Task<OtpResult> RequestEmailOtp()
        {
            var raw = await Bridge.AlogameBridge.CallNative("auth.requestEmailOtp", null);
            return OtpResult.FromJson(raw);
        }

        /// <param name="phone">Override phone number (optional — uses account phone if omitted)</param>
        public static async Task<OtpResult> RequestPhoneOtp(string phone = null)
        {
            var raw = await Bridge.AlogameBridge.CallNative("auth.requestPhoneOtp", new Dictionary<string, object> { ["phone"] = phone });
            return OtpResult.FromJson(raw);
        }

        public static async Task<OtpResult> VerifyOtp(string otp)
        {
            var raw = await Bridge.AlogameBridge.CallNative("auth.verifyOtp", new Dictionary<string, object> { ["otp"] = otp });
            return OtpResult.FromJson(raw);
        }

        // ── State Queries ─────────────────────────────────────────────────────────

        /// <summary>Returns the current logged-in user, or null if not logged in.</summary>
        public static async Task<AuthUser> GetCurrentUser()
        {
            var raw = await Bridge.AlogameBridge.CallNative("auth.getCurrentUser", null);
            return AuthUser.FromJson(raw as Dictionary<string, object>);
        }

        /// <summary>Returns true if a user session is active.</summary>
        public static async Task<bool> IsLoggedIn()
        {
            var raw = await Bridge.AlogameBridge.CallNative("auth.isLoggedIn", null);
            var dict = raw as Dictionary<string, object>;
            return JsonField.Bool(dict, "loggedIn") || JsonField.Bool(dict, "isLoggedIn");
        }

        // ── UI ───────────────────────────────────────────────────────────────────

        /// <summary>Launch the built-in SDK login/register UI.</summary>
        public static async Task<AuthResult> ShowLoginUI()
        {
            var raw = await Bridge.AlogameBridge.CallNative("auth.showLoginUI", null);
            return AuthResult.FromJson(raw);
        }

        // ── Game Role ─────────────────────────────────────────────────────────────

        /// <summary>Report the active game character to the SDK (shown on dashboard, synced to backend).
        /// Calling this again while a different role is already active fires an `sdk_switch_role`
        /// analytics event (if configured) — no separate call needed for a direct switch.</summary>
        public static Task SetGameRole(string serverId, string roleId, string serverName = null, string roleName = null, int? level = null)
        {
            return Bridge.AlogameBridge.CallNative("auth.setGameRole", new Dictionary<string, object>
            {
                ["serverId"] = serverId,
                ["roleId"] = roleId,
                ["serverName"] = serverName,
                ["roleName"] = roleName,
                ["level"] = level,
            });
        }

        /// <summary>Clear the active game character without logging the account out — call when the
        /// player returns to a server/character-select screen, before the next <see cref="SetGameRole"/>
        /// call for the newly chosen one. Fires an `sdk_logout_game_role` analytics event (if configured)
        /// when a role was actually active. The SDK only clears its own state — it does not navigate
        /// anywhere; move to your own switch-server screen after this completes.</summary>
        public static Task LogoutGameRole()
        {
            return Bridge.AlogameBridge.CallNative("auth.logoutGameRole", null);
        }
    }

    /// <summary>Mirrors the inline `params` object type of `AlogameAuth.updateProfile` in the TS bridge.</summary>
    public sealed class UpdateProfileParams
    {
        public string Fullname;
        public string DisplayName;
        public string Email;
        public string Phone;
        public string Birthday;
        public int? Gender;
        public string Address;
        public string IdNo;
        public string IdDate;
        public string IdAddress;
        public string Country;
        public string Province;
        public string Avatar;

        internal Dictionary<string, object> ToJson() => new()
        {
            ["fullname"] = Fullname,
            ["displayName"] = DisplayName,
            ["email"] = Email,
            ["phone"] = Phone,
            ["birthday"] = Birthday,
            ["gender"] = Gender,
            ["address"] = Address,
            ["idNo"] = IdNo,
            ["idDate"] = IdDate,
            ["idAddress"] = IdAddress,
            ["country"] = Country,
            ["province"] = Province,
            ["avatar"] = Avatar,
        };
    }
}
