using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// Write-side project configuration for tags and layers. These are small
    /// but important no-click project hygiene operations.
    /// </summary>
    internal static class ProjectConfigWriteTools
    {
        private static readonly HashSet<string> BuiltInTags = new(StringComparer.Ordinal)
        {
            "Untagged",
            "Respawn",
            "Finish",
            "EditorOnly",
            "MainCamera",
            "Player",
            "GameController"
        };

        [ReifyTool("project-tag-add")]
        public static Task<object> TagAdd(JToken args)
        {
            var name = NormalizeTag(args?.Value<string>("name"));

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var tagManager = LoadTagManager();
                var tagsProp = tagManager.FindProperty("tags")
                    ?? throw new InvalidOperationException("TagManager.asset has no 'tags' property.");

                for (var i = 0; i < tagsProp.arraySize; i++)
                {
                    if (string.Equals(tagsProp.GetArrayElementAtIndex(i).stringValue, name, StringComparison.Ordinal))
                    {
                        return new
                        {
                            created = false,
                            already_exists = true,
                            tag = name,
                            tags = BuildTagsSnapshot(),
                            read_at_utc = DateTime.UtcNow.ToString("o"),
                            frame = (long)UnityEngine.Time.frameCount
                        };
                    }
                }

                tagsProp.InsertArrayElementAtIndex(tagsProp.arraySize);
                tagsProp.GetArrayElementAtIndex(tagsProp.arraySize - 1).stringValue = name;
                SaveTagManager(tagManager);

                return new
                {
                    created = true,
                    tag = name,
                    tags = BuildTagsSnapshot(),
                    note = "Adding a tag only changes ProjectSettings/TagManager.asset. Existing GameObjects are untouched until a tool explicitly assigns the tag.",
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame = (long)UnityEngine.Time.frameCount
                };
            });
        }

        [ReifyTool("project-tag-remove")]
        public static Task<object> TagRemove(JToken args)
        {
            var name = NormalizeTag(args?.Value<string>("name"));

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                if (BuiltInTags.Contains(name))
                    throw new InvalidOperationException($"'{name}' is a built-in Unity tag and cannot be removed.");

                var tagManager = LoadTagManager();
                var tagsProp = tagManager.FindProperty("tags")
                    ?? throw new InvalidOperationException("TagManager.asset has no 'tags' property.");

                var index = -1;
                for (var i = 0; i < tagsProp.arraySize; i++)
                {
                    if (string.Equals(tagsProp.GetArrayElementAtIndex(i).stringValue, name, StringComparison.Ordinal))
                    {
                        index = i;
                        break;
                    }
                }

                if (index < 0)
                {
                    return new
                    {
                        deleted = false,
                        already_absent = true,
                        tag = name,
                        tags = BuildTagsSnapshot(),
                        read_at_utc = DateTime.UtcNow.ToString("o"),
                        frame = (long)UnityEngine.Time.frameCount
                    };
                }

                tagsProp.DeleteArrayElementAtIndex(index);
                SaveTagManager(tagManager);

                return new
                {
                    deleted = true,
                    tag = name,
                    tags = BuildTagsSnapshot(),
                    note = "Unity does not automatically retag existing GameObjects that referenced this tag. Review any objects/scripts that depended on it.",
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame = (long)UnityEngine.Time.frameCount
                };
            });
        }

        [ReifyTool("project-layer-set")]
        public static Task<object> LayerSet(JToken args)
        {
            var index = args?.Value<int?>("index")
                ?? throw new ArgumentException("index is required.");
            var requestedName = args?["name"]?.Type == JTokenType.Null
                ? null
                : args?.Value<string>("name");
            var layerName = string.IsNullOrWhiteSpace(requestedName) ? string.Empty : requestedName.Trim();

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                if (index < 8 || index > 31)
                    throw new InvalidOperationException("Only custom layers 8-31 can be modified.");

                var tagManager = LoadTagManager();
                var layersProp = tagManager.FindProperty("layers")
                    ?? throw new InvalidOperationException("TagManager.asset has no 'layers' property.");

                if (!string.IsNullOrEmpty(layerName))
                {
                    for (var i = 0; i < layersProp.arraySize; i++)
                    {
                        if (i == index) continue;
                        var existing = layersProp.GetArrayElementAtIndex(i).stringValue;
                        if (string.Equals(existing, layerName, StringComparison.Ordinal))
                            throw new InvalidOperationException($"Layer name '{layerName}' is already assigned to index {i}.");
                    }
                }

                var element = layersProp.GetArrayElementAtIndex(index);
                var before = element.stringValue;
                element.stringValue = layerName;
                SaveTagManager(tagManager);

                return new
                {
                    index,
                    before_name = before,
                    after_name = string.IsNullOrEmpty(layerName) ? null : layerName,
                    cleared = string.IsNullOrEmpty(layerName),
                    layers = BuildLayersSnapshot(),
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame = (long)UnityEngine.Time.frameCount
                };
            });
        }

        private static string NormalizeTag(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("name is required.");
            return name.Trim();
        }

        private static SerializedObject LoadTagManager()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0 || assets[0] == null)
                throw new InvalidOperationException("Could not load ProjectSettings/TagManager.asset.");
            return new SerializedObject(assets[0]);
        }

        private static void SaveTagManager(SerializedObject tagManager)
        {
            tagManager.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
        }

        private static object[] BuildTagsSnapshot()
        {
            var tags = UnityEditorInternal.InternalEditorUtility.tags;
            var list = new object[tags.Length];
            for (var i = 0; i < tags.Length; i++)
                list[i] = new { name = tags[i] };
            return list;
        }

        private static object[] BuildLayersSnapshot()
        {
            var layers = new object[32];
            for (var i = 0; i < 32; i++)
            {
                var name = UnityEditorInternal.InternalEditorUtility.GetLayerName(i);
                layers[i] = new
                {
                    index = i,
                    name = string.IsNullOrEmpty(name) ? null : name,
                    is_builtin = i < 8,
                    is_unused = string.IsNullOrEmpty(name) && i >= 8
                };
            }
            return layers;
        }
    }
}
