using System.IO;
using UnityEngine;

namespace Alogame.SDK.Editor
{
    /// <summary>Shared by the Android and iOS build hooks (each compiles only for its own target).</summary>
    internal static class AlogameAssetSearch
    {
        /// <summary>First Assets/** file with this exact name, or null.</summary>
        internal static string FindInAssets(string fileName)
        {
            var matches = Directory.GetFiles(Application.dataPath, fileName, SearchOption.AllDirectories);
            if (matches.Length > 1)
                Debug.LogWarning("[Alogame SDK] several " + fileName + " files under Assets — using " + matches[0]);
            return matches.Length > 0 ? matches[0] : null;
        }
    }
}
