using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Reify.Shared.Contracts;

namespace Reify.Server.Tools;

[McpServerToolType]
public static class GameObjectTools
{
    [McpServerTool(Name = "gameobject-create"), Description(
        "Create a GameObject in the active scene. Pass primitive='Cube'/'Sphere'/" +
        "'Capsule'/'Cylinder'/'Plane'/'Quad' for a primitive mesh, or omit " +
        "primitive for an empty GameObject. Optional parent_path, local " +
        "position, rotation (euler degrees), and scale. The created object is " +
        "registered with Unity's Undo system (Ctrl+Z reverses it). Returns " +
        "the full GameObject DTO including instance_id, scene path, and " +
        "components."
    )]
    public static async Task<JsonElement> GameObjectCreate(
        UnityClient unity,
        [Description("Name for the new GameObject. Defaults to 'GameObject'.")]
        string? name,
        [Description("Primitive type: Cube, Sphere, Capsule, Cylinder, Plane, Quad. Omit for empty.")]
        string? primitive,
        [Description("Optional scene path for the parent; omit to create at scene root.")]
        string? parent_path,
        [Description("Local position {x,y,z}. Default (0,0,0).")]
        Vec3Arg? position,
        [Description("Local rotation as Euler degrees {x,y,z}. Default (0,0,0).")]
        Vec3Arg? rotation_euler,
        [Description("Local scale {x,y,z}. Default (1,1,1).")]
        Vec3Arg? scale,
        CancellationToken ct
    ) => await unity.CallAsync<JsonElement>(
        "gameobject-create",
        new GameObjectCreateArgs(name, primitive, parent_path, position, rotation_euler, scale),
        ct);

    [McpServerTool(Name = "gameobject-find"), Description(
        "Find GameObjects in the loaded scenes. Provide exactly one of: name " +
        "(walks all scenes, matches inactive objects too), tag (Unity tag " +
        "lookup), path (returns at most one), or instance_id. Returns a list " +
        "of GameObject DTOs (without component detail — use component-get for " +
        "that). Always returns match_count so empty results are unambiguous."
    )]
    public static async Task<JsonElement> GameObjectFind(
        UnityClient unity,
        [Description("Exact name match (walks every loaded scene, including inactive).")]
        string? name,
        [Description("Unity tag (must be defined in Tags & Layers).")]
        string? tag,
        [Description("Scene path like 'Root/Child/Leaf' or '/Root/Child/Leaf'.")]
        string? path,
        [Description("Unity instance id for precise lookup.")]
        int? instance_id,
        CancellationToken ct
    ) => await unity.CallAsync<JsonElement>(
        "gameobject-find",
        new GameObjectFindArgs(name, tag, path, instance_id),
        ct);

    [McpServerTool(Name = "gameobject-destroy"), Description(
        "Destroy a GameObject via Unity's Undo system (reversible with Ctrl+Z). " +
        "Provide path or instance_id. Returns the destroyed object's " +
        "identifying snapshot (instance_id + name + path) so the caller has a " +
        "record."
    )]
    public static async Task<JsonElement> GameObjectDestroy(
        UnityClient unity,
        [Description("Scene path of the GameObject to destroy.")]
        string? path,
        [Description("Instance id of the GameObject to destroy.")]
        int? instance_id,
        CancellationToken ct
    ) => await unity.CallAsync<JsonElement>(
        "gameobject-destroy",
        new GameObjectDestroyArgs(path, instance_id),
        ct);

    [McpServerTool(Name = "gameobject-modify"), Description(
        "Modify an existing GameObject: rename, reparent, toggle active, " +
        "change layer, or change tag. Pass an empty string for reparent_to " +
        "to unparent (make root). All changes go through Undo. Returns the " +
        "post-modification GameObject DTO."
    )]
    public static async Task<JsonElement> GameObjectModify(
        UnityClient unity,
        [Description("Scene path of the GameObject to modify.")]
        string path,
        [Description("New name. Omit to keep the current name.")]
        string? new_name,
        [Description("Scene path of new parent; empty string for scene root; omit to keep parent.")]
        string? reparent_to,
        [Description("Set active state. Omit to keep current.")]
        bool? active,
        [Description("Layer index (0–31). Omit to keep current.")]
        int? layer,
        [Description("Tag string (must be defined). Omit to keep current.")]
        string? tag,
        CancellationToken ct
    ) => await unity.CallAsync<JsonElement>(
        "gameobject-modify",
        new GameObjectModifyArgs(path, new_name, reparent_to, active, layer, tag),
        ct);

    [McpServerTool(Name = "component-add"), Description(
        "Add a Component to a GameObject by type name. type_name accepts the " +
        "short form ('BoxCollider'), full FQN ('UnityEngine.BoxCollider'), or " +
        "assembly-qualified. The add goes through Undo. Returns the added " +
        "component's type_fqn + instance_id and the updated GameObject DTO " +
        "with its full component list."
    )]
    public static async Task<JsonElement> ComponentAdd(
        UnityClient unity,
        [Description("Scene path of the target GameObject.")]
        string path,
        [Description("Component type. Short ('Rigidbody'), FQN ('UnityEngine.Rigidbody'), or assembly-qualified.")]
        string type_name,
        CancellationToken ct
    ) => await unity.CallAsync<JsonElement>(
        "component-add",
        new ComponentAddArgs(path, type_name),
        ct);

    [McpServerTool(Name = "component-modify"), Description(
        "Batch-update fields on an existing Component. Resolve the target by " +
        "instance_id (preferred) or gameobject_path + component_type. The " +
        "'properties' dict maps SerializedProperty names (e.g. 'm_Mass', " +
        "'m_IsKinematic', 'm_Center') to new values — primitive JSON values " +
        "for scalars, objects like {x,y,z} for vectors, {r,g,b,a} for colors, " +
        "and {instance_id} or {asset_path} for ObjectReferences. Undo-backed. " +
        "Returns an 'applied' list and a 'failed' list; a failed entry has " +
        "{name, reason} so the caller knows which keys didn't land."
    )]
    public static async Task<JsonElement> ComponentModify(
        UnityClient unity,
        [Description("Component instance id (preferred).")]
        int? instance_id,
        [Description("GameObject scene path (with component_type if no instance_id).")]
        string? gameobject_path,
        [Description("Short or full component type name (with gameobject_path if no instance_id).")]
        string? component_type,
        [Description("Dict of SerializedProperty name → value. Vectors as {x,y,z}; colors as {r,g,b,a}; refs as {instance_id} or {asset_path}.")]
        Dictionary<string, JsonElement> properties,
        CancellationToken ct
    ) => await unity.CallAsync<JsonElement>(
        "component-modify",
        new ComponentModifyArgs(instance_id, gameobject_path, component_type, properties),
        ct);

    [McpServerTool(Name = "component-remove"), Description(
        "Remove a Component from its GameObject. Resolve by instance_id or " +
        "(gameobject_path + component_type). Goes through Undo — reversible " +
        "via Ctrl+Z. Rejects removing Transform (Unity forbids it). Returns " +
        "the removed component's identifying snapshot."
    )]
    public static async Task<JsonElement> ComponentRemove(
        UnityClient unity,
        [Description("Component instance id (preferred).")]
        int? instance_id,
        [Description("GameObject path when resolving via (path + type).")]
        string? gameobject_path,
        [Description("Component type name when resolving via (path + type).")]
        string? component_type,
        CancellationToken ct
    ) => await unity.CallAsync<JsonElement>(
        "component-remove",
        new ComponentRemoveArgs(instance_id, gameobject_path, component_type),
        ct);

    [McpServerTool(Name = "component-set-property"), Description(
        "Set one SerializedProperty on a Component, with before/after read-back " +
        "for verification. Supports nested paths that SerializedObject already " +
        "understands: 'm_LocalPosition.x', 'm_Materials.Array.size', " +
        "'m_Center.y'. Returns {before, after, type} so the caller has proof " +
        "of the mutation — per ADR-001 §5 writes verify by reading back."
    )]
    public static async Task<JsonElement> ComponentSetProperty(
        UnityClient unity,
        [Description("Component instance id (preferred).")]
        int? instance_id,
        [Description("GameObject path when resolving via (path + type).")]
        string? gameobject_path,
        [Description("Component type name when resolving via (path + type).")]
        string? component_type,
        [Description("SerializedProperty path. e.g. 'm_Mass', 'm_LocalScale.x', 'm_Center'.")]
        string property_path,
        [Description("New value. Scalars as primitives; vectors {x,y,z}; colors {r,g,b,a}; refs {instance_id} or {asset_path}.")]
        JsonElement value,
        CancellationToken ct
    ) => await unity.CallAsync<JsonElement>(
        "component-set-property",
        new ComponentSetPropertyArgs(instance_id, gameobject_path, component_type, property_path, value),
        ct);

    [McpServerTool(Name = "component-get"), Description(
        "Inspect components on a GameObject, or a specific Component by " +
        "instance_id. Pass path (GameObject) or instance_id (either GameObject " +
        "or Component). Set include_properties=true to read every serialized " +
        "property via Unity's SerializedObject (name, type, value). Use this " +
        "instead of screenshotting the Inspector — structured state over pixels."
    )]
    public static async Task<JsonElement> ComponentGet(
        UnityClient unity,
        [Description("Scene path of a GameObject.")]
        string? path,
        [Description("Instance id of a GameObject or Component.")]
        int? instance_id,
        [Description("If true, read each component's serialized properties. Default false.")]
        bool? include_properties,
        CancellationToken ct
    ) => await unity.CallAsync<JsonElement>(
        "component-get",
        new ComponentGetArgs(path, instance_id, include_properties),
        ct);
}
