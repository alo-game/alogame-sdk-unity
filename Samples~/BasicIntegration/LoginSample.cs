using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Alogame.SDK;
using UnityEngine;

namespace Alogame.SDK.Samples
{
    /// <summary>
    /// Every SDK feature behind a button, for trying the package on a device. Attach to any
    /// GameObject in your first scene and set the game id. IMGUI only, so it needs no scene or
    /// prefab. Results and events are logged to the on-screen console and the Unity log.
    ///
    /// The calls show the intended integration:
    ///   1. Initialize once at boot.
    ///   2. Subscribe to <see cref="AlogameAuth.SessionEnded"/> / <see cref="AlogameAuth.LoggedIn"/>:
    ///      the SDK's dashboard can log the player out and back in on its own.
    ///   3. ShowLoginUI, then SetGameRole when the player picks a character.
    ///   4. Purchase with the server / role ids your game server knows.
    /// A rejected call throws <see cref="AlogameSdkException"/>; the code is in <c>e.Error.Code</c>.
    /// </summary>
    public class LoginSample : MonoBehaviour
    {
        [Tooltip("Your game id from the Alogame Operation Manager.")]
        [SerializeField] private int gameId;

        [Tooltip("Dev backend + verbose HTTP logging. Never on in a release build.")]
        [SerializeField] private bool debugEnv;

        [SerializeField] private string productId = "gold_100";
        [SerializeField] private string serverId = "1";
        [SerializeField] private string roleId = "10001";

        private readonly List<string> _log = new();
        private Vector2 _scroll;
        private string _username = "", _password = "";

        private void Awake()
        {
            if (gameId <= 0)
            {
                Log("Set gameId on LoginSample first.");
                enabled = false;
                return;
            }

            AlogameSDK.Initialize(gameId, debugEnv);

            AlogameAuth.SessionEnded += message => Log("EVENT sessionEnded: " + message + " — back to title screen");
            AlogameAuth.LoggedIn += user => Log("EVENT loggedIn: " + user?.Username);
            AlogamePush.MessageReceived += data => Log("EVENT push: " + string.Join(", ", data.Keys));
            AlogamePush.TokenRefreshed += token => Log("EVENT push token: " + token);
            AlogameAnalytics.DeepLinkResolved += data =>
                Log("EVENT deep link: " + (data.TryGetValue("deep_link_value", out var v) ? v : "(none)"));
        }

        private void OnGUI()
        {
            var scale = Mathf.Max(1f, Screen.dpi / 160f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            var width = Screen.width / scale;

            GUILayout.BeginArea(new Rect(8, 8, width - 16, Screen.height / scale - 16));
            _scroll = GUILayout.BeginScrollView(_scroll);

            GUILayout.Label("Auth");
            Row(("Show login UI", () => Run("ShowLoginUI", async () => Describe(await AlogameAuth.ShowLoginUI()))),
                ("Play now", () => Run("PlayNow", async () => Describe(await AlogameAuth.PlayNow()))),
                ("Current user", () => Run("GetCurrentUser", async () => Describe(await AlogameAuth.GetCurrentUser()))));
            GUILayout.BeginHorizontal();
            _username = GUILayout.TextField(_username, GUILayout.Width(width * 0.3f));
            _password = GUILayout.PasswordField(_password, '*', GUILayout.Width(width * 0.3f));
            if (GUILayout.Button("Login")) Run("Login", async () => Describe(await AlogameAuth.Login(_username, _password)));
            if (GUILayout.Button("Link Alo ID")) Run("LinkAloId", async () => Describe(await AlogameAuth.LinkAloId(_username, _password)));
            GUILayout.EndHorizontal();
            Row(("Set game role", () => Run("SetGameRole", async () => { await AlogameAuth.SetGameRole(serverId, roleId, "S" + serverId, "Hero", 10); return "ok"; })),
                ("Get game role", () => Run("GetGameRole", async () => { var r = await AlogameAuth.GetGameRole(); return r == null ? "none" : r.ServerId + "/" + r.RoleId + " lv" + r.Level; })),
                ("Logout", () => Run("Logout", async () => { await AlogameAuth.Logout(); await AlogameSDK.HideFloatingButton(); return "ok"; })));

            GUILayout.Label("SDK screens");
            Row(("Dashboard", () => Run("ShowDashboard", async () => { await AlogameSDK.ShowDashboard(); return "shown"; })),
                ("Change password", () => Run("ShowChangePassword", async () => { await AlogameAuth.ShowChangePassword(); return "changed"; })),
                ("Update profile", () => Run("ShowUpdateProfile", async () => Describe(await AlogameAuth.ShowUpdateProfile()))));
            Row(("Show bubble", () => Run("ShowFloatingButton", async () => { await AlogameSDK.ShowFloatingButton(); return "ok"; })),
                ("Hide bubble", () => Run("HideFloatingButton", async () => { await AlogameSDK.HideFloatingButton(); return "ok"; })));

            GUILayout.Label("Payment");
            Row(("Query", () => Run("QueryProducts", async () =>
                {
                    var list = await AlogamePayment.QueryProducts(new[] { productId });
                    return list.Count == 0 ? "no products" : list[0].ProductId + " " + list[0].Price;
                })),
                ("Buy " + productId, () => Run("Purchase", async () =>
                {
                    var r = await AlogamePayment.Purchase(productId, new GameData { ServerId = serverId, RoleId = roleId, RoleName = "Hero", Level = 10 });
                    return r.Success ? "paid " + r.ProductId : "not completed";
                })),
                ("Restore", () => Run("RestorePurchases", async () => (await AlogamePayment.RestorePurchases(new GameData { ServerId = serverId, RoleId = roleId })).Success ? "restored" : "nothing")),
                ("Pending?", () => Run("HasPendingPurchases", async () => (await AlogamePayment.HasPendingPurchases()).ToString())));

            GUILayout.Label("Push / analytics / account");
            Row(("Push permission", () => Run("RequestPermission", async () => (await AlogamePush.RequestPermission()).ToString())),
                ("Push token", () => Run("GetDeviceToken", async () => await AlogamePush.GetDeviceToken() ?? "null")),
                ("Log event", () => Run("LogEvent", async () => { await AlogameAnalytics.LogEvent("af_tutorial_completion"); return "ok"; })),
                ("Account status", () => Run("GetStatus", async () => (await AlogameAccount.GetStatus()).Status)));

            GUILayout.Space(8);
            foreach (var line in _log) GUILayout.Label(line);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private static void Row(params (string Label, Action OnClick)[] buttons)
        {
            GUILayout.BeginHorizontal();
            foreach (var (label, onClick) in buttons)
                if (GUILayout.Button(label, GUILayout.Height(44))) onClick();
            GUILayout.EndHorizontal();
        }

        private async void Run(string name, Func<Task<string>> call)
        {
            try { Log(name + ": " + await call()); }
            catch (AlogameSdkException e) { Log(name + " rejected [" + e.Error?.Code + "] " + e.Message); }
            catch (Exception e) { Log(name + " threw " + e); }
        }

        private static string Describe(AuthResult r) => r.User != null ? "user " + Describe(r.User) : (r.Success ? "ok" : "failed");
        private static string Describe(AuthUser u) => u == null ? "null" : u.Username + " uuid=" + u.Uuid + " guest=" + u.IsPlayNow;

        private void Log(string line)
        {
            Debug.Log("[Alogame sample] " + line);
            _log.Insert(0, DateTime.Now.ToString("HH:mm:ss") + "  " + line);
            if (_log.Count > 60) _log.RemoveAt(_log.Count - 1);
        }
    }
}
