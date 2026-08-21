using System.Collections.Generic;
using System.Threading.Tasks;

namespace Alogame.SDK
{
    /// <summary>
    /// Mirrors `AlogamePayment.ts` 1:1. Wired end-to-end on both platforms via the shared
    /// `OEGBridge.kt` / `OEGBridge.swift` glue — <see cref="Purchase"/>/<see cref="RestorePurchases"/>
    /// go through the same full purchase+verify flow the built-in UI uses. This is the highest-
    /// priority method in the whole bridge surface — see SDK-ALOGAME-BRIDGE-RELEASE-PLAN.md §2.2d.
    /// </summary>
    public static class AlogamePayment
    {
        public static async Task<List<ProductDetails>> QueryProducts(IEnumerable<string> productIds, ProductType productType = ProductType.Inapp)
        {
            var raw = await Bridge.AlogameBridge.CallNative("payment.queryProducts", new Dictionary<string, object>
            {
                ["productIds"] = new List<object>(productIds),
                ["productType"] = productType.ToWireValue(),
            });
            return ProductDetails.ListFromJson(raw);
        }

        /// <param name="productId">Store product id (SKU)</param>
        /// <param name="gameData">Recommended — serverId/roleId are required by server-side verification.</param>
        public static async Task<PurchaseResult> Purchase(string productId, GameData gameData = null)
        {
            var raw = await Bridge.AlogameBridge.CallNative("payment.purchase", new Dictionary<string, object>
            {
                ["productId"] = productId,
                ["gameData"] = gameData?.ToJson(),
            });
            return PurchaseResult.FromJson(raw);
        }

        public static async Task<RestoreResult> RestorePurchases(GameData gameData = null)
        {
            var raw = await Bridge.AlogameBridge.CallNative("payment.restorePurchases", new Dictionary<string, object>
            {
                ["gameData"] = gameData?.ToJson(),
            });
            return RestoreResult.FromJson(raw);
        }

        /// <summary>Returns true if there are unverified/pending purchases for the current user still awaiting server ack.</summary>
        public static async Task<bool> HasPendingPurchases()
        {
            var raw = await Bridge.AlogameBridge.CallNative("payment.hasPendingPurchases", null);
            return JsonField.Bool(raw as Dictionary<string, object>, "hasPending");
        }
    }
}
