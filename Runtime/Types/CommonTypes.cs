using System;
using System.Collections.Generic;

namespace Alogame.SDK
{
    // ─── Error ──────────────────────────────────────────────────────────────────
    // Mirrors AlogameError / SdkErrorCode in CommonTypes.ts (identical numeric codes).

    public sealed class AlogameError
    {
        public int Code;
        public string Message;

        internal static AlogameError FromJson(Dictionary<string, object> json)
        {
            if (json == null) return null;
            return new AlogameError
            {
                Code = JsonField.Int(json, "code"),
                Message = JsonField.Str(json, "message"),
            };
        }
    }

    /// <summary>Thrown when a bridge call rejects. Mirrors the JS Promise reject shape.</summary>
    public sealed class AlogameSdkException : Exception
    {
        public readonly AlogameError Error;

        internal AlogameSdkException(Dictionary<string, object> data) : base(JsonField.Str(data, "message") ?? "Alogame SDK error")
        {
            Error = AlogameError.FromJson(data);
        }
    }

    // ─── Auth ───────────────────────────────────────────────────────────────────

    public enum SocialProvider { Google, Apple, Facebook, TikTok }

    internal static class SocialProviderExtensions
    {
        public static string ToWireValue(this SocialProvider provider) => provider switch
        {
            SocialProvider.Google => "google",
            SocialProvider.Apple => "apple",
            SocialProvider.Facebook => "facebook",
            SocialProvider.TikTok => "tiktok",
            _ => throw new ArgumentOutOfRangeException(nameof(provider)),
        };
    }

    public sealed class LoginMetadata
    {
        public string AccountStatus;
        public string DeleteScheduledAt;
        public bool IsMaintain;
        public string MaintainMessage;
        public string MaintainMessageEn;
        public string MaintainRedirectUrl;
        public string MaintainEndTime;

        internal static LoginMetadata FromJson(Dictionary<string, object> json)
        {
            if (json == null) return null;
            return new LoginMetadata
            {
                AccountStatus = JsonField.Str(json, "accountStatus"),
                DeleteScheduledAt = JsonField.Str(json, "deleteScheduledAt"),
                IsMaintain = JsonField.Bool(json, "isMaintain"),
                MaintainMessage = JsonField.Str(json, "maintainMessage"),
                MaintainMessageEn = JsonField.Str(json, "maintainMessageEn"),
                MaintainRedirectUrl = JsonField.Str(json, "maintainRedirectUrl"),
                MaintainEndTime = JsonField.Str(json, "maintainEndTime"),
            };
        }
    }

    public sealed class AuthUser
    {
        public string Id; // maps to uuid on native
        public string UserId;
        public string Username;
        public string DisplayName;
        public string Fullname;
        public string Email;
        public string Avatar;
        public string Phone;
        public string Uuid;
        public string Token;
        public string Birthday;
        public bool EmailVerified;
        public bool PhoneVerified;
        public bool IsPlayNow;
        public string LoginType;
        public string AccountStatus;
        public int? Gender;
        public string Address;
        public string IdNo;
        public string IdDate;
        public string IdAddress;
        public string Country;
        public string Province;

        internal static AuthUser FromJson(Dictionary<string, object> json)
        {
            if (json == null) return null;
            return new AuthUser
            {
                Id = JsonField.Str(json, "id"),
                UserId = JsonField.Str(json, "userId"),
                Username = JsonField.Str(json, "username"),
                DisplayName = JsonField.Str(json, "displayName"),
                Fullname = JsonField.Str(json, "fullname"),
                Email = JsonField.Str(json, "email"),
                Avatar = JsonField.Str(json, "avatar"),
                Phone = JsonField.Str(json, "phone"),
                Uuid = JsonField.Str(json, "uuid"),
                Token = JsonField.Str(json, "token"),
                Birthday = JsonField.Str(json, "birthday"),
                EmailVerified = JsonField.Bool(json, "emailVerified"),
                PhoneVerified = JsonField.Bool(json, "phoneVerified"),
                IsPlayNow = JsonField.Bool(json, "isPlayNow"),
                LoginType = JsonField.Str(json, "loginType"),
                AccountStatus = JsonField.Str(json, "accountStatus"),
                Gender = JsonField.NullableInt(json, "gender"),
                Address = JsonField.Str(json, "address"),
                IdNo = JsonField.Str(json, "idNo"),
                IdDate = JsonField.Str(json, "idDate"),
                IdAddress = JsonField.Str(json, "idAddress"),
                Country = JsonField.Str(json, "country"),
                Province = JsonField.Str(json, "province"),
            };
        }
    }

    public sealed class AuthResult
    {
        public bool Success;
        public AuthUser User;
        public LoginMetadata LoginMetadata;
        public AlogameError Error;

        internal static AuthResult FromJson(object raw)
        {
            var json = raw as Dictionary<string, object>;
            if (json == null) return new AuthResult { Success = false };
            return new AuthResult
            {
                Success = JsonField.Bool(json, "success"),
                User = AuthUser.FromJson(JsonField.Obj(json, "user")),
                LoginMetadata = LoginMetadata.FromJson(JsonField.Obj(json, "loginMetadata")),
                Error = AlogameError.FromJson(JsonField.Obj(json, "error")),
            };
        }
    }

    public sealed class OtpResult
    {
        public bool Success;
        public bool? Verified;
        public int? ExpiresIn;
        public AlogameError Error;

        internal static OtpResult FromJson(object raw)
        {
            var json = raw as Dictionary<string, object>;
            if (json == null) return new OtpResult { Success = false };
            return new OtpResult
            {
                Success = JsonField.Bool(json, "success"),
                Verified = JsonField.NullableBool(json, "verified"),
                ExpiresIn = JsonField.NullableInt(json, "expiresIn"),
                Error = AlogameError.FromJson(JsonField.Obj(json, "error")),
            };
        }
    }

    public sealed class VerificationStatus
    {
        public bool Verified;
        public AlogameError Error;

        internal static VerificationStatus FromJson(object raw)
        {
            var json = raw as Dictionary<string, object>;
            if (json == null) return new VerificationStatus { Verified = false };
            return new VerificationStatus
            {
                Verified = JsonField.Bool(json, "verified"),
                Error = AlogameError.FromJson(JsonField.Obj(json, "error")),
            };
        }
    }

    // ─── Game Data (payment/analytics context) ────────────────────────────────

    public sealed class GameData
    {
        public string ServerId;
        public string RoleId;
        public string ServerName;
        public string RoleName;
        public int? Level;
        public string ExtInfo;

        internal Dictionary<string, object> ToJson() => new()
        {
            ["serverId"] = ServerId,
            ["roleId"] = RoleId,
            ["serverName"] = ServerName,
            ["roleName"] = RoleName,
            ["level"] = Level,
            ["extInfo"] = ExtInfo,
        };
    }

    // ─── Payment / IAP ──────────────────────────────────────────────────────────

    public enum ProductType { Inapp, Subs }

    internal static class ProductTypeExtensions
    {
        public static string ToWireValue(this ProductType type) => type == ProductType.Subs ? "subs" : "inapp";
    }

    public sealed class ProductDetails
    {
        public string ProductId;
        public string Price;
        public double? PriceAmountMicros;
        public string Currency;
        public string Title;
        public string Description;

        internal static ProductDetails FromJson(Dictionary<string, object> json)
        {
            if (json == null) return null;
            return new ProductDetails
            {
                ProductId = JsonField.Str(json, "productId"),
                Price = JsonField.Str(json, "price"),
                PriceAmountMicros = JsonField.NullableDouble(json, "priceAmountMicros"),
                Currency = JsonField.Str(json, "currency"),
                Title = JsonField.Str(json, "title"),
                Description = JsonField.Str(json, "description"),
            };
        }

        internal static List<ProductDetails> ListFromJson(object raw)
        {
            var list = new List<ProductDetails>();
            if (raw is List<object> array)
            {
                foreach (var item in array)
                    if (item is Dictionary<string, object> dict)
                        list.Add(FromJson(dict));
            }
            return list;
        }
    }

    public sealed class PurchaseResult
    {
        public bool Success;
        public string ProductId;
        public string TransactionId;
        public AlogameError Error;

        internal static PurchaseResult FromJson(object raw)
        {
            var json = raw as Dictionary<string, object>;
            if (json == null) return new PurchaseResult { Success = false };
            return new PurchaseResult
            {
                Success = JsonField.Bool(json, "success"),
                ProductId = JsonField.Str(json, "productId"),
                TransactionId = JsonField.Str(json, "transactionId"),
                Error = AlogameError.FromJson(JsonField.Obj(json, "error")),
            };
        }
    }

    public sealed class RestoreResult
    {
        public bool Success;
        public int? RestoredCount;
        public AlogameError Error;

        internal static RestoreResult FromJson(object raw)
        {
            var json = raw as Dictionary<string, object>;
            if (json == null) return new RestoreResult { Success = false };
            return new RestoreResult
            {
                Success = JsonField.Bool(json, "success"),
                RestoredCount = JsonField.NullableInt(json, "restoredCount"),
                Error = AlogameError.FromJson(JsonField.Obj(json, "error")),
            };
        }
    }

    // ─── Account ────────────────────────────────────────────────────────────────

    public sealed class AccountResult
    {
        public bool Success;
        public AlogameError Error;

        internal static AccountResult FromJson(object raw)
        {
            var json = raw as Dictionary<string, object>;
            if (json == null) return new AccountResult { Success = false };
            return new AccountResult
            {
                Success = JsonField.Bool(json, "success"),
                Error = AlogameError.FromJson(JsonField.Obj(json, "error")),
            };
        }
    }

    public sealed class AccountStatus
    {
        public string Status;
        public string ScheduledAt;
        public int? DaysRemaining;

        internal static AccountStatus FromJson(object raw)
        {
            var json = raw as Dictionary<string, object>;
            if (json == null) return new AccountStatus { Status = "unknown" };
            return new AccountStatus
            {
                Status = JsonField.Str(json, "status"),
                ScheduledAt = JsonField.Str(json, "scheduledAt"),
                DaysRemaining = JsonField.NullableInt(json, "daysRemaining"),
            };
        }
    }

    // ─── Shared JSON field-extraction helpers ──────────────────────────────────

    internal static class JsonField
    {
        public static string Str(Dictionary<string, object> json, string key) =>
            json != null && json.TryGetValue(key, out var v) ? v as string : null;

        public static bool Bool(Dictionary<string, object> json, string key) =>
            json != null && json.TryGetValue(key, out var v) && v is bool b && b;

        public static bool? NullableBool(Dictionary<string, object> json, string key) =>
            json != null && json.TryGetValue(key, out var v) && v is bool b ? b : (bool?)null;

        public static int Int(Dictionary<string, object> json, string key) =>
            json != null && json.TryGetValue(key, out var v) && v is double d ? (int)d : 0;

        public static int? NullableInt(Dictionary<string, object> json, string key) =>
            json != null && json.TryGetValue(key, out var v) && v is double d ? (int)d : (int?)null;

        public static double? NullableDouble(Dictionary<string, object> json, string key) =>
            json != null && json.TryGetValue(key, out var v) && v is double d ? d : (double?)null;

        public static Dictionary<string, object> Obj(Dictionary<string, object> json, string key) =>
            json != null && json.TryGetValue(key, out var v) ? v as Dictionary<string, object> : null;
    }
}
