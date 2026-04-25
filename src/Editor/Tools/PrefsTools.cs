using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEngine;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// EditorPrefs (per-user-machine, persists across projects) and
    /// PlayerPrefs (per-app, persists across runs) read/write/list/delete.
    /// PlayerPrefs has no built-in enumeration on standalone — listing
    /// reads the registry on Windows or the .plist file on macOS via
    /// reflection where possible, returns an empty array otherwise.
    /// </summary>
    internal static class PrefsTools
    {
        // ---------- editor-prefs-get ----------
        [ReifyTool("editor-prefs-get")]
        public static Task<object> EditorGet(JToken args)
        {
            var key = RequireKey(args);
            var typeHint = (args?.Value<string>("type") ?? "auto").ToLowerInvariant();
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                if (!EditorPrefs.HasKey(key))
                    return new { key, exists = false, value = (object)null };
                object val = typeHint switch
                {
                    "string" => EditorPrefs.GetString(key),
                    "int"    => (object)EditorPrefs.GetInt(key),
                    "bool"   => (object)EditorPrefs.GetBool(key),
                    "float"  => (object)EditorPrefs.GetFloat(key),
                    _        => EditorPrefs.GetString(key) // best-effort
                };
                return new { key, exists = true, value = val, type_hint = typeHint };
            });
        }

        // ---------- editor-prefs-set ----------
        [ReifyTool("editor-prefs-set")]
        public static Task<object> EditorSet(JToken args)
        {
            var key = RequireKey(args);
            var val = args?["value"] ?? throw new ArgumentException("value is required.");
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                switch (val.Type)
                {
                    case JTokenType.Integer: EditorPrefs.SetInt(key, val.Value<int>()); break;
                    case JTokenType.Float:   EditorPrefs.SetFloat(key, val.Value<float>()); break;
                    case JTokenType.Boolean: EditorPrefs.SetBool(key, val.Value<bool>()); break;
                    default:                  EditorPrefs.SetString(key, val.Value<string>() ?? ""); break;
                }
                return new { key, set = true, value = val.ToString(), type = val.Type.ToString() };
            });
        }

        // ---------- editor-prefs-delete ----------
        [ReifyTool("editor-prefs-delete")]
        public static Task<object> EditorDelete(JToken args)
        {
            var key = RequireKey(args);
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                bool existed = EditorPrefs.HasKey(key);
                EditorPrefs.DeleteKey(key);
                return new { key, deleted = existed };
            });
        }

        // ---------- player-prefs-get ----------
        [ReifyTool("player-prefs-get")]
        public static Task<object> PlayerGet(JToken args)
        {
            var key = RequireKey(args);
            var typeHint = (args?.Value<string>("type") ?? "auto").ToLowerInvariant();
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                if (!PlayerPrefs.HasKey(key))
                    return new { key, exists = false, value = (object)null };
                object val = typeHint switch
                {
                    "string" => PlayerPrefs.GetString(key),
                    "int"    => (object)PlayerPrefs.GetInt(key),
                    "float"  => (object)PlayerPrefs.GetFloat(key),
                    _        => PlayerPrefs.GetString(key, PlayerPrefs.GetInt(key, 0).ToString())
                };
                return new { key, exists = true, value = val, type_hint = typeHint };
            });
        }

        // ---------- player-prefs-set ----------
        [ReifyTool("player-prefs-set")]
        public static Task<object> PlayerSet(JToken args)
        {
            var key = RequireKey(args);
            var val = args?["value"] ?? throw new ArgumentException("value is required.");
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                switch (val.Type)
                {
                    case JTokenType.Integer: PlayerPrefs.SetInt(key, val.Value<int>()); break;
                    case JTokenType.Float:   PlayerPrefs.SetFloat(key, val.Value<float>()); break;
                    default:                  PlayerPrefs.SetString(key, val.Value<string>() ?? ""); break;
                }
                PlayerPrefs.Save();
                return new { key, set = true, value = val.ToString(), type = val.Type.ToString() };
            });
        }

        // ---------- player-prefs-delete ----------
        [ReifyTool("player-prefs-delete")]
        public static Task<object> PlayerDelete(JToken args)
        {
            var key = RequireKey(args);
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                bool existed = PlayerPrefs.HasKey(key);
                PlayerPrefs.DeleteKey(key);
                PlayerPrefs.Save();
                return new { key, deleted = existed };
            });
        }

        // ---------- editor-prefs-list ----------
        // EditorPrefs has no enumeration API. On Windows we can read the
        // registry under HKCU\Software\Unity Technologies\Unity Editor 5.x.
        // Other platforms return an empty list with a note.
        [ReifyTool("editor-prefs-list")]
        public static Task<object> EditorList(JToken args)
        {
            var prefix = args?.Value<string>("prefix");
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var keys = new List<string>();
                string note = null;
#if UNITY_EDITOR_WIN
                try
                {
                    var rk = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                        @"Software\Unity Technologies\Unity Editor 5.x");
                    if (rk != null)
                    {
                        foreach (var name in rk.GetValueNames())
                        {
                            // Unity stores with a "_h<hash>" suffix on the value name
                            // e.g. "MyPref_h1234567890" — strip the suffix to recover
                            // the original key string.
                            int hIdx = name.LastIndexOf("_h", StringComparison.Ordinal);
                            string original = hIdx > 0 ? name.Substring(0, hIdx) : name;
                            if (string.IsNullOrEmpty(prefix) || original.StartsWith(prefix, StringComparison.Ordinal))
                                keys.Add(original);
                        }
                    }
                    else note = "HKCU\\Software\\Unity Technologies\\Unity Editor 5.x not found.";
                }
                catch (Exception ex) { note = "Registry read failed: " + ex.Message; }
#else
                note = "Per-platform EditorPrefs enumeration not implemented for this OS yet.";
#endif
                keys.Sort(StringComparer.Ordinal);
                return new { prefix, count = keys.Count, keys = keys.ToArray(), note };
            });
        }

        private static string RequireKey(JToken args)
            => args?.Value<string>("key") ?? throw new ArgumentException("key is required.");
    }
}
