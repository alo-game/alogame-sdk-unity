using System.Threading.Tasks;

namespace Alogame.SDK
{
    /// <summary>
    /// Mirrors `AlogameAccount.ts` 1:1. Calls straight through to native `OEGAccount`. Zero call
    /// sites in either reference app this bridge was validated against — treat as less
    /// battle-tested than Auth/Payment/Analytics.
    /// </summary>
    public static class AlogameAccount
    {
        /// <summary>Request account deletion (starts the 30-day soft-delete hold).</summary>
        public static async Task<AccountResult> RequestDeletion()
        {
            var raw = await Bridge.AlogameBridge.CallNative("account.requestDeletion", null);
            return AccountResult.FromJson(raw);
        }

        /// <summary>Cancel a pending account deletion request.</summary>
        public static async Task<AccountResult> CancelDeletion()
        {
            var raw = await Bridge.AlogameBridge.CallNative("account.cancelDeletion", null);
            return AccountResult.FromJson(raw);
        }

        public static async Task<AccountStatus> GetStatus()
        {
            var raw = await Bridge.AlogameBridge.CallNative("account.getStatus", null);
            return AccountStatus.FromJson(raw);
        }
    }
}
