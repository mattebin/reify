using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// Gap-closing tools added after comparing reify against upstreams from the
    /// perspective that matters here: "can an LLM actually build a game with
    /// this?" This batch fills the last broad missing editor-side surfaces:
    /// script execution, generic serialized-object fallback, active/unload
    /// scene control, and a few practical discovery/save utilities.
    /// </summary>
    internal static class CompletionGapTools
    {
        [ReifyTool("script-execute")]
        public static Task<object> ScriptExecute(JToken args)
        {
            var code = args?.Value<string>("code") ?? throw new ArgumentException("code is required.");
            var typeName = args?.Value<string>("type_name") ?? "ReifyScriptExecution";
            var methodName = args?.Value<string>("method_name") ?? "Run";

            return MainThreadDispatcher.RunAsync<object>(() =>
                ScriptExecutionRoslyn.Execute(code, typeName, methodName));
        }

        [ReifyTool("object-get-data")]
        public static Task<object> ObjectGetData(JToken args)
        {
            var includeProperties = args?.Value<bool?>("include_properties") ?? true;

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var target = SerializedObjectEvidence.ResolveObjectFromArgs(args);
                var properties = includeProperties
                    ? SerializedObjectEvidence.ReadAllProperties(target)
                    : Array.Empty<object>();

                return new
                {
                    target = SerializedObjectEvidence.DescribeObject(target),
                    property_count = properties.Length,
                    properties,
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }

        [ReifyTool("object-modify")]
        public static Task<object> ObjectModify(JToken args)
        {
            var properties = args?["properties"] as JObject
                ?? throw new ArgumentException("'properties' object is required.");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var target = SerializedObjectEvidence.ResolveObjectFromArgs(args);
                Undo.RecordObject(target, $"Reify: modify {target.name}");

                using var so = new SerializedObject(target);
                var applied = new List<(string Name, object Before)>();
                var failed  = new List<object>();

                foreach (var kv in properties)
                {
                    var p = so.FindProperty(kv.Key);
                    if (p == null)
                    {
                        failed.Add(new { name = kv.Key, reason = "property_not_found" });
                        continue;
                    }

                    var before = SerializedObjectEvidence.ReadSerializedValue(p);
                    try
                    {
                        SerializedPropertyWriter.Apply(p, kv.Value);
                        applied.Add((kv.Key, before));
                    }
                    catch (Exception ex)
                    {
                        failed.Add(new { name = kv.Key, reason = ex.Message });
                    }
                }

                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(target);

                var assetPath = AssetDatabase.GetAssetPath(target);
                if (!string.IsNullOrEmpty(assetPath))
                    AssetDatabase.SaveAssets();

                using var soAfter = new SerializedObject(target);
                var appliedFields = new List<object>(applied.Count);
                foreach (var entry in applied)
                {
                    var pAfter = soAfter.FindProperty(entry.Name);
                    appliedFields.Add(new
                    {
                        field  = entry.Name,
                        before = entry.Before,
                        after  = pAfter != null ? SerializedObjectEvidence.ReadSerializedValue(pAfter) : null
                    });
                }

                return new
                {
                    target = SerializedObjectEvidence.DescribeObject(target),
                    applied_fields = appliedFields.ToArray(),
                    applied_count  = appliedFields.Count,
                    failed         = failed.ToArray(),
                    read_at_utc    = DateTime.UtcNow.ToString("o"),
                    frame          = (long)Time.frameCount
                };
            });
        }

        [ReifyTool("scene-set-active")]
        public static Task<object> SceneSetActive(JToken args)
        {
            var path = args?.Value<string>("path") ?? throw new ArgumentException("path is required.");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var scene = SceneManager.GetSceneByPath(path);
                if (!scene.IsValid() || !scene.isLoaded)
                    throw new InvalidOperationException($"Loaded scene not found: {path}");

                var before = SceneManager.GetActiveScene();
                if (scene == before)
                {
                    return new
                    {
                        active_scene = new
                        {
                            name = scene.name,
                            path = scene.path,
                            build_index = scene.buildIndex,
                            is_loaded = scene.isLoaded,
                            is_dirty = scene.isDirty,
                            is_active = true
                        },
                        applied_fields = Array.Empty<object>(),
                        applied_count = 0,
                        read_at_utc = DateTime.UtcNow.ToString("o"),
                        frame = (long)Time.frameCount
                    };
                }

                if (!SceneManager.SetActiveScene(scene))
                    throw new InvalidOperationException($"SetActiveScene returned false for {path}.");

                return new
                {
                    active_scene = new
                    {
                        name = scene.name,
                        path = scene.path,
                        build_index = scene.buildIndex,
                        is_loaded = scene.isLoaded,
                        is_dirty = scene.isDirty,
                        is_active = true
                    },
                    applied_fields = new object[]
                    {
                        new
                        {
                            field = "active_scene_path",
                            before = before.path,
                            after = scene.path
                        }
                    },
                    applied_count = 1,
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }

        [ReifyTool("scene-unload")]
        public static Task<object> SceneUnload(JToken args)
        {
            var path = args?.Value<string>("path") ?? throw new ArgumentException("path is required.");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var scene = SceneManager.GetSceneByPath(path);
                if (!scene.IsValid() || !scene.isLoaded)
                    throw new InvalidOperationException($"Loaded scene not found: {path}");

                if (SceneManager.sceneCount <= 1)
                    throw new InvalidOperationException("scene-unload requires at least two opened scenes.");

                var wasActive = scene == SceneManager.GetActiveScene();
                var unloaded = new
                {
                    name = scene.name,
                    path = scene.path,
                    build_index = scene.buildIndex,
                    was_active = wasActive
                };

                var ok = EditorSceneManager.CloseScene(scene, true);
                if (!ok)
                    throw new InvalidOperationException($"CloseScene returned false for {path}.");

                var remaining = new List<object>(SceneManager.sceneCount);
                for (var i = 0; i < SceneManager.sceneCount; i++)
                {
                    var open = SceneManager.GetSceneAt(i);
                    remaining.Add(new
                    {
                        name = open.name,
                        path = open.path,
                        is_active = open == SceneManager.GetActiveScene()
                    });
                }

                return new
                {
                    unloaded,
                    remaining_open_scene_count = remaining.Count,
                    remaining_open_scenes = remaining.ToArray(),
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }

        [ReifyTool("asset-find-built-in")]
        public static Task<object> AssetFindBuiltIn(JToken args)
        {
            var name = args?.Value<string>("name");
            var type = args?.Value<string>("type");
            var includeHidden = args?.Value<bool?>("include_hidden") ?? false;
            var limit = args?.Value<int?>("limit") ?? 200;

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var matches = new List<object>();
                var seen = new HashSet<int>();
                var truncated = false;

                foreach (var obj in Resources.FindObjectsOfTypeAll<Object>())
                {
                    if (obj == null) continue;
                    if (!EditorUtility.IsPersistent(obj)) continue;
                    if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(obj))) continue;
                    if (!includeHidden && (obj.hideFlags & HideFlags.HideAndDontSave) != 0) continue;
                    if (!string.IsNullOrEmpty(name) &&
                        obj.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (!string.IsNullOrEmpty(type) &&
                        !MatchesType(obj.GetType(), type)) continue;

                    var instanceId = GameObjectResolver.InstanceIdOf(obj);
                    if (!seen.Add(instanceId)) continue;

                    if (matches.Count >= limit) { truncated = true; break; }
                    matches.Add(new
                    {
                        name = obj.name,
                        type_fqn = obj.GetType().FullName,
                        instance_id = instanceId,
                        hide_flags = obj.hideFlags.ToString(),
                        source = "builtin_or_editor_resource"
                    });
                }

                return new
                {
                    match_count = matches.Count,
                    truncated,
                    matches = matches.ToArray(),
                    warnings = new[]
                    {
                        "Built-in asset search is best-effort and limited to built-in/editor resources currently loaded by Unity."
                    },
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }

        [ReifyTool("asset-shader-list-all")]
        public static Task<object> AssetShaderListAll(JToken args)
        {
            var includeHidden = args?.Value<bool?>("include_hidden") ?? true;
            var limit = args?.Value<int?>("limit") ?? 1000;

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var shaders = new List<object>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var truncated = false;

                foreach (var guid in AssetDatabase.FindAssets("t:Shader"))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                    if (shader == null) continue;
                    if (!includeHidden && shader.name.StartsWith("Hidden/", StringComparison.Ordinal)) continue;

                    var key = "asset:" + path;
                    if (!seen.Add(key)) continue;
                    if (shaders.Count >= limit) { truncated = true; break; }

                    shaders.Add(new
                    {
                        name = shader.name,
                        asset_path = path,
                        guid,
                        instance_id = GameObjectResolver.InstanceIdOf(shader),
                        is_hidden = shader.name.StartsWith("Hidden/", StringComparison.Ordinal),
                        is_supported = shader.isSupported,
                        source = "asset"
                    });
                }

                if (!truncated)
                {
                    foreach (var shader in Resources.FindObjectsOfTypeAll<Shader>())
                    {
                        if (shader == null) continue;
                        if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(shader))) continue;
                        if (!includeHidden && shader.name.StartsWith("Hidden/", StringComparison.Ordinal)) continue;

                        var key = "builtin:" + shader.name;
                        if (!seen.Add(key)) continue;
                        if (shaders.Count >= limit) { truncated = true; break; }

                        shaders.Add(new
                        {
                            name = shader.name,
                            asset_path = (string)null,
                            guid = (string)null,
                            instance_id = GameObjectResolver.InstanceIdOf(shader),
                            is_hidden = shader.name.StartsWith("Hidden/", StringComparison.Ordinal),
                            is_supported = shader.isSupported,
                            source = "builtin_or_loaded"
                        });
                    }
                }

                return new
                {
                    shader_count = shaders.Count,
                    truncated,
                    shaders = shaders.ToArray(),
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }

        [ReifyTool("prefab-save")]
        public static Task<object> PrefabSave(JToken _)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var stage = PrefabStageUtility.GetCurrentPrefabStage();
                if (stage == null)
                    throw new InvalidOperationException("prefab-save requires Unity to be in Prefab Mode.");

                var beforeDirty = stage.scene.isDirty;
                var saved = PrefabUtility.SaveAsPrefabAsset(stage.prefabContentsRoot, stage.assetPath);
                if (saved == null)
                    throw new InvalidOperationException($"SaveAsPrefabAsset returned null for {stage.assetPath}. Check Unity Console.");

                AssetDatabase.SaveAssets();
                var prov = AssetProvenance.Summarize(stage.assetPath);
                var guid = AssetDatabase.AssetPathToGUID(stage.assetPath);

                return new
                {
                    prefab = new
                    {
                        asset_path = stage.assetPath,
                        guid = guid,
                        root_name = saved.name
                    },
                    applied_fields = new object[]
                    {
                        new
                        {
                            field = "prefab_scene_dirty",
                            before = beforeDirty,
                            after = stage.scene.isDirty
                        }
                    },
                    applied_count = 1,
                    prefab_provenance = prov,
                    guids_touched = new[] { guid },
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }

        [ReifyTool("component-list-all")]
        public static Task<object> ComponentListAll(JToken args)
        {
            var nameLike = args?.Value<string>("name_like");
            var namespaceLike = args?.Value<string>("namespace_like");
            var includeEditorOnly = args?.Value<bool?>("include_editor_only") ?? true;
            var limit = args?.Value<int?>("limit") ?? 1000;

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var matches = new List<object>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var truncated = false;

                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type[] types;
                    try { types = asm.GetTypes(); }
                    catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }
                    catch { continue; }

                    foreach (var type in types)
                    {
                        if (type == null) continue;
                        if (!typeof(Component).IsAssignableFrom(type)) continue;
                        if (!type.IsClass || type.IsAbstract || type.ContainsGenericParameters) continue;
                        if (!includeEditorOnly &&
                            (type.Namespace?.StartsWith("UnityEditor", StringComparison.Ordinal) ?? false)) continue;
                        if (!string.IsNullOrEmpty(nameLike) &&
                            type.Name.IndexOf(nameLike, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        if (!string.IsNullOrEmpty(namespaceLike) &&
                            (type.Namespace?.IndexOf(namespaceLike, StringComparison.OrdinalIgnoreCase) ?? -1) < 0) continue;

                        var key = type.FullName;
                        if (!seen.Add(key)) continue;
                        if (matches.Count >= limit) { truncated = true; break; }

                        matches.Add(new
                        {
                            type_fqn = type.FullName,
                            short_name = type.Name,
                            @namespace = type.Namespace,
                            assembly_name = type.Assembly.GetName().Name
                        });
                    }

                    if (truncated) break;
                }

                matches.Sort((a, b) =>
                    string.Compare(
                        (string)a.GetType().GetProperty("type_fqn")?.GetValue(a),
                        (string)b.GetType().GetProperty("type_fqn")?.GetValue(b),
                        StringComparison.Ordinal));

                return new
                {
                    component_type_count = matches.Count,
                    truncated,
                    component_types = matches.ToArray(),
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }

        private static bool MatchesType(Type actualType, string filter)
        {
            return string.Equals(actualType.Name, filter, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(actualType.FullName, filter, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal static class SerializedObjectEvidence
    {
        public static Object ResolveObjectFromArgs(JToken args)
        {
            var instanceId = ReadIntArg(args, "instance_id") ?? ReadIntArg(args, "object_instance_id");
            if (instanceId.HasValue)
            {
                var obj = GameObjectResolver.ByInstanceId(instanceId.Value);
                if (obj == null)
                    throw new InvalidOperationException($"No object with instance_id {instanceId}.");
                return obj;
            }

            var assetPath = args?.Value<string>("asset_path");
            if (!string.IsNullOrWhiteSpace(assetPath))
            {
                var asset = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
                if (asset == null)
                    throw new InvalidOperationException($"Asset not found: {assetPath}");
                return asset;
            }

            var gameObjectPath = ComponentLookup.ReadGameObjectPathArg(args);
            var componentType = ComponentLookup.ReadComponentTypeArg(args);
            if (!string.IsNullOrWhiteSpace(gameObjectPath) && !string.IsNullOrWhiteSpace(componentType))
                return ComponentLookup.Resolve(null, gameObjectPath, componentType);

            if (!string.IsNullOrWhiteSpace(gameObjectPath))
            {
                var go = GameObjectResolver.ByPath(gameObjectPath);
                if (go == null)
                    throw new InvalidOperationException($"GameObject not found: {gameObjectPath}");
                return go;
            }

            throw new ArgumentException("Provide one of instance_id, asset_path, gameobject_path, or gameobject_path + component_type.");
        }

        public static object DescribeObject(Object obj)
        {
            string assetPath = null;
            string gameobjectPath = null;
            string qualifiedPath = null;
            string sceneName = null;
            string scenePath = null;

            if (obj != null)
                assetPath = AssetDatabase.GetAssetPath(obj);

            if (obj is GameObject go)
            {
                gameobjectPath = GameObjectResolver.PathOf(go);
                qualifiedPath = GameObjectResolver.QualifiedPathOf(go);
                sceneName = go.scene.name;
                scenePath = go.scene.path;
            }
            else if (obj is Component component)
            {
                gameobjectPath = GameObjectResolver.PathOf(component.gameObject);
                qualifiedPath = GameObjectResolver.QualifiedPathOf(component.gameObject);
                sceneName = component.gameObject.scene.name;
                scenePath = component.gameObject.scene.path;
            }

            return new
            {
                type_fqn = obj?.GetType().FullName,
                instance_id = obj != null ? GameObjectResolver.InstanceIdOf(obj) : 0,
                name = obj != null ? obj.name : null,
                asset_path = string.IsNullOrEmpty(assetPath) ? null : assetPath,
                gameobject_path = gameobjectPath,
                qualified_path = qualifiedPath,
                scene_name = sceneName,
                scene_path = scenePath,
                is_persistent = obj != null && EditorUtility.IsPersistent(obj)
            };
        }

        public static object[] ReadAllProperties(Object obj)
        {
            var props = new List<object>();
            using var so = new SerializedObject(obj);
            var it = so.GetIterator();
            if (it.NextVisible(true))
            {
                do
                {
                    props.Add(new
                    {
                        name = it.name,
                        type = it.propertyType.ToString(),
                        value = ReadSerializedValue(it)
                    });
                } while (it.NextVisible(false));
            }
            return props.ToArray();
        }

        public static object ReadSerializedValue(SerializedProperty p)
        {
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer:    return p.intValue;
                case SerializedPropertyType.Boolean:    return p.boolValue;
                case SerializedPropertyType.Float:      return p.floatValue;
                case SerializedPropertyType.String:     return p.stringValue;
                case SerializedPropertyType.Color:      return new { r = p.colorValue.r, g = p.colorValue.g, b = p.colorValue.b, a = p.colorValue.a };
                case SerializedPropertyType.ObjectReference:
                    return p.objectReferenceValue == null ? null : new
                    {
                        type_fqn = p.objectReferenceValue.GetType().FullName,
                        instance_id = GameObjectResolver.InstanceIdOf(p.objectReferenceValue),
                        name = p.objectReferenceValue.name
                    };
                case SerializedPropertyType.LayerMask:  return p.intValue;
                case SerializedPropertyType.Enum:       return p.enumValueIndex;
                case SerializedPropertyType.Vector2:    return new { x = p.vector2Value.x, y = p.vector2Value.y };
                case SerializedPropertyType.Vector3:    return new { x = p.vector3Value.x, y = p.vector3Value.y, z = p.vector3Value.z };
                case SerializedPropertyType.Vector4:    return new { x = p.vector4Value.x, y = p.vector4Value.y, z = p.vector4Value.z, w = p.vector4Value.w };
                case SerializedPropertyType.Quaternion: return new { x = p.quaternionValue.x, y = p.quaternionValue.y, z = p.quaternionValue.z, w = p.quaternionValue.w };
                case SerializedPropertyType.Bounds:
                    return new
                    {
                        center = new { x = p.boundsValue.center.x, y = p.boundsValue.center.y, z = p.boundsValue.center.z },
                        size = new { x = p.boundsValue.size.x, y = p.boundsValue.size.y, z = p.boundsValue.size.z }
                    };
                case SerializedPropertyType.Rect:
                    return new
                    {
                        x = p.rectValue.x,
                        y = p.rectValue.y,
                        width = p.rectValue.width,
                        height = p.rectValue.height
                    };
                case SerializedPropertyType.Vector2Int:
                    return new { x = p.vector2IntValue.x, y = p.vector2IntValue.y };
                case SerializedPropertyType.Vector3Int:
                    return new { x = p.vector3IntValue.x, y = p.vector3IntValue.y, z = p.vector3IntValue.z };
                default:
                    return $"<{p.propertyType}>";
            }
        }

        private static int? ReadIntArg(JToken args, string name)
        {
            var token = args?[name];
            return token != null && token.Type == JTokenType.Integer ? token.Value<int>() : null;
        }
    }

    internal static class ScriptExecutionRoslyn
    {
        public static object Execute(string code, string typeName, string methodName)
        {
            var session = RoslynExecutionSession.Create();
            return session.Execute(code, typeName, methodName);
        }

        private sealed class RoslynExecutionSession
        {
            private readonly Type _csharpSyntaxTreeType;
            private readonly Type _compilationType;
            private readonly Type _compilationOptionsType;
            private readonly Type _metadataReferenceType;
            private readonly Type _outputKindType;

            private RoslynExecutionSession(
                Type csharpSyntaxTreeType,
                Type compilationType,
                Type compilationOptionsType,
                Type metadataReferenceType,
                Type outputKindType)
            {
                _csharpSyntaxTreeType = csharpSyntaxTreeType;
                _compilationType = compilationType;
                _compilationOptionsType = compilationOptionsType;
                _metadataReferenceType = metadataReferenceType;
                _outputKindType = outputKindType;
            }

            public static RoslynExecutionSession Create()
            {
                EnsureRoslynLoaded();

                var csharpSyntaxTreeType = FindType(
                    "Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree, Microsoft.CodeAnalysis.CSharp",
                    "Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree");
                var compilationType = FindType(
                    "Microsoft.CodeAnalysis.CSharp.CSharpCompilation, Microsoft.CodeAnalysis.CSharp",
                    "Microsoft.CodeAnalysis.CSharp.CSharpCompilation");
                var compilationOptionsType = FindType(
                    "Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions, Microsoft.CodeAnalysis.CSharp",
                    "Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions");
                var metadataReferenceType = FindType(
                    "Microsoft.CodeAnalysis.MetadataReference, Microsoft.CodeAnalysis",
                    "Microsoft.CodeAnalysis.MetadataReference");
                var outputKindType = FindType(
                    "Microsoft.CodeAnalysis.OutputKind, Microsoft.CodeAnalysis",
                    "Microsoft.CodeAnalysis.OutputKind");

                if (csharpSyntaxTreeType == null || compilationType == null ||
                    compilationOptionsType == null || metadataReferenceType == null ||
                    outputKindType == null)
                {
                    throw new InvalidOperationException(
                        "Roslyn execution requires Microsoft.CodeAnalysis + Microsoft.CodeAnalysis.CSharp in the Unity editor.");
                }

                return new RoslynExecutionSession(
                    csharpSyntaxTreeType,
                    compilationType,
                    compilationOptionsType,
                    metadataReferenceType,
                    outputKindType);
            }

            public object Execute(string code, string typeName, string methodName)
            {
                var sourcePath = "reify://script-execute/input.cs";
                var syntaxTree = ParseText(code, sourcePath);
                var references = BuildMetadataReferences();
                var compilation = CreateCompilation(syntaxTree, references);

                var diagnostics = ReadDiagnostics(GetCompilationDiagnostics(compilation), sourcePath, code);
                var hasErrors = diagnostics.Any(d =>
                    string.Equals(GetMemberValue(d, "severity")?.ToString(), "error", StringComparison.OrdinalIgnoreCase));

                if (hasErrors)
                {
                    return new
                    {
                        executed = false,
                        entrypoint = new { type_name = typeName, method_name = methodName },
                        compile_succeeded = false,
                        compile_diagnostics = diagnostics.ToArray(),
                        diagnostic_count = diagnostics.Count,
                        read_at_utc = DateTime.UtcNow.ToString("o"),
                        frame       = (long)Time.frameCount
                    };
                }

                using var stream = new MemoryStream();
                var emitResult = EmitCompilation(compilation, stream);
                var emitDiagnostics = ReadDiagnostics(GetMemberValue(emitResult, "Diagnostics") as IEnumerable, sourcePath, code);
                if (!Convert.ToBoolean(GetMemberValue(emitResult, "Success") ?? false))
                {
                    return new
                    {
                        executed = false,
                        entrypoint = new { type_name = typeName, method_name = methodName },
                        compile_succeeded = false,
                        compile_diagnostics = emitDiagnostics.ToArray(),
                        diagnostic_count = emitDiagnostics.Count,
                        read_at_utc = DateTime.UtcNow.ToString("o"),
                        frame       = (long)Time.frameCount
                    };
                }

                var assembly = System.Reflection.Assembly.Load(stream.ToArray());
                var targetType = ResolveExecutionType(assembly, typeName)
                    ?? throw new InvalidOperationException($"Compiled assembly does not contain type '{typeName}'.");
                var method = targetType.GetMethod(
                    methodName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (method == null)
                    throw new InvalidOperationException($"Compiled type '{targetType.FullName}' does not contain static method '{methodName}'.");
                if (method.GetParameters().Length != 0)
                    throw new InvalidOperationException("script-execute currently supports only parameterless static methods.");

                object returnValue;
                try
                {
                    returnValue = method.Invoke(null, null);
                }
                catch (TargetInvocationException ex)
                {
                    throw new InvalidOperationException($"Executed script threw: {ex.InnerException?.Message ?? ex.Message}");
                }

                return new
                {
                    executed = true,
                    entrypoint = new
                    {
                        type_name = targetType.FullName,
                        method_name = method.Name
                    },
                    compile_succeeded = true,
                    compile_diagnostics = emitDiagnostics.ToArray(),
                    diagnostic_count = emitDiagnostics.Count,
                    return_type_fqn = method.ReturnType.FullName,
                    return_value = NormalizeReturnValue(returnValue),
                    return_is_null = returnValue == null,
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            }

            private object ParseText(string code, string sourcePath)
            {
                var parseText = _csharpSyntaxTreeType
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m =>
                    {
                        if (m.Name != "ParseText") return false;
                        var ps = m.GetParameters();
                        return ps.Length > 0 && ps[0].ParameterType == typeof(string);
                    });

                if (parseText == null)
                    throw new InvalidOperationException("Roslyn ParseText entrypoint not found.");

                return parseText.Invoke(null, BindArguments(parseText.GetParameters(), code, sourcePath));
            }

            private IList BuildMetadataReferences()
            {
                var refs = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(_metadataReferenceType));
                var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var asm in CompilationPipeline.GetAssemblies())
                    foreach (var path in asm.compiledAssemblyReferences ?? Array.Empty<string>())
                        AddReferencePath(paths, path);

                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        if (asm.IsDynamic) continue;
                        AddReferencePath(paths, asm.Location);
                    }
                    catch { }
                }

                foreach (var path in paths)
                    refs.Add(CreateMetadataReference(path));
                return refs;
            }

            private object CreateCompilation(object syntaxTree, IList references)
            {
                var syntaxTreeType = FindType(
                    "Microsoft.CodeAnalysis.SyntaxTree, Microsoft.CodeAnalysis",
                    "Microsoft.CodeAnalysis.SyntaxTree")
                    ?? throw new InvalidOperationException("Roslyn SyntaxTree type not found.");

                var syntaxTrees = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(syntaxTreeType));
                syntaxTrees.Add(syntaxTree);

                var options = CreateCompilationOptions();

                var create = _compilationType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m =>
                    {
                        if (m.Name != "Create") return false;
                        var ps = m.GetParameters();
                        return ps.Length >= 3 && ps[0].ParameterType == typeof(string);
                    });
                if (create == null)
                    throw new InvalidOperationException("Roslyn compilation factory not found.");

                return create.Invoke(
                    null,
                    BindArguments(create.GetParameters(),
                        "Reify.ScriptExecute." + Guid.NewGuid().ToString("N"),
                        syntaxTrees,
                        references,
                        options));
            }

            private object CreateCompilationOptions()
            {
                var outputKind = Enum.Parse(_outputKindType, "DynamicallyLinkedLibrary");
                foreach (var ctor in _compilationOptionsType.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                             .OrderBy(c => c.GetParameters().Length))
                {
                    try
                    {
                        return ctor.Invoke(BindArguments(ctor.GetParameters(), outputKind));
                    }
                    catch { }
                }
                throw new InvalidOperationException("Roslyn compilation options could not be constructed.");
            }

            private IEnumerable GetCompilationDiagnostics(object compilation)
                => GetMemberValue(InvokeMethod(compilation, "GetDiagnostics"), "Length") != null
                    ? (IEnumerable)InvokeMethod(compilation, "GetDiagnostics")
                    : Array.Empty<object>();

            private object EmitCompilation(object compilation, Stream stream)
            {
                var emit = compilation.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m =>
                    {
                        if (m.Name != "Emit") return false;
                        var ps = m.GetParameters();
                        return ps.Length > 0 && typeof(Stream).IsAssignableFrom(ps[0].ParameterType);
                    });
                if (emit == null)
                    throw new InvalidOperationException("Roslyn Emit(Stream, ...) overload not found.");

                return emit.Invoke(compilation, BindArguments(emit.GetParameters(), stream));
            }

            private object CreateMetadataReference(string path)
            {
                var createFromFile = _metadataReferenceType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m =>
                    {
                        if (m.Name != "CreateFromFile") return false;
                        var ps = m.GetParameters();
                        return ps.Length > 0 && ps[0].ParameterType == typeof(string);
                    });
                if (createFromFile == null)
                    throw new InvalidOperationException("Roslyn MetadataReference.CreateFromFile not found.");

                return createFromFile.Invoke(null, BindArguments(createFromFile.GetParameters(), path));
            }

            private static Type ResolveExecutionType(System.Reflection.Assembly assembly, string typeName)
            {
                var type = assembly.GetType(typeName, false);
                if (type != null) return type;

                var matches = assembly.GetTypes()
                    .Where(t => string.Equals(t.Name, typeName, StringComparison.Ordinal))
                    .ToArray();
                return matches.Length == 1 ? matches[0] : null;
            }

            private static object NormalizeReturnValue(object value)
            {
                if (value == null) return null;

                switch (value)
                {
                    case string s: return s;
                    case bool b: return b;
                    case byte or sbyte or short or ushort or int or uint or long or ulong:
                    case float or double or decimal:
                        return value;
                    case Enum e:
                        return e.ToString();
                    case Vector2 v2:
                        return new { x = v2.x, y = v2.y };
                    case Vector3 v3:
                        return new { x = v3.x, y = v3.y, z = v3.z };
                    case Vector4 v4:
                        return new { x = v4.x, y = v4.y, z = v4.z, w = v4.w };
                    case Quaternion q:
                        return new { x = q.x, y = q.y, z = q.z, w = q.w };
                    case Color c:
                        return new { r = c.r, g = c.g, b = c.b, a = c.a };
                    case Object obj:
                        return SerializedObjectEvidence.DescribeObject(obj);
                    case IEnumerable enumerable:
                    {
                        var list = new List<object>();
                        foreach (var item in enumerable)
                        {
                            if (list.Count >= 50) break;
                            list.Add(NormalizeReturnValue(item));
                        }
                        return list.ToArray();
                    }
                    default:
                        return value.ToString();
                }
            }

            private static List<object> ReadDiagnostics(IEnumerable diagnostics, string sourcePath, string code)
            {
                var lines = code.Replace("\r\n", "\n").Split('\n');
                var list = new List<object>();
                if (diagnostics == null) return list;

                foreach (var diagnostic in diagnostics)
                {
                    if (diagnostic == null) continue;

                    var location = GetMemberValue(diagnostic, "Location");
                    var lineSpan = location != null ? InvokeMethod(location, "GetLineSpan") : null;
                    var start = lineSpan != null ? GetMemberValue(lineSpan, "StartLinePosition") : null;
                    var end = lineSpan != null ? GetMemberValue(lineSpan, "EndLinePosition") : null;
                    var path = lineSpan != null ? GetMemberValue(lineSpan, "Path") as string : sourcePath;

                    var line = ReadLinePosition(start, "Line");
                    var column = ReadLinePosition(start, "Character");
                    var endLine = ReadLinePosition(end, "Line");
                    var endColumn = ReadLinePosition(end, "Character");

                    var oneBasedLine = line.HasValue ? line.Value + 1 : (int?)null;
                    list.Add(new
                    {
                        severity = (GetMemberValue(diagnostic, "Severity")?.ToString() ?? "info").ToLowerInvariant(),
                        id = GetMemberValue(diagnostic, "Id")?.ToString(),
                        message = InvokeMethod(diagnostic, "GetMessage") as string ?? diagnostic.ToString(),
                        asset_path = path,
                        line = oneBasedLine,
                        column = column.HasValue ? column.Value + 1 : (int?)null,
                        end_line = endLine.HasValue ? endLine.Value + 1 : (int?)null,
                        end_column = endColumn.HasValue ? endColumn.Value + 1 : (int?)null,
                        source_snippet = oneBasedLine.HasValue && oneBasedLine.Value - 1 < lines.Length
                            ? lines[oneBasedLine.Value - 1]
                            : null
                    });
                }

                return list;
            }

            private static int? ReadLinePosition(object linePosition, string propertyName)
            {
                if (linePosition == null) return null;
                var value = GetMemberValue(linePosition, propertyName);
                if (value == null) return null;
                try { return Convert.ToInt32(value); }
                catch { return null; }
            }

            private static object InvokeMethod(object instance, string methodName)
            {
                if (instance == null) return null;
                var methods = instance.GetType()
                    .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .Where(m => m.Name == methodName)
                    .OrderBy(m => m.GetParameters().Length)
                    .ToArray();
                foreach (var method in methods)
                {
                    try { return method.Invoke(instance, BindArguments(method.GetParameters())); }
                    catch { }
                }
                return null;
            }

            private static object GetMemberValue(object instance, string memberName)
            {
                if (instance == null) return null;
                var type = instance.GetType();
                var property = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                if (property != null) return property.GetValue(instance);
                var field = type.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                if (field != null) return field.GetValue(instance);
                return null;
            }

            private static object[] BindArguments(ParameterInfo[] parameters, params object[] preferred)
            {
                var args = new object[parameters.Length];
                for (var i = 0; i < parameters.Length; i++)
                {
                    if (i < preferred.Length && preferred[i] != null && parameters[i].ParameterType.IsInstanceOfType(preferred[i]))
                    {
                        args[i] = preferred[i];
                        continue;
                    }

                    if (i < preferred.Length && preferred[i] == null)
                    {
                        args[i] = null;
                        continue;
                    }

                    var parameter = parameters[i];
                    if (parameter.Name == "path" && preferred.Length > 1 && preferred[1] is string path)
                    {
                        args[i] = path;
                        continue;
                    }

                    if (parameter.ParameterType == typeof(Encoding))
                    {
                        args[i] = Encoding.UTF8;
                        continue;
                    }

                    if (parameter.HasDefaultValue)
                    {
                        args[i] = parameter.DefaultValue;
                        continue;
                    }

                    if (parameter.ParameterType.IsValueType)
                    {
                        args[i] = Activator.CreateInstance(parameter.ParameterType);
                        continue;
                    }

                    args[i] = null;
                }
                return args;
            }

            private static void EnsureRoslynLoaded()
            {
                var editorDataPath = EditorApplication.applicationContentsPath;
                if (string.IsNullOrWhiteSpace(editorDataPath) || !Directory.Exists(editorDataPath))
                    return;

                var probeDirs = new[]
                {
                    Path.Combine(editorDataPath, "MonoBleedingEdge", "lib", "mono", "msbuild", "Current", "bin", "Roslyn"),
                    Path.Combine(editorDataPath, "MonoBleedingEdge", "lib", "mono", "4.5"),
                    Path.Combine(editorDataPath, "Tools", "BuildPipeline", "Compilation", "ApiUpdater")
                };

                var dlls = new[]
                {
                    "System.Collections.Immutable.dll",
                    "System.Memory.dll",
                    "System.Buffers.dll",
                    "System.Runtime.CompilerServices.Unsafe.dll",
                    "System.Reflection.Metadata.dll",
                    "System.Threading.Tasks.Extensions.dll",
                    "Microsoft.CodeAnalysis.dll",
                    "Microsoft.CodeAnalysis.CSharp.dll"
                };

                foreach (var probeDir in probeDirs)
                {
                    if (!Directory.Exists(probeDir)) continue;
                    foreach (var dll in dlls)
                    {
                        var path = Path.Combine(probeDir, dll);
                        if (!File.Exists(path)) continue;

                        var simpleName = Path.GetFileNameWithoutExtension(path);
                        if (AppDomain.CurrentDomain.GetAssemblies().Any(a =>
                        {
                            try { return string.Equals(a.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase); }
                            catch { return false; }
                        }))
                        {
                            continue;
                        }

                        try { System.Reflection.Assembly.LoadFrom(path); }
                        catch { }
                    }
                }
            }

            private static void AddReferencePath(HashSet<string> paths, string path)
            {
                if (string.IsNullOrWhiteSpace(path)) return;
                if (!File.Exists(path)) return;
                paths.Add(Path.GetFullPath(path));
            }

            private static Type FindType(params string[] candidates)
            {
                foreach (var candidate in candidates)
                {
                    var type = Type.GetType(candidate, throwOnError: false);
                    if (type != null) return type;
                }

                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    foreach (var candidate in candidates)
                    {
                        var type = asm.GetType(candidate, false);
                        if (type != null) return type;
                    }
                }

                return null;
            }
        }
    }
}
