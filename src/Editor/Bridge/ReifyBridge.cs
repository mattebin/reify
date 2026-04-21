using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
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

        private static HttpListener _listener;
        private static CancellationTokenSource _cts;
        private static int _port;

        static ReifyBridge()
        {
            // InitializeOnLoad fires on every domain reload — stop and restart.
            EditorApplication.quitting += Stop;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;

            Register("ping",         args => Tools.PingTool.Handle(args));
            Register("scene-list",   args => Tools.SceneListTool.Handle(args));
            Register("scene-open",   args => Tools.SceneOpenTool.Handle(args));
            Register("scene-save",   args => Tools.SceneSaveTool.Handle(args));
            Register("scene-create", args => Tools.SceneCreateTool.Handle(args));
            Register("mesh-native-bounds", args => Tools.MeshNativeBoundsTool.Handle(args));
            Register("gameobject-create",  args => Tools.GameObjectCreateTool.Handle(args));
            Register("gameobject-find",    args => Tools.GameObjectFindTool.Handle(args));
            Register("gameobject-destroy", args => Tools.GameObjectDestroyTool.Handle(args));
            Register("gameobject-modify",  args => Tools.GameObjectModifyTool.Handle(args));
            Register("component-add",          args => Tools.ComponentAddTool.Handle(args));
            Register("component-get",          args => Tools.ComponentGetTool.Handle(args));
            Register("component-modify",       args => Tools.ComponentModifyTool.Handle(args));
            Register("component-remove",       args => Tools.ComponentRemoveTool.Handle(args));
            Register("component-set-property", args => Tools.ComponentSetPropertyTool.Handle(args));

            Register("asset-find",   args => Tools.AssetTools.Find(args));
            Register("asset-create", args => Tools.AssetTools.Create(args));
            Register("asset-delete", args => Tools.AssetTools.Delete(args));
            Register("asset-get",    args => Tools.AssetTools.Get(args));
            Register("asset-rename", args => Tools.AssetTools.Rename(args));
            Register("asset-move",   args => Tools.AssetTools.Move(args));

            Register("prefab-create",           args => Tools.PrefabTools.Create(args));
            Register("prefab-instantiate",      args => Tools.PrefabTools.Instantiate(args));
            Register("prefab-open",             args => Tools.PrefabTools.Open(args));
            Register("prefab-close",            args => Tools.PrefabTools.Close(args));
            Register("prefab-get-overrides",    args => Tools.PrefabTools.GetOverrides(args));
            Register("prefab-apply-overrides",  args => Tools.PrefabTools.ApplyOverrides(args));
            Register("prefab-revert-overrides", args => Tools.PrefabTools.RevertOverrides(args));

            Register("material-inspect", args => Tools.MaterialInspectTool.Handle(args));

            Register("play-mode-enter",  args => Tools.PlayModeTools.Enter(args));
            Register("play-mode-exit",   args => Tools.PlayModeTools.Exit(args));
            Register("play-mode-pause",  args => Tools.PlayModeTools.Pause(args));
            Register("play-mode-resume", args => Tools.PlayModeTools.Resume(args));
            Register("play-mode-step",   args => Tools.PlayModeTools.Step(args));
            Register("play-mode-status", args => Tools.PlayModeTools.StatusTool(args));

            Register("scene-hierarchy", args => Tools.SceneHierarchyTools.Hierarchy(args));
            Register("scene-query",     args => Tools.SceneHierarchyTools.Query(args));
            Register("scene-stats",     args => Tools.SceneHierarchyTools.Stats(args));

            Register("console-log-read",               args => Tools.ConsoleLogTools.Read(args));
            Register("console-log-clear",              args => Tools.ConsoleLogTools.Clear(args));
            Register("console-log-subscribe-snapshot", args => Tools.ConsoleLogTools.SubscribeSnapshot(args));

            Register("editor-menu-execute",   args => Tools.EditorOpsTools.MenuExecute(args));
            Register("editor-undo",           args => Tools.EditorOpsTools.Undo(args));
            Register("editor-redo",           args => Tools.EditorOpsTools.Redo(args));
            Register("editor-undo-history",   args => Tools.EditorOpsTools.UndoHistory(args));
            Register("editor-selection-get",  args => Tools.EditorOpsTools.SelectionGet(args));
            Register("editor-selection-set",  args => Tools.EditorOpsTools.SelectionSet(args));

            Register("project-info",                  args => Tools.ProjectInfoTools.Info(args));
            Register("project-packages",              args => Tools.ProjectInfoTools.Packages(args));
            Register("project-build-settings",        args => Tools.ProjectInfoTools.BuildSettings(args));
            Register("project-layers-tags",           args => Tools.ProjectInfoTools.LayersTags(args));
            Register("project-render-pipeline-state", args => Tools.ProjectInfoTools.RenderPipelineState(args));
            Register("project-active-scene",          args => Tools.ProjectInfoTools.ActiveScene(args));
            Register("project-quality-settings",      args => Tools.ProjectInfoTools.QualitySettings(args));

            Register("animator-state",     args => Tools.AnimatorStateTool.Handle(args));
            Register("render-queue-audit", args => Tools.RenderQueueAuditTool.Handle(args));
            Register("asset-dependents",    args => Tools.AssetDependentsTool.Handle(args));
            Register("lighting-diagnostic", args => Tools.LightingDiagnosticTool.Handle(args));

            Register("physics-raycast",        args => Tools.PhysicsTools.Raycast(args));
            Register("physics-raycast-all",    args => Tools.PhysicsTools.RaycastAll(args));
            Register("physics-spherecast",     args => Tools.PhysicsTools.SphereCast(args));
            Register("physics-overlap-sphere", args => Tools.PhysicsTools.OverlapSphere(args));
            Register("physics-overlap-box",    args => Tools.PhysicsTools.OverlapBox(args));
            Register("physics-settings",       args => Tools.PhysicsTools.Settings(args));

            Register("animator-parameter-set", args => Tools.AnimatorMutationTools.ParameterSet(args));
            Register("animator-crossfade",     args => Tools.AnimatorMutationTools.CrossFade(args));
            Register("animator-play",          args => Tools.AnimatorMutationTools.Play(args));

            Start();
        }

        public static void Register(string name, Func<JToken, Task<object>> handler)
            => Handlers[name] = handler;

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
                await WriteBody(ctx, 200, json);
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
            await WriteBody(ctx, status, json);
        }

        private static async Task WriteBody(HttpListenerContext ctx, int status, string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json";
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            ctx.Response.OutputStream.Close();
        }
    }
}
