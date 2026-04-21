using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEngine;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// Meta + batch tools. batch-execute runs multiple tool calls in a
    /// single round trip. reify-tool-list enumerates every registered
    /// handler grouped by domain. reify-version returns build info.
    /// reflection-method-find / -call are the escape hatch for anything
    /// reify doesn't expose natively.
    /// </summary>
    internal static class MetaAndBatchTools
    {
        // ---------- batch-execute ----------
        [ReifyTool("batch-execute")]
        public static async Task<object> BatchExecute(JToken args)
        {
            var calls = args?["calls"] as JArray
                ?? throw new ArgumentException("'calls' array is required. Each entry: { tool, args? }.");
            var stopOnError = args?.Value<bool?>("stop_on_error") ?? false;

            var results = new List<object>(calls.Count);
            var successCount = 0;
            var failureCount = 0;

            foreach (var call in calls)
            {
                var toolName = call?.Value<string>("tool");
                var toolArgs = call?["args"];
                if (string.IsNullOrEmpty(toolName))
                {
                    failureCount++;
                    results.Add(new { ok = false, error = new { code = "BAD_CALL", message = "each call needs a 'tool' string field" } });
                    if (stopOnError) break;
                    continue;
                }

                if (!ReifyBridge.TryGetHandler(toolName, out var handler))
                {
                    failureCount++;
                    results.Add(new { tool = toolName, ok = false, error = new { code = "UNKNOWN_TOOL", message = $"No handler for tool '{toolName}'" } });
                    if (stopOnError) break;
                    continue;
                }

                try
                {
                    var data = await handler(toolArgs);
                    successCount++;
                    results.Add(new { tool = toolName, ok = true, data });
                }
                catch (Exception ex)
                {
                    failureCount++;
                    results.Add(new { tool = toolName, ok = false, error = new { code = "TOOL_EXCEPTION", message = ex.Message } });
                    if (stopOnError) break;
                }
            }

            return new
            {
                requested      = calls.Count,
                executed       = results.Count,
                success_count  = successCount,
                failure_count  = failureCount,
                stopped_early  = stopOnError && failureCount > 0 && results.Count < calls.Count,
                results        = results.ToArray(),
                read_at_utc    = DateTime.UtcNow.ToString("o"),
                frame          = (long)Time.frameCount
            };
        }

        // ---------- reify-tool-list ----------
        [ReifyTool("reify-tool-list")]
        public static Task<object> ToolList(JToken _)
        {
            var names = ReifyBridge.GetRegisteredToolNames().OrderBy(n => n, StringComparer.Ordinal).ToArray();

            // Group by first kebab segment (e.g. "gameobject" from "gameobject-create").
            var byDomain = new Dictionary<string, List<string>>();
            foreach (var n in names)
            {
                var dash = n.IndexOf('-');
                var domain = dash > 0 ? n.Substring(0, dash) : n;
                if (!byDomain.TryGetValue(domain, out var list))
                {
                    list = new List<string>();
                    byDomain[domain] = list;
                }
                list.Add(n);
            }

            var domains = byDomain
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => new
                {
                    domain = kv.Key,
                    count  = kv.Value.Count,
                    tools  = kv.Value.ToArray()
                })
                .ToArray();

            return Task.FromResult<object>(new
            {
                total_count = names.Length,
                domain_count = domains.Length,
                domains,
                all_tools = names,
                read_at_utc = DateTime.UtcNow.ToString("o"),
                frame       = (long)Time.frameCount
            });
        }

        // ---------- reify-version ----------
        [ReifyTool("reify-version")]
        public static Task<object> Version(JToken _)
        {
            var asm = typeof(ReifyBridge).Assembly;
            var asmName = asm.GetName();
            var asmVersion = asmName.Version?.ToString() ?? "0.0.0";

            return Task.FromResult<object>(new
            {
                package_name       = "com.reify.unity",
                assembly_name      = asmName.Name,
                assembly_version   = asmVersion,
                bridge_type_fqn    = typeof(ReifyBridge).FullName,
                runtime_version    = Environment.Version.ToString(),
                unity_version      = Application.unityVersion,
                tool_count         = ReifyBridge.GetRegisteredToolNames().Count(),
                read_at_utc        = DateTime.UtcNow.ToString("o"),
                frame              = (long)Time.frameCount
            });
        }

        // ---------- reflection-method-find ----------
        [ReifyTool("reflection-method-find")]
        public static Task<object> ReflectionMethodFind(JToken args)
        {
            var typeName   = args?.Value<string>("type_name") ?? throw new ArgumentException("type_name required (FQN preferred).");
            var methodName = args?.Value<string>("method_name");
            var nameLike   = args?.Value<string>("name_like");  // substring, case-insensitive
            var staticOnly = args?.Value<bool?>("static_only") ?? false;
            var limit      = args?.Value<int?>("limit") ?? 100;

            var type = ResolveType(typeName)
                ?? throw new InvalidOperationException($"Type '{typeName}' not found in any loaded assembly.");

            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            var all = type.GetMethods(flags);
            var matches = new List<object>();
            var truncated = false;

            foreach (var m in all)
            {
                if (staticOnly && !m.IsStatic) continue;
                if (!string.IsNullOrEmpty(methodName) && m.Name != methodName) continue;
                if (!string.IsNullOrEmpty(nameLike) &&
                    m.Name.IndexOf(nameLike, StringComparison.OrdinalIgnoreCase) < 0) continue;

                if (matches.Count >= limit) { truncated = true; break; }

                var ps = m.GetParameters();
                var paramDtos = new object[ps.Length];
                for (var i = 0; i < ps.Length; i++)
                {
                    paramDtos[i] = new
                    {
                        name      = ps[i].Name,
                        type_fqn  = ps[i].ParameterType.FullName,
                        is_out    = ps[i].IsOut,
                        is_optional = ps[i].IsOptional,
                        has_default = ps[i].HasDefaultValue,
                        default_value = ps[i].HasDefaultValue ? ps[i].DefaultValue?.ToString() : null
                    };
                }

                matches.Add(new
                {
                    name              = m.Name,
                    declaring_type    = m.DeclaringType?.FullName,
                    return_type_fqn   = m.ReturnType.FullName,
                    is_static         = m.IsStatic,
                    is_public         = m.IsPublic,
                    is_virtual        = m.IsVirtual,
                    parameters        = paramDtos,
                    parameter_count   = ps.Length
                });
            }

            return Task.FromResult<object>(new
            {
                type_fqn      = type.FullName,
                total_methods = all.Length,
                returned      = matches.Count,
                truncated,
                methods       = matches.ToArray(),
                read_at_utc   = DateTime.UtcNow.ToString("o"),
                frame         = (long)Time.frameCount
            });
        }

        // ---------- reflection-method-call ----------
        [ReifyTool("reflection-method-call")]
        public static Task<object> ReflectionMethodCall(JToken args)
        {
            // Dangerous tool. Gated behind an explicit opt-in env var so the
            // default install can't accidentally run arbitrary reflection.
            if (Environment.GetEnvironmentVariable("REIFY_ALLOW_REFLECTION_CALL") != "1")
                throw new InvalidOperationException(
                    "reflection-method-call is disabled. Set REIFY_ALLOW_REFLECTION_CALL=1 " +
                    "in the reify-server's environment to enable.");

            var typeName    = args?.Value<string>("type_name") ?? throw new ArgumentException("type_name required.");
            var methodName  = args?.Value<string>("method_name") ?? throw new ArgumentException("method_name required.");
            var instanceId  = args?["instance_id"]?.Type == JTokenType.Integer ? args.Value<int?>("instance_id") : null;
            var argsArr     = args?["arguments"] as JArray;
            var paramTypes  = args?["parameter_types"] as JArray;

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var type = ResolveType(typeName)
                    ?? throw new InvalidOperationException($"Type '{typeName}' not found.");

                // If parameter_types is provided, use it to disambiguate overloads.
                MethodInfo method;
                if (paramTypes != null && paramTypes.Count > 0)
                {
                    var types = new Type[paramTypes.Count];
                    for (var i = 0; i < paramTypes.Count; i++)
                    {
                        var pt = ResolveType(paramTypes[i].Value<string>())
                            ?? throw new InvalidOperationException($"parameter_types[{i}] '{paramTypes[i]}' not found.");
                        types[i] = pt;
                    }
                    method = type.GetMethod(methodName,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static,
                        null, types, null)
                        ?? throw new InvalidOperationException(
                            $"No method {type.FullName}.{methodName}({string.Join(",", types.Select(t => t.Name))}) found.");
                }
                else
                {
                    var candidates = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                                         .Where(m => m.Name == methodName)
                                         .ToArray();
                    if (candidates.Length == 0)
                        throw new InvalidOperationException($"No method {type.FullName}.{methodName} found.");
                    if (candidates.Length > 1)
                        throw new InvalidOperationException(
                            $"{candidates.Length} overloads for {type.FullName}.{methodName}. Pass parameter_types[] to disambiguate.");
                    method = candidates[0];
                }

                var mps = method.GetParameters();
                var callArgs = new object[mps.Length];
                if (argsArr != null)
                {
                    for (var i = 0; i < mps.Length && i < argsArr.Count; i++)
                    {
                        var jt = argsArr[i];
                        callArgs[i] = jt.Type == JTokenType.Null ? null : jt.ToObject(mps[i].ParameterType);
                    }
                }

                object instance = null;
                if (!method.IsStatic)
                {
                    if (!instanceId.HasValue)
                        throw new ArgumentException("instance_id required for instance method.");
                    instance = GameObjectResolver.ByInstanceId(instanceId.Value)
                        ?? throw new InvalidOperationException($"No object with instance_id {instanceId}.");
                }

                object returnValue;
                try { returnValue = method.Invoke(instance, callArgs); }
                catch (TargetInvocationException tie)
                {
                    throw new InvalidOperationException($"Method threw: {tie.InnerException?.Message ?? tie.Message}");
                }

                return new
                {
                    type_fqn        = type.FullName,
                    method_name     = methodName,
                    return_type_fqn = method.ReturnType.FullName,
                    return_value    = returnValue?.ToString(),
                    return_is_null  = returnValue == null,
                    read_at_utc     = DateTime.UtcNow.ToString("o"),
                    frame           = (long)Time.frameCount
                };
            });
        }

        // ---------- helper ----------
        private static Type ResolveType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return null;
            var t = Type.GetType(typeName);
            if (t != null) return t;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    t = asm.GetType(typeName, throwOnError: false);
                    if (t != null) return t;
                }
                catch { /* ignore */ }
            }
            return null;
        }
    }
}
