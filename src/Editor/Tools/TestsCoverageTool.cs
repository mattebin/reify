using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// Whole-project test-coverage view: every asmdef in the project,
    /// flagged with whether it has a paired test assembly. Pair detection
    /// uses two heuristics: (a) test asmdef name contains the source
    /// asmdef name (e.g. Foo.Bar → Foo.Bar.Tests), (b) test asmdef
    /// references the source asmdef. An asmdef counts as a "test
    /// assembly" if its precompiledReferences include nunit.framework.dll
    /// or its name ends in ".Tests" / ".Test".
    /// </summary>
    internal static class TestsCoverageTool
    {
        // ---------- tests-coverage-map ----------
        [ReifyTool("tests-coverage-map")]
        public static Task<object> CoverageMap(JToken args)
        {
            var includeBuiltin = args?.Value<bool?>("include_builtin") ?? false;

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var allAsmdefGuids = AssetDatabase.FindAssets("t:AssemblyDefinitionAsset");
                var asmInfos = new List<AsmInfo>();
                foreach (var guid in allAsmdefGuids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(path)) continue;
                    if (!includeBuiltin && path.StartsWith("Packages/")) continue;

                    string json;
                    try { json = File.ReadAllText(path); }
                    catch { continue; }

                    JObject parsed;
                    try { parsed = JObject.Parse(json); }
                    catch { continue; }

                    var info = new AsmInfo
                    {
                        AsmdefPath = path,
                        Name       = parsed.Value<string>("name") ?? Path.GetFileNameWithoutExtension(path),
                        References = (parsed["references"] as JArray)?.Select(t => t.Value<string>()).ToArray() ?? Array.Empty<string>(),
                        Precompiled = (parsed["precompiledReferences"] as JArray)?.Select(t => t.Value<string>()).ToArray() ?? Array.Empty<string>(),
                        Defines    = (parsed["defineConstraints"] as JArray)?.Select(t => t.Value<string>()).ToArray() ?? Array.Empty<string>(),
                    };
                    info.IsTestAssembly = LooksLikeTestAssembly(info);
                    asmInfos.Add(info);
                }

                // Pair test assemblies to their source assemblies.
                var byName = asmInfos.ToDictionary(a => a.Name, StringComparer.OrdinalIgnoreCase);
                var coverageBySource = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

                foreach (var test in asmInfos.Where(a => a.IsTestAssembly))
                {
                    foreach (var refName in test.References)
                    {
                        if (byName.ContainsKey(refName) && !byName[refName].IsTestAssembly)
                        {
                            if (!coverageBySource.TryGetValue(refName, out var list))
                                coverageBySource[refName] = list = new List<string>();
                            list.Add(test.Name);
                        }
                    }
                    // Heuristic name match (Foo → Foo.Tests)
                    foreach (var src in asmInfos.Where(a => !a.IsTestAssembly))
                    {
                        if (test.Name.StartsWith(src.Name + ".", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!coverageBySource.TryGetValue(src.Name, out var list))
                                coverageBySource[src.Name] = list = new List<string>();
                            if (!list.Contains(test.Name)) list.Add(test.Name);
                        }
                    }
                }

                var sourceCount   = asmInfos.Count(a => !a.IsTestAssembly);
                var testCount     = asmInfos.Count(a => a.IsTestAssembly);
                var coveredCount  = coverageBySource.Count;

                var sources = asmInfos
                    .Where(a => !a.IsTestAssembly)
                    .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(a => new
                    {
                        name              = a.Name,
                        asmdef_path       = a.AsmdefPath,
                        has_test_assembly = coverageBySource.ContainsKey(a.Name),
                        test_assemblies   = coverageBySource.TryGetValue(a.Name, out var l) ? l.ToArray() : Array.Empty<string>()
                    })
                    .ToArray();

                return new
                {
                    source_assembly_count   = sourceCount,
                    test_assembly_count     = testCount,
                    covered_source_count    = coveredCount,
                    coverage_ratio          = sourceCount == 0 ? 0.0 : (double)coveredCount / sourceCount,
                    uncovered = sources.Where(s => !s.has_test_assembly).Select(s => s.name).ToArray(),
                    sources,
                    test_assemblies = asmInfos.Where(a => a.IsTestAssembly).Select(a => a.Name).ToArray(),
                    note = "An assembly counts as 'covered' if a test asmdef references it OR a test asmdef's name starts with the source's name + '.'.",
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }

        private static bool LooksLikeTestAssembly(AsmInfo a)
        {
            if (a.Name.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase)) return true;
            if (a.Name.EndsWith(".Test",  StringComparison.OrdinalIgnoreCase)) return true;
            foreach (var p in a.Precompiled)
                if (p != null && p.IndexOf("nunit", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            foreach (var r in a.References)
                if (r == "UnityEngine.TestRunner" || r == "UnityEditor.TestRunner") return true;
            foreach (var d in a.Defines)
                if (d == "UNITY_INCLUDE_TESTS") return true;
            return false;
        }

        private class AsmInfo
        {
            public string   Name;
            public string   AsmdefPath;
            public string[] References    = Array.Empty<string>();
            public string[] Precompiled   = Array.Empty<string>();
            public string[] Defines       = Array.Empty<string>();
            public bool     IsTestAssembly;
        }
    }
}
