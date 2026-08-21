using Alogame.SDK;
using UnityEngine;

namespace Alogame.SDK.Samples
{
    /// <summary>
    /// Minimal end-to-end walkthrough: init, built-in login UI, logEvent/logRevenue, purchase —
    /// the four call sites flagged as highest-priority in SDK-ALOGAME-BRIDGE-RELEASE-PLAN.md §2.2d.
    /// Attach to any GameObject in your bootstrap scene. Not a wired-up prefab/scene — this
    /// package ships source only (no `.unity` scene file) so it stays diffable and dependency-free.
    /// </summary>
    public class LoginSample : MonoBehaviour
    {
        private async void Start()
        {
            AlogameSDK.Initialize();

            var login = await AlogameAuth.ShowLoginUI();
            if (!login.Success)
            {
                Debug.LogError($"[LoginSample] Login failed: {login.Error?.Message}");
                return;
            }

            Debug.Log($"[LoginSample] Logged in as {login.User.Username} (uuid={login.User.Uuid})");
            await AlogameAnalytics.LogEvent("game_start");

            var purchase = await AlogamePayment.Purchase("gold_100", new GameData
            {
                ServerId = "1",
                RoleId = login.User.Uuid,
            });

            if (purchase.Success)
            {
                Debug.Log($"[LoginSample] Purchase ok: {purchase.ProductId} ({purchase.TransactionId})");
                await AlogameAnalytics.LogRevenue("iap_gold_100", 0.99, "USD", purchase.TransactionId);
            }
            else
            {
                Debug.LogWarning($"[LoginSample] Purchase failed: {purchase.Error?.Message}");
            }
        }
    }
}
