using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// Builds {path, guid, type_fqn, instance_id} records for write-tool
    /// `guids_touched` receipts. Nothing in CoplayDev / IvanMurzak exposes
    /// this; it is a differentiator for write-side trust.
    /// </summary>
    internal static class AssetProvenance
    {
        public static object[] Summarize(IEnumerable<string> assetPaths)
        {
            if (assetPaths == null) return System.Array.Empty<object>();
            var list = new List<object>();
            foreach (var p in assetPaths)
            {
                if (string.IsNullOrEmpty(p)) continue;
                list.Add(Summarize(p));
            }
            return list.ToArray();
        }

        public static object Summarize(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return new { path = (string)null, guid = (string)null, type_fqn = (string)null, instance_id = 0 };

            var guid = AssetDatabase.AssetPathToGUID(assetPath);
            var obj = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
            return new
            {
                path        = assetPath,
                guid        = string.IsNullOrEmpty(guid) ? null : guid,
                type_fqn    = obj != null ? obj.GetType().FullName : null,
                instance_id = obj != null ? GameObjectResolver.InstanceIdOf(obj) : 0
            };
        }
    }
}
