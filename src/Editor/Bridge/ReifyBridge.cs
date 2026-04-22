using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Reify.Editor.Bridge
{
    /// <summary>
    /// HTTP listener that the Reify MCP server talks to.
    ///
    /// Protocol: POST /tool with body {"tool":"name","args":{...}}. Response
    /// is {"ok":true,"data":{...}} or {"ok":false,"error":{"code","message"}}.
    /// See /docs/ARCH_DECISION.md § HTTP bridge contract for the full spec.
    /// </summary>
    [InitializeOnLoad]
    internal static class ReifyBridge
    {
        private static readonly Dictionary<string, Func<JToken, Task<object>>> Handlers = new();
        private const int DefaultMaxResponseBytes = 786432; // 768 KiB

        private static HttpListener _listener;
        private static CancellationTokenSource _cts;
        private static int _port;

        static ReifyBridge()
        {
            // InitializeOnLoad fires on every domain reload — stop and restart.
            EditorApplication.quitting += Stop;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;

            AutoRegister();

            Start();
        }

        /// <summary>
        /// Scan the containing assembly for static methods decorated with
        /// <see cref="ReifyToolAttribute"/> and register them. This is the
        /// single source of truth for tool registration — per-tool files
        /// self-register by attribute, no manual list in this file.
        /// </summary>
        private static void AutoRegister()
        {
            var sig = typeof(Func<JToken, Task<object>>);
            var assembly = typeof(ReifyBridge).Assembly;
            var duplicates = new List<string>();

            foreach (var type in assembly.GetTypes())
            {
                MethodInfo[] methods;
                try { methods = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic); }
                catch { continue; }

                foreach (var method in methods)
                {
                    var attr = method.GetCustomAttribute<ReifyToolAttribute>();
                    if (attr == null) continue;

                    Delegate d;
                    try { d = Delegate.CreateDelegate(sig, method); }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[Reify] Tool '{attr.Name}' on {type.FullName}.{method.Name} " +
                                       $"has wrong signature (expected static Task<object>(JToken)): {ex.Message}");
                        continue;
                    }

                    if (Handlers.ContainsKey(attr.Name)) duplicates.Add(attr.Name);
                    Handlers[attr.Name] = (Func<JToken, Task<object>>)d;
                }
            }

            if (duplicates.Count > 0)
                Debug.LogError($"[Reify] Duplicate tool name(s): {string.Join(", ", duplicates)}");
        }

        /// <summary>Expose the registered tool names for introspection tools.</summary>
        public static IEnumerable<string> GetRegisteredToolNames() => Handlers.Keys;

        /// <summary>Look up a handler for batch-execute / meta routing.</summary>
        public static bool TryGetHandler(string name, out Func<JToken, Task<object>> handler)
            => Handlers.TryGetValue(name, out handler);

        private static void Start()
        {
            _port = ResolvePort();
            _cts  = new CancellationTokenSource();

            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
                _listener.Start();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Reify] Failed to bind bridge to 127.0.0.1:{_port}. {ex.Message}");
                return;
            }

            Debug.Log($"[Reify] Bridge listening on http://127.0.0.1:{_port}/");
            _ = AcceptLoop(_cts.Token);
        }

        private static void Stop()
        {
            try { _cts?.Cancel(); } catch { /* ignore */ }
            try { _listener?.Stop(); } catch { /* ignore */ }
            _listener = null;
        }

        private static int ResolvePort()
        {
            var fromEnv = Environment.GetEnvironmentVariable("REIFY_BRIDGE_PORT");
            return int.TryParse(fromEnv, out var p) && p > 0 ? p : 17777;
        }

        private static async Task AcceptLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _listener is { IsListening: true })
            {
                HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync(); }
                catch { break; }

                _ = Task.Run(() => Handle(ctx), ct);
            }
        }

        private static async Task Handle(HttpListenerContext ctx)
        {
            try
            {
                if (ctx.Request.HttpMethod != "POST" || ctx.Request.Url?.AbsolutePath != "/tool")
                {
                    await WriteError(ctx, 404, "NOT_FOUND", "Only POST /tool is supported");
                    return;
                }

                string body;
                using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                    body = await reader.ReadToEndAsync();

                var envelope = JObject.Parse(body);
                var tool = envelope.Value<string>("tool");
                var args = envelope["args"];

                if (string.IsNullOrEmpty(tool) || !Handlers.TryGetValue(tool, out var handler))
                {
                    await WriteError(ctx, 404, "UNKNOWN_TOOL", $"No handler for tool '{tool}'");
                    return;
                }

                object data;
                try
                {
                    data = await handler(args);
                }
                catch (Exception ex)
                {
                    await WriteError(ctx, 500, "TOOL_EXCEPTION", ex.Message);
                    return;
                }

                var json = JsonConvert.SerializeObject(new { ok = true, data });
                var bytes = Encoding.UTF8.GetBytes(json);
                var maxResponseBytes = ResolveMaxResponseBytes();
                if (bytes.Length > maxResponseBytes)
                {
                    await WriteError(
                        ctx,
                        413,
                        "RESPONSE_TOO_LARGE",
                        $"Tool '{tool}' produced {bytes.Length} UTF-8 bytes, exceeding the configured " +
                        $"response cap of {maxResponseBytes} bytes. Narrow the query, paginate the " +
                        "result, or raise REIFY_MAX_RESPONSE_BYTES if this size is intentional.");
                    return;
                }

                await WriteBody(ctx, 200, bytes);
            }
            catch (Exception ex)
            {
                try { await WriteError(ctx, 500, "BRIDGE_FAILURE", ex.Message); }
                catch { /* connection already gone */ }
            }
        }

        private static async Task WriteError(HttpListenerContext ctx, int status, string code, string message)
        {
            var json = JsonConvert.SerializeObject(new { ok = false, error = new { code, message } });
            await WriteBody(ctx, status, Encoding.UTF8.GetBytes(json));
        }

        private static async Task WriteBody(HttpListenerContext ctx, int status, byte[] bytes)
        {
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json";
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            ctx.Response.OutputStream.Close();
        }

        private static int ResolveMaxResponseBytes()
        {
            var fromEnv = Environment.GetEnvironmentVariable("REIFY_MAX_RESPONSE_BYTES");
            return int.TryParse(fromEnv, out var parsed) && parsed > 16384
                ? parsed
                : DefaultMaxResponseBytes;
        }
    }
}
