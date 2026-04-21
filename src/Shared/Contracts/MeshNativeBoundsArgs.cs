using System.Text.Json.Serialization;

namespace Reify.Shared.Contracts;

public sealed record MeshNativeBoundsArgs(
    [property: JsonPropertyName("asset_path")]      string? AssetPath,
    [property: JsonPropertyName("gameobject_path")] string? GameObjectPath,
    [property: JsonPropertyName("instance_id")]     int?    InstanceId
);

public sealed record Vec3(
    [property: JsonPropertyName("x")] float X,
    [property: JsonPropertyName("y")] float Y,
    [property: JsonPropertyName("z")] float Z
);

public sealed record BoundsData(
    [property: JsonPropertyName("center")]  Vec3 Center,
    [property: JsonPropertyName("size")]    Vec3 Size,
    [property: JsonPropertyName("extents")] Vec3 Extents,
    [property: JsonPropertyName("min")]     Vec3 Min,
    [property: JsonPropertyName("max")]     Vec3 Max
);

public sealed record TransformData(
    [property: JsonPropertyName("local_scale")]    Vec3 LocalScale,
    [property: JsonPropertyName("lossy_scale")]    Vec3 LossyScale,
    [property: JsonPropertyName("world_position")] Vec3 WorldPosition
);

public sealed record MeshSourceRef(
    [property: JsonPropertyName("type")]       string Type,
    [property: JsonPropertyName("identifier")] string Identifier
);

public sealed record SubmeshBounds(
    [property: JsonPropertyName("index")]  int Index,
    [property: JsonPropertyName("bounds")] BoundsData Bounds
);

public sealed record MeshNativeBoundsResponse(
    [property: JsonPropertyName("source")]               MeshSourceRef Source,
    [property: JsonPropertyName("mesh_name")]            string MeshName,
    [property: JsonPropertyName("submesh_count")]        int SubmeshCount,
    [property: JsonPropertyName("native_bounds")]        BoundsData NativeBounds,
    [property: JsonPropertyName("effective_bounds")]     BoundsData? EffectiveBounds,
    [property: JsonPropertyName("transform")]            TransformData? Transform,
    [property: JsonPropertyName("submeshes")]            SubmeshBounds[]? Submeshes,
    [property: JsonPropertyName("vertex_count")]         int VertexCount,
    [property: JsonPropertyName("triangle_count")]       int TriangleCount,
    [property: JsonPropertyName("has_normals")]          bool HasNormals,
    [property: JsonPropertyName("has_uvs")]              bool HasUvs,
    [property: JsonPropertyName("has_colors")]           bool HasColors,
    [property: JsonPropertyName("has_skinning")]         bool HasSkinning,
    [property: JsonPropertyName("is_readable")]          bool IsReadable,
    [property: JsonPropertyName("import_scale_factor")]  float? ImportScaleFactor,
    [property: JsonPropertyName("import_global_scale")]  float? ImportGlobalScale,
    [property: JsonPropertyName("warnings")]             string[] Warnings,
    [property: JsonPropertyName("read_at_utc")]          string ReadAtUtc,
    [property: JsonPropertyName("frame")]                long Frame
);
