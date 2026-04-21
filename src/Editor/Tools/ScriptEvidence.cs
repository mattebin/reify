using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// Evidence projection for C# script assets. This keeps the script tools
    /// aligned with reify's thesis: return code-grounded facts, not just raw
    /// file bytes.
    /// </summary>
    internal static class ScriptEvidence
    {
        private static readonly Regex NamespaceRegex = new(
            @"^\s*namespace\s+([A-Za-z_][\w\.]*)\s*(?:;|{)",
            RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly Regex UsingRegex = new(
            @"^\s*using\s+([^;]+);",
            RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly Regex TypeRegex = new(
            @"^(?<indent>\s*)(?<mods>(?:(?:public|internal|private|protected|static|abstract|sealed|partial)\s+)*)" +
            @"(?<kind>class|struct|interface|enum|record)\s+(?<name>[A-Za-z_]\w*)",
            RegexOptions.Multiline | RegexOptions.Compiled);

        public static string AbsolutePath(string assetPath)
        {
            ValidateAssetPath(assetPath);

            var projectRoot = Path.GetDirectoryName(Application.dataPath)
                ?? throw new InvalidOperationException("Could not resolve Unity project root.");

            var relative = assetPath.Replace('/', Path.DirectorySeparatorChar);
            var full = Path.GetFullPath(Path.Combine(projectRoot, relative));
            var root = Path.GetFullPath(projectRoot + Path.DirectorySeparatorChar);

            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Resolved path escapes the Unity project root: {assetPath}");

            return full;
        }

        public static void EnsureParentFolder(string assetPath)
        {
            var abs = AbsolutePath(assetPath);
            var parent = Path.GetDirectoryName(abs);
            if (string.IsNullOrEmpty(parent))
                throw new InvalidOperationException($"Could not resolve a parent directory for {assetPath}");
            Directory.CreateDirectory(parent);
        }

        public static object Summarize(string assetPath, string text)
        {
            var abs = AbsolutePath(assetPath);
            var guid = AssetDatabase.AssetPathToGUID(assetPath);
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(assetPath);
            var fileName = Path.GetFileName(assetPath);
            var fileStem = Path.GetFileNameWithoutExtension(assetPath);

            var usingDirectives = new List<string>();
            foreach (Match m in UsingRegex.Matches(text))
                usingDirectives.Add(m.Groups[1].Value.Trim());

            var declarations = new List<object>();
            var declaredNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match m in TypeRegex.Matches(text))
            {
                var name = m.Groups["name"].Value;
                declaredNames.Add(name);
                declarations.Add(new
                {
                    kind       = m.Groups["kind"].Value,
                    name,
                    modifiers  = m.Groups["mods"].Value.Trim(),
                    line_number = 1 + CountNewlines(text, m.Index)
                });
            }

            var warnings = new List<string>();
            if (declarations.Count == 0)
                warnings.Add("No top-level type declarations found. Unity will import the file, but it won't define a usable script type.");
            if (declarations.Count > 0 && !declaredNames.Contains(fileStem))
                warnings.Add($"No top-level type matches the file name '{fileStem}'. Unity attachable scripts usually require the file name and class name to match.");
            if (assetPath.IndexOf("/Editor/", StringComparison.OrdinalIgnoreCase) >= 0)
                warnings.Add("Script is under an Editor folder and will compile into an editor-only assembly by folder convention.");
            if (script == null)
                warnings.Add("MonoScript asset could not be loaded after import. Check the Unity Console for compile/import errors.");

            var ns = NamespaceRegex.Match(text);

            return new
            {
                asset_path          = assetPath,
                guid,
                file_name           = fileName,
                script_name         = script != null ? script.name : fileStem,
                exists              = File.Exists(abs),
                absolute_path       = abs.Replace('\\', '/'),
                line_count          = CountLines(text),
                char_count          = text.Length,
                sha256              = Sha256(text),
                newline_style       = DetectNewlineStyle(text),
                declared_namespace  = ns.Success ? ns.Groups[1].Value : null,
                using_directives    = usingDirectives.ToArray(),
                declaration_count   = declarations.Count,
                declarations        = declarations.ToArray(),
                file_name_matches_declared_type = declaredNames.Contains(fileStem),
                compile_scope       = assetPath.IndexOf("/Editor/", StringComparison.OrdinalIgnoreCase) >= 0
                    ? "editor_only_by_folder"
                    : "runtime_or_asmdef_defined",
                warnings            = warnings.ToArray()
            };
        }

        public static void ValidateAssetPath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                throw new ArgumentException("asset_path is required.");
            if (!assetPath.StartsWith("Assets/", StringComparison.Ordinal))
                throw new ArgumentException($"asset_path must be under Assets/: {assetPath}");
            if (!assetPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"asset_path must point to a .cs file: {assetPath}");
        }

        private static int CountLines(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            var lines = 1;
            for (var i = 0; i < text.Length; i++)
                if (text[i] == '\n') lines++;
            return lines;
        }

        private static int CountNewlines(string text, int endExclusive)
        {
            var count = 0;
            for (var i = 0; i < endExclusive && i < text.Length; i++)
                if (text[i] == '\n') count++;
            return count;
        }

        private static string DetectNewlineStyle(string text)
        {
            if (string.IsNullOrEmpty(text)) return "none";

            var hasCrLf = text.Contains("\r\n");
            var hasLf = text.Contains("\n");
            var lfWithoutCr = text.Replace("\r\n", string.Empty)
                                  .Contains("\n");

            if (hasCrLf && lfWithoutCr) return "mixed";
            if (hasCrLf) return "CRLF";
            if (hasLf) return "LF";
            return "none";
        }

        private static string Sha256(string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(bytes);
            var sb = new StringBuilder(hash.Length * 2);
            for (var i = 0; i < hash.Length; i++)
                sb.Append(hash[i].ToString("x2"));
            return sb.ToString();
        }
    }
}
