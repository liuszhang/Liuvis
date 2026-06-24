using System.Text;
using System.Text.Json;
using System.Linq;
using Liuvis.Generation.Services;
using Microsoft.Extensions.Logging;

namespace Liuvis.Generation.Geometry;

/// <summary>
/// Builds valid GLB 2.0 binary files with real vertex geometry from structured scene descriptions.
/// Generates proper vertex positions, normals, UVs, and indices for common primitives (box, sphere, cylinder, cone).
/// </summary>
public class ProceduralGeometryBuilder
{
    private readonly ILogger<ProceduralGeometryBuilder> _logger;

    public ProceduralGeometryBuilder(ILogger<ProceduralGeometryBuilder> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Generate a complete .glb binary from a scene description.
    /// </summary>
    public byte[] BuildGlb(SceneDescription scene)
    {
        var allVertices = new List<float>();
        var allIndices = new List<uint>();
        var meshes = new List<GlbMesh>();
        var materials = new List<GlbMaterial>();
        uint vertexOffset = 0;

        foreach (var obj in scene.Objects)
        {
            var geometry = GenerateGeometry(obj);
            if (geometry.VertexCount == 0) continue;

            // Offset indices by previous vertex count
            var offsetIndices = geometry.Indices.Select(i => i + vertexOffset).ToList();
            vertexOffset += (uint)geometry.VertexCount;

            allVertices.AddRange(geometry.Vertices);
            allIndices.AddRange(offsetIndices.Select(i => (uint)i));

            var color = ParseColor(obj.Color);
            var matIndex = FindOrAddMaterial(materials, color, obj.Material);
            meshes.Add(new GlbMesh
            {
                VertexBase = allVertices.Count - geometry.Vertices.Length,
                VertexCount = geometry.VertexCount,
                IndexBase = allIndices.Count - offsetIndices.Count,
                IndexCount = offsetIndices.Count,
                MaterialIndex = matIndex
            });
        }

        if (meshes.Count == 0)
        {
            _logger.LogWarning("No geometry generated, returning minimal GLB");
            return GenerateMinimalGlb();
        }

        return BuildGlbBinary(allVertices, allIndices, meshes, materials, scene);
    }

    public byte[] BuildStl(SceneDescription scene)
    {
        var allVertices = new List<float>();
        var allIndices = new List<uint>();

        foreach (var obj in scene.Objects)
        {
            var geometry = GenerateGeometry(obj);
            if (geometry.VertexCount == 0) continue;

            var offset = (uint)(allVertices.Count / 8);
            allVertices.AddRange(geometry.Vertices);
            allIndices.AddRange(geometry.Indices.Select(i => i + offset));
        }

        if (allIndices.Count == 0)
        {
            _logger.LogWarning("No geometry generated, returning minimal STL");
            return GenerateMinimalStl();
        }

        var vertexCount = allVertices.Count / 8;
        var posFloats = new float[vertexCount * 3];
        var normFloats = new float[vertexCount * 3];
        for (int i = 0; i < vertexCount; i++)
        {
            var src = i * 8;
            posFloats[i * 3] = allVertices[src];
            posFloats[i * 3 + 1] = allVertices[src + 1];
            posFloats[i * 3 + 2] = allVertices[src + 2];
            normFloats[i * 3] = allVertices[src + 3];
            normFloats[i * 3 + 1] = allVertices[src + 4];
            normFloats[i * 3 + 2] = allVertices[src + 5];
        }

        var triangleCount = allIndices.Count / 3;
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bw.Write(new byte[80]);
        bw.Write((uint)triangleCount);

        for (int t = 0; t < triangleCount; t++)
        {
            var i0 = (int)allIndices[t * 3];
            var i1 = (int)allIndices[t * 3 + 1];
            var i2 = (int)allIndices[t * 3 + 2];

            var nx = (normFloats[i0 * 3] + normFloats[i1 * 3] + normFloats[i2 * 3]) / 3f;
            var ny = (normFloats[i0 * 3 + 1] + normFloats[i1 * 3 + 1] + normFloats[i2 * 3 + 1]) / 3f;
            var nz = (normFloats[i0 * 3 + 2] + normFloats[i1 * 3 + 2] + normFloats[i2 * 3 + 2]) / 3f;

            bw.Write(nx); bw.Write(ny); bw.Write(nz);

            bw.Write(posFloats[i0 * 3]); bw.Write(posFloats[i0 * 3 + 1]); bw.Write(posFloats[i0 * 3 + 2]);
            bw.Write(posFloats[i1 * 3]); bw.Write(posFloats[i1 * 3 + 1]); bw.Write(posFloats[i1 * 3 + 2]);
            bw.Write(posFloats[i2 * 3]); bw.Write(posFloats[i2 * 3 + 1]); bw.Write(posFloats[i2 * 3 + 2]);

            bw.Write((ushort)0);
        }

        _logger.LogInformation("Built STL: {VertCount}v, {TriCount}triangles, {ByteCount}bytes",
            vertexCount, triangleCount, ms.Length);

        return ms.ToArray();
    }

    private static GeometryData GenerateGeometry(SceneObject obj)
    {
        return obj.Type.ToLowerInvariant() switch
        {
            "box" or "cube" => CreateBox(obj.Size),
            "sphere" or "ball" => CreateSphere(obj.Size),
            "cylinder" => CreateCylinder(obj.Size),
            "cone" => CreateCone(obj.Size),
            _ => CreateBox(obj.Size)
        };
    }

    private static GeometryData CreateBox(double[] size)
    {
        var w = (float)(size.Length > 0 ? size[0] : 1.0) / 2f;
        var h = (float)(size.Length > 1 ? size[1] : 1.0) / 2f;
        var d = (float)(size.Length > 2 ? size[2] : 1.0) / 2f;

        // 24 vertices (4 per face, 6 faces) with position + normal + uv (8 floats each)
        // Layout: [px, py, pz, nx, ny, nz, u, v]
        float[] verts = {
            // Front face (z+)
            -w, -h,  d,  0, 0, 1, 0, 0,
             w, -h,  d,  0, 0, 1, 1, 0,
             w,  h,  d,  0, 0, 1, 1, 1,
            -w,  h,  d,  0, 0, 1, 0, 1,
            // Back face (z-)
             w, -h, -d,  0, 0, -1, 0, 0,
            -w, -h, -d,  0, 0, -1, 1, 0,
            -w,  h, -d,  0, 0, -1, 1, 1,
             w,  h, -d,  0, 0, -1, 0, 1,
            // Top face (y+)
            -w,  h,  d,  0, 1, 0, 0, 0,
             w,  h,  d,  0, 1, 0, 1, 0,
             w,  h, -d,  0, 1, 0, 1, 1,
            -w,  h, -d,  0, 1, 0, 0, 1,
            // Bottom face (y-)
            -w, -h, -d,  0, -1, 0, 0, 0,
             w, -h, -d,  0, -1, 0, 1, 0,
             w, -h,  d,  0, -1, 0, 1, 1,
            -w, -h,  d,  0, -1, 0, 0, 1,
            // Right face (x+)
             w, -h,  d,  1, 0, 0, 0, 0,
             w, -h, -d,  1, 0, 0, 1, 0,
             w,  h, -d,  1, 0, 0, 1, 1,
             w,  h,  d,  1, 0, 0, 0, 1,
            // Left face (x-)
            -w, -h, -d, -1, 0, 0, 0, 0,
            -w, -h,  d, -1, 0, 0, 1, 0,
            -w,  h,  d, -1, 0, 0, 1, 1,
            -w,  h, -d, -1, 0, 0, 0, 1,
        };

        uint[] indices = {
            0,1,2, 0,2,3,     // front
            4,5,6, 4,6,7,     // back
            8,9,10, 8,10,11,  // top
            12,13,14, 12,14,15, // bottom
            16,17,18, 16,18,19, // right
            20,21,22, 20,22,23, // left
        };

        return new GeometryData { Vertices = verts, Indices = indices };
    }

    private static GeometryData CreateSphere(double[] size)
    {
        var radius = (float)(size.Length > 0 ? size[0] : 0.5);
        var latSegs = size.Length > 1 ? (int)size[1] : 32;
        var lonSegs = size.Length > 2 ? (int)size[2] : 32;

        var verts = new List<float>();
        var indices = new List<uint>();

        for (int lat = 0; lat <= latSegs; lat++)
        {
            float theta = (float)(lat * Math.PI / latSegs);
            float sinTheta = MathF.Sin(theta);
            float cosTheta = MathF.Cos(theta);

            for (int lon = 0; lon <= lonSegs; lon++)
            {
                float phi = (float)(lon * 2 * Math.PI / lonSegs);
                float sinPhi = MathF.Sin(phi);
                float cosPhi = MathF.Cos(phi);

                float x = cosPhi * sinTheta;
                float y = cosTheta;
                float z = sinPhi * sinTheta;
                float u = 1f - (float)lon / lonSegs;
                float v = 1f - (float)lat / latSegs;

                verts.AddRange(new[] { x * radius, y * radius, z * radius, x, y, z, u, v });
            }
        }

        for (int lat = 0; lat < latSegs; lat++)
        {
            for (int lon = 0; lon < lonSegs; lon++)
            {
                uint a = (uint)(lat * (lonSegs + 1) + lon);
                uint b = a + (uint)(lonSegs + 1);
                uint c = a + 1;
                uint d = b + 1;
                indices.AddRange(new[] { a, b, d, a, d, c });
            }
        }

        return new GeometryData { Vertices = verts.ToArray(), Indices = indices.ToArray() };
    }

    private static GeometryData CreateCylinder(double[] size)
    {
        var radius = (float)(size.Length > 0 ? size[0] : 0.5);
        var height = (float)(size.Length > 1 ? size[1] : 2.0);
        var segments = size.Length > 2 ? (int)size[2] : 32;
        var halfH = height / 2f;

        var verts = new List<float>();
        var indices = new List<uint>();

        // Side vertices
        for (int i = 0; i <= segments; i++)
        {
            float angle = (float)(i * 2 * Math.PI / segments);
            float x = MathF.Cos(angle);
            float z = MathF.Sin(angle);
            float u = (float)i / segments;

            // Bottom ring
            verts.AddRange(new[] { x * radius, -halfH, z * radius, x, 0, z, u, 1 });
            // Top ring
            verts.AddRange(new[] { x * radius, halfH, z * radius, x, 0, z, u, 0 });
        }

        for (int i = 0; i < segments; i++)
        {
            uint a = (uint)(i * 2);
            uint b = a + 1;
            uint c = a + 2;
            uint d = a + 3;
            indices.AddRange(new[] { a, c, b, b, c, d });
        }

        // Top cap
        uint centerTop = (uint)(verts.Count / 8);
        verts.AddRange(new[] { 0f, halfH, 0f, 0, 1, 0, 0.5f, 0.5f });
        for (int i = 0; i <= segments; i++)
        {
            float angle = (float)(i * 2 * Math.PI / segments);
            float x = MathF.Cos(angle);
            float z = MathF.Sin(angle);
            verts.AddRange(new[] { x * radius, halfH, z * radius, 0, 1, 0, (x + 1) / 2, (z + 1) / 2 });
        }
        for (int i = 0; i < segments; i++)
            indices.AddRange(new[] { centerTop, centerTop + (uint)(i + 2), centerTop + (uint)(i + 1) });

        // Bottom cap
        uint centerBot = (uint)(verts.Count / 8);
        verts.AddRange(new[] { 0f, -halfH, 0f, 0, -1, 0, 0.5f, 0.5f });
        for (int i = 0; i <= segments; i++)
        {
            float angle = (float)(i * 2 * Math.PI / segments);
            float x = MathF.Cos(angle);
            float z = MathF.Sin(angle);
            verts.AddRange(new[] { x * radius, -halfH, z * radius, 0, -1, 0, (x + 1) / 2, (z + 1) / 2 });
        }
        for (int i = 0; i < segments; i++)
            indices.AddRange(new[] { centerBot, centerBot + (uint)(i + 1), centerBot + (uint)(i + 2) });

        return new GeometryData { Vertices = verts.ToArray(), Indices = indices.ToArray() };
    }

    private static GeometryData CreateCone(double[] size)
    {
        var radius = (float)(size.Length > 0 ? size[0] : 0.5);
        var height = (float)(size.Length > 1 ? size[1] : 2.0);
        var segments = size.Length > 2 ? (int)size[2] : 32;
        var halfH = height / 2f;

        var verts = new List<float>();
        var indices = new List<uint>();

        // Side
        for (int i = 0; i <= segments; i++)
        {
            float angle = (float)(i * 2 * Math.PI / segments);
            float x = MathF.Cos(angle);
            float z = MathF.Sin(angle);
            float u = (float)i / segments;
            verts.AddRange(new[] { x * radius, -halfH, z * radius, x, 0.5f, z, u, 1 });
            verts.AddRange(new[] { 0f, halfH, 0f, 0, 1, 0, u, 0 });
        }
        for (int i = 0; i < segments; i++)
        {
            uint a = (uint)(i * 2);
            uint b = a + 1;
            uint c = a + 2;
            uint d = a + 3;
            indices.AddRange(new[] { a, c, b, a, d, c });
        }

        // Bottom cap
        uint botCenter = (uint)(verts.Count / 8);
        verts.AddRange(new[] { 0f, -halfH, 0f, 0, -1, 0, 0.5f, 0.5f });
        for (int i = 0; i <= segments; i++)
        {
            float angle = (float)(i * 2 * Math.PI / segments);
            float x = MathF.Cos(angle);
            float z = MathF.Sin(angle);
            verts.AddRange(new[] { x * radius, -halfH, z * radius, 0, -1, 0, (x + 1) / 2, (z + 1) / 2 });
        }
        for (int i = 0; i < segments; i++)
            indices.AddRange(new[] { botCenter, botCenter + (uint)(i + 1), botCenter + (uint)(i + 2) });

        return new GeometryData { Vertices = verts.ToArray(), Indices = indices.ToArray() };
    }

    private static (float r, float g, float b) ParseColor(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length >= 6)
            return (
                Convert.ToInt32(hex[..2], 16) / 255f,
                Convert.ToInt32(hex[2..4], 16) / 255f,
                Convert.ToInt32(hex[4..6], 16) / 255f
            );
        return (0, 0.83f, 1); // Default cyan
    }

    private static int FindOrAddMaterial(List<GlbMaterial> materials, (float r, float g, float b) color, MaterialProps? props)
    {
        var metal = props?.Metalness ?? 0.5;
        var rough = props?.Roughness ?? 0.3;

        for (int i = 0; i < materials.Count; i++)
        {
            var m = materials[i];
            if (MathF.Abs(m.R - color.r) < 0.01f &&
                MathF.Abs(m.G - color.g) < 0.01f &&
                MathF.Abs(m.B - color.b) < 0.01f &&
                MathF.Abs(m.Metalness - (float)metal) < 0.01f &&
                MathF.Abs(m.Roughness - (float)rough) < 0.01f)
                return i;
        }

        materials.Add(new GlbMaterial
        {
            R = color.r, G = color.g, B = color.b,
            Metalness = (float)metal, Roughness = (float)rough
        });
        return materials.Count - 1;
    }

    private byte[] BuildGlbBinary(List<float> vertices, List<uint> indices,
        List<GlbMesh> meshes, List<GlbMaterial> materials, SceneDescription scene)
    {
        var vertexCount = vertices.Count / 8; // 8 floats per vertex (interleaved: pos3 norm3 uv2)
        var vertexArray = vertices.ToArray();

        // De-interleave: split interleaved [pos, norm, uv] per vertex into separate tightly-packed arrays
        var posFloats = new float[vertexCount * 3];
        var normFloats = new float[vertexCount * 3];
        var uvFloats = new float[vertexCount * 2];
        for (int i = 0; i < vertexCount; i++)
        {
            var src = i * 8;
            posFloats[i * 3 + 0] = vertexArray[src + 0];
            posFloats[i * 3 + 1] = vertexArray[src + 1];
            posFloats[i * 3 + 2] = vertexArray[src + 2];
            normFloats[i * 3 + 0] = vertexArray[src + 3];
            normFloats[i * 3 + 1] = vertexArray[src + 4];
            normFloats[i * 3 + 2] = vertexArray[src + 5];
            uvFloats[i * 2 + 0] = vertexArray[src + 6];
            uvFloats[i * 2 + 1] = vertexArray[src + 7];
        }

        var posBytes = new byte[posFloats.Length * 4];
        var normBytes = new byte[normFloats.Length * 4];
        var uvBytes = new byte[uvFloats.Length * 4];
        var idxBytes = new byte[indices.Count * 4];
        Buffer.BlockCopy(posFloats, 0, posBytes, 0, posBytes.Length);
        Buffer.BlockCopy(normFloats, 0, normBytes, 0, normBytes.Length);
        Buffer.BlockCopy(uvFloats, 0, uvBytes, 0, uvBytes.Length);
        Buffer.BlockCopy(indices.ToArray(), 0, idxBytes, 0, idxBytes.Length);

        // Concatenate: positions | normals | uvs | indices
        var binBytes = new byte[posBytes.Length + normBytes.Length + uvBytes.Length + idxBytes.Length];
        var offset = 0;
        Buffer.BlockCopy(posBytes, 0, binBytes, offset, posBytes.Length); offset += posBytes.Length;
        Buffer.BlockCopy(normBytes, 0, binBytes, offset, normBytes.Length); offset += normBytes.Length;
        Buffer.BlockCopy(uvBytes, 0, binBytes, offset, uvBytes.Length); offset += uvBytes.Length;
        Buffer.BlockCopy(idxBytes, 0, binBytes, offset, idxBytes.Length);

        // Compute position bounds for accessor min/max (required by Three.js)
        float posMinX = float.MaxValue, posMinY = float.MaxValue, posMinZ = float.MaxValue;
        float posMaxX = float.MinValue, posMaxY = float.MinValue, posMaxZ = float.MinValue;
        for (int i = 0; i < vertexCount; i++)
        {
            var x = posFloats[i * 3 + 0];
            var y = posFloats[i * 3 + 1];
            var z = posFloats[i * 3 + 2];
            if (x < posMinX) posMinX = x; if (x > posMaxX) posMaxX = x;
            if (y < posMinY) posMinY = y; if (y > posMaxY) posMaxY = y;
            if (z < posMinZ) posMinZ = z; if (z > posMaxZ) posMaxZ = z;
        }

        // Byte offsets for buffer views
        var posByteOffset = 0;
        var posByteLength = posBytes.Length;
        var normByteOffset = posByteLength;
        var normByteLength = normBytes.Length;
        var uvByteOffset = normByteOffset + normByteLength;
        var uvByteLength = uvBytes.Length;
        var idxByteOffset = uvByteOffset + uvByteLength;
        var idxByteLength = idxBytes.Length;

        // Build GLTF JSON
        var gltfJson = BuildGltfJsonDeinterleaved(vertexCount, indices.Count, meshes, materials, scene,
            posByteOffset, posByteLength, normByteOffset, normByteLength,
            uvByteOffset, uvByteLength, idxByteOffset, idxByteLength,
            posMinX, posMinY, posMinZ, posMaxX, posMaxY, posMaxZ
        );
        var jsonBytes = Encoding.UTF8.GetBytes(gltfJson);

        // Pad bins to 4-byte alignment
        var jsonPadding = (4 - (jsonBytes.Length % 4)) % 4;
        var binPadding = (4 - (binBytes.Length % 4)) % 4;

        // GLB header: magic + version + totalLength
        var totalLength = 12 + 8 + (jsonBytes.Length + jsonPadding) + 8 + (binBytes.Length + binPadding);

        using var ms = new MemoryStream();
        ms.Write("glTF"u8);
        ms.Write(BitConverter.GetBytes((uint)2)); // version
        ms.Write(BitConverter.GetBytes((uint)totalLength));

        // JSON chunk
        ms.Write(BitConverter.GetBytes((uint)(jsonBytes.Length + jsonPadding)));
        ms.Write("JSON"u8);
        ms.Write(jsonBytes);
        for (int i = 0; i < jsonPadding; i++) ms.WriteByte(0x20);

        // Binary chunk
        ms.Write(BitConverter.GetBytes((uint)(binBytes.Length + binPadding)));
        ms.Write("BIN\0"u8);
        ms.Write(binBytes);
        for (int i = 0; i < binPadding; i++) ms.WriteByte(0);

        _logger.LogInformation("Built GLB: {VertCount}v, {IdxCount}i, {MeshCount}meshes, {ByteCount}bytes",
            vertexCount, indices.Count, meshes.Count, totalLength);

        return ms.ToArray();
    }

    private static string BuildGltfJsonDeinterleaved(
        int vertexCount, int indexCount,
        List<GlbMesh> meshes, List<GlbMaterial> materials, SceneDescription scene,
        int posByteOffset, int posByteLength,
        int normByteOffset, int normByteLength,
        int uvByteOffset, int uvByteLength,
        int idxByteOffset, int idxByteLength,
        float posMinX, float posMinY, float posMinZ,
        float posMaxX, float posMaxY, float posMaxZ)
    {
        // Build nodes
        var nodes = new List<object>();
        for (int i = 0; i < meshes.Count; i++)
        {
            var obj = scene.Objects.Count > i ? scene.Objects[i] : null;
            nodes.Add(new
            {
                mesh = i,
                translation = obj?.Position ?? new[] { 0.0, 0.0, 0.0 },
                rotation = obj?.Rotation ?? new[] { 0.0, 0.0, 0.0 }
            });
        }

        // Build meshes — each primitive references 4 accessors (pos, norm, uv, index)
        var meshList = new List<object>();
        foreach (var m in meshes)
        {
            meshList.Add(new
            {
                primitives = new[]
                {
                    new
                    {
                        attributes = new { POSITION = 0, NORMAL = 1, TEXCOORD_0 = 2 },
                        indices = 3,
                        material = m.MaterialIndex
                    }
                }
            });
        }

        // Accessors — each points to its own bufferView (tightly packed)
        var accessors = new List<object>
        {
            new { bufferView = 0, componentType = 5126, count = vertexCount, type = "VEC3",
                  min = new[] { posMinX, posMinY, posMinZ }, max = new[] { posMaxX, posMaxY, posMaxZ } },
            new { bufferView = 1, componentType = 5126, count = vertexCount, type = "VEC3" },
            new { bufferView = 2, componentType = 5126, count = vertexCount, type = "VEC2" },
            new { bufferView = 3, componentType = 5125, count = indexCount, type = "SCALAR" }
        };

        // Buffer views — 4 separate tightly-packed views, NO byteStride
        var bufferViews = new List<object>
        {
            new { buffer = 0, byteOffset = posByteOffset, byteLength = posByteLength, target = 34962 },
            new { buffer = 0, byteOffset = normByteOffset, byteLength = normByteLength, target = 34962 },
            new { buffer = 0, byteOffset = uvByteOffset, byteLength = uvByteLength, target = 34962 },
            new { buffer = 0, byteOffset = idxByteOffset, byteLength = idxByteLength, target = 34963 }
        };

        // Materials
        var matList = new List<object>();
        foreach (var m in materials)
        {
            matList.Add(new
            {
                pbrMetallicRoughness = new
                {
                    baseColorFactor = new[] { m.R, m.G, m.B, 1.0f },
                    metallicFactor = m.Metalness,
                    roughnessFactor = m.Roughness
                }
            });
        }

        var totalBufferSize = posByteLength + normByteLength + uvByteLength + idxByteLength;

        var gltf = new
        {
            asset = new { version = "2.0", generator = "Liuvis LLM+Procedural" },
            scene = 0,
            scenes = new[] { new { nodes = Enumerable.Range(0, nodes.Count).ToArray() } },
            nodes,
            meshes = meshList,
            accessors,
            bufferViews,
            buffers = new[] { new { byteLength = totalBufferSize } },
            materials = matList
        };

        return JsonSerializer.Serialize(gltf, new JsonSerializerOptions { WriteIndented = true });
    }

    private static byte[] GenerateMinimalGlb()
    {
        var json = @"{""asset"":{""version"":""2.0"",""generator"":""Liuvis""},""scene"":0,""scenes"":[{""nodes"":[]}],""nodes"":[],""meshes"":[]}";
        var jsonBytes = Encoding.UTF8.GetBytes(json);
        var pad = (4 - (jsonBytes.Length % 4)) % 4;
        var total = (uint)(12 + 8 + jsonBytes.Length + pad);

        using var ms = new MemoryStream();
        ms.Write("glTF"u8);
        ms.Write(BitConverter.GetBytes((uint)2));
        ms.Write(BitConverter.GetBytes(total));
        ms.Write(BitConverter.GetBytes((uint)(jsonBytes.Length + pad)));
        ms.Write("JSON"u8);
        ms.Write(jsonBytes);
        for (int i = 0; i < pad; i++) ms.WriteByte(0x20);
        return ms.ToArray();
    }

    private static byte[] GenerateMinimalStl()
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write(new byte[80]);
        bw.Write((uint)0);
        return ms.ToArray();
    }

    public byte[] BuildObj(SceneDescription scene)
    {
        var allVertices = new List<float>();
        var allNormals = new List<float>();
        var allUvs = new List<float>();
        var allIndices = new List<uint>();
        uint vertexOffset = 0;

        var sb = new StringBuilder();
        sb.AppendLine("# Generated by Liuvis Studio");
        sb.AppendLine();

        foreach (var obj in scene.Objects)
        {
            var geometry = GenerateGeometry(obj);
            if (geometry.VertexCount == 0) continue;

            var offsetIndices = geometry.Indices.Select(i => i + vertexOffset).ToList();

            // Write vertices
            for (int i = 0; i < geometry.VertexCount; i++)
            {
                var src = i * 8;
                sb.AppendLine($"v {geometry.Vertices[src]:F6} {geometry.Vertices[src + 1]:F6} {geometry.Vertices[src + 2]:F6}");
            }
            // Write normals
            for (int i = 0; i < geometry.VertexCount; i++)
            {
                var src = i * 8;
                sb.AppendLine($"vn {geometry.Vertices[src + 3]:F6} {geometry.Vertices[src + 4]:F6} {geometry.Vertices[src + 5]:F6}");
            }
            // Write UVs
            for (int i = 0; i < geometry.VertexCount; i++)
            {
                var src = i * 8;
                sb.AppendLine($"vt {geometry.Vertices[src + 6]:F6} {geometry.Vertices[src + 7]:F6}");
            }

            // Write faces
            for (int i = 0; i < offsetIndices.Count; i += 3)
            {
                var a = offsetIndices[i] + 1;
                var b = offsetIndices[i + 1] + 1;
                var c = offsetIndices[i + 2] + 1;
                sb.AppendLine($"f {a}/{a}/{a} {b}/{b}/{b} {c}/{c}/{c}");
            }

            vertexOffset += (uint)geometry.VertexCount;
            sb.AppendLine();
        }

        _logger.LogInformation("Built OBJ: {VertCount}v, {ByteCount}bytes", vertexOffset, sb.Length);
        return Encoding.UTF8.GetBytes(sb.ToString());
    }
}

internal record GeometryData
{
    public float[] Vertices { get; init; } = Array.Empty<float>();
    public uint[] Indices { get; init; } = Array.Empty<uint>();
    public int VertexCount => Vertices.Length / 8;
}

internal class GlbMesh
{
    public int VertexBase { get; init; }
    public int VertexCount { get; init; }
    public int IndexBase { get; init; }
    public int IndexCount { get; init; }
    public int MaterialIndex { get; init; }
}

internal class GlbMaterial
{
    public float R { get; init; }
    public float G { get; init; }
    public float B { get; init; }
    public float Metalness { get; init; } = 0.5f;
    public float Roughness { get; init; } = 0.3f;
}
