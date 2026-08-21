using System.Collections.Generic;
using System.Threading.Tasks;

namespace Alogame.SDK.Bridge
{
    /// <summary>
    /// Matches native responses back to the Task that requested them, keyed by callbackId.
    /// Mirrors `CallbackRouter` in `BridgeAdapter.ts`.
    /// </summary>
    internal static class CallbackRouter
    {
        private static int _counter;
        private static readonly Dictionary<string, TaskCompletionSource<object>> Pending = new();
        private static readonly object Lock = new();

        public static string Register(TaskCompletionSource<object> tcs)
        {
            string id;
            lock (Lock)
            {
                id = $"cb_{++_counter}_unity";
                Pending[id] = tcs;
            }
            return id;
        }

        public static void Resolve(string callbackId, bool success, object data)
        {
            TaskCompletionSource<object> tcs;
            lock (Lock)
            {
                if (callbackId == null || !Pending.TryGetValue(callbackId, out tcs)) return;
                Pending.Remove(callbackId);
            }

            if (success) tcs.SetResult(data);
            else tcs.SetException(new AlogameSdkException(data as Dictionary<string, object>));
        }
    }
}
