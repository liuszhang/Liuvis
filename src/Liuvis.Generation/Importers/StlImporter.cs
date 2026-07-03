using System.Numerics;
using Liuvis.Core.Entities;
using Liuvis.Core.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Liuvis.Generation.Importers;

/// <summary>
/// STL file importer supporting both ASCII and binary formats.
/// Parses STL content and converts to Model3D entity.
/// For Liuvis-exported STLs, component index is encoded in the attribute byte count field.
/// Falls back to heuristic connectivity-based separation for third-party STLs.
/// </summary>
public class StlImporter
{
    private readonly ILogger<StlImporter> _logger;

    public StlImporter(ILogger<StlImporter> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Import STL file from stream and convert to Model3D.
    /// </summary>
    public async Task<Model3D> ImportAsync(Stream stream, string modelName, CancellationToken cancellationToken = default)
    {
        var (vertices, normals, triangleCount, triComponentIds) = await ParseStlAsync(stream, cancellationToken);

        if (triangleCount == 0)
        {
            throw new InvalidOperationException("STL file contains no valid triangles.");
        }

        _logger.LogInformation("Parsed STL: {TriangleCount} triangles, {VertexCount} vertices",
            triangleCount, vertices.Count / 3);

        // If STL has component index metadata (Liuvis-exported), use exact separation.
        // Otherwise fall back to heuristic connectivity analysis.
        var componentInfos = triComponentIds.Count == triangleCount
            ? SeparateByComponentIndex(vertices, triangleCount, triComponentIds)
            : SeparateComponents(vertices, triangleCount);

        _logger.LogInformation("Component separation: found {ComponentCount} components",
            componentInfos.Count);

        var model = new Model3D(modelName,
            $"Imported STL model with {triangleCount} triangles in {componentInfos.Count} component(s)",
            Core.Enums.ModelFormat.STL);

        if (componentInfos.Count == 0)
        {
            var fallback = new ModelComponent(model.ModelId, "MainBody", "mesh");
            model.AddComponent(fallback);
        }
        else
        {
            for (int i = 0; i < componentInfos.Count; i++)
            {
                var ci = componentInfos[i];
                var component = new ModelComponent(model.ModelId, ci.Name, "mesh");
                component.SetTransform(new Transform3D
                {
                    Position = new Liuvis.Core.ValueObjects.Vector3(
                        (float)ci.Center.X,
                        (float)ci.Center.Y,
                        (float)ci.Center.Z),
                    Scale = Liuvis.Core.ValueObjects.Vector3.One
                });
                model.AddComponent(component);

                model.Metadata[$"Component_{i}_Name"] = ci.Name;
                model.Metadata[$"Component_{i}_TriangleCount"] = ci.TriangleCount.ToString();
                model.Metadata[$"Component_{i}_MinX"] = ci.MinBound.X.ToString("F6");
                model.Metadata[$"Component_{i}_MinY"] = ci.MinBound.Y.ToString("F6");
                model.Metadata[$"Component_{i}_MinZ"] = ci.MinBound.Z.ToString("F6");
                model.Metadata[$"Component_{i}_MaxX"] = ci.MaxBound.X.ToString("F6");
                model.Metadata[$"Component_{i}_MaxY"] = ci.MaxBound.Y.ToString("F6");
                model.Metadata[$"Component_{i}_MaxZ"] = ci.MaxBound.Z.ToString("F6");
            }
        }

        model.Metadata["TriangleCount"] = triangleCount.ToString();
        model.Metadata["VertexCount"] = (vertices.Count / 3).ToString();
        model.Metadata["SourceFormat"] = "STL";
        model.Metadata["ComponentCount"] = componentInfos.Count.ToString();

        model.AddTag("imported");
        model.AddTag("stl");

        return model;
    }

    /// <summary>
    /// Exact component separation using per-triangle component index from STL attribute bytes.
    /// Triangles are grouped by their component ID (1-based). Used for Liuvis-exported STLs
    /// where the component index is encoded in the attribute byte count field.
    /// </summary>
    public List<StlComponentInfo> SeparateByComponentIndex(List<float> vertices, int triangleCount, List<ushort> triComponentIds)
    {
        const int minTrianglesPerComponent = 4;

        if (triangleCount == 0 || triComponentIds.Count != triangleCount)
            return new List<StlComponentInfo>();

        // Group triangle indices by component ID
        var componentGroups = new Dictionary<ushort, List<int>>();
        for (int t = 0; t < triangleCount; t++)
        {
            var cid = triComponentIds[t];
            if (!componentGroups.TryGetValue(cid, out var list))
            {
                list = new List<int>();
                componentGroups[cid] = list;
            }
            list.Add(t);
        }

        var result = new List<StlComponentInfo>();
        int compIdx = 0;
        foreach (var (cid, triList) in componentGroups.OrderBy(kv => kv.Key))
        {
            if (triList.Count < minTrianglesPerComponent) continue;

            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
            foreach (int t in triList)
            {
                for (int v = 0; v < 3; v++)
                {
                    int idx = t * 9 + v * 3;
                    double x = vertices[idx], y = vertices[idx + 1], z = vertices[idx + 2];
                    if (x < minX) minX = x; if (y < minY) minY = y; if (z < minZ) minZ = z;
                    if (x > maxX) maxX = x; if (y > maxY) maxY = y; if (z > maxZ) maxZ = z;
                }
            }

            compIdx++;
            result.Add(new StlComponentInfo
            {
                Name = componentGroups.Count == 1 ? "MainBody" : $"Component_{compIdx}",
                TriangleCount = triList.Count,
                TriangleIndices = triList,
                MinBound = new Vector3D(minX, minY, minZ),
                MaxBound = new Vector3D(maxX, maxY, maxZ),
                Center = new Vector3D((minX + maxX) / 2.0, (minY + maxY) / 2.0, (minZ + maxZ) / 2.0)
            });
        }

        return result;
    }

    /// <summary>
    /// Heuristic component separation using vertex-level normal clustering.
    /// Fallback for STL files without component index metadata (third-party STLs).
    /// </summary>
    public List<StlComponentInfo> SeparateComponents(List<float> vertices, int triangleCount)
    {
        const int minTrianglesPerComponent = 4;
        const double splitThresholdDeg = 30.0;

        if (triangleCount == 0) return new List<StlComponentInfo>();

        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
        for (int i = 0; i < vertices.Count; i += 3)
        {
            double x = vertices[i], y = vertices[i + 1], z = vertices[i + 2];
            if (x < minX) minX = x; if (x > maxX) maxX = x;
            if (y < minY) minY = y; if (y > maxY) maxY = y;
            if (z < minZ) minZ = z; if (z > maxZ) maxZ = z;
        }
        double modelSize = Math.Max(Math.Max(maxX - minX, maxY - minY), Math.Max(maxZ - minZ, 1.0));
        double epsilon = Math.Max(modelSize * 1e-6, 1e-7);
        double quantize = 1.0 / epsilon;

        // Vertex welding
        var vertexKeyToIndex = new Dictionary<long, int>();
        var canonicalIdx = new int[triangleCount * 3];
        for (int vi = 0; vi < triangleCount * 3; vi++)
        {
            int baseIdx = vi * 3;
            long key = HashVertex(vertices[baseIdx], vertices[baseIdx + 1], vertices[baseIdx + 2], quantize);
            if (!vertexKeyToIndex.TryGetValue(key, out int ci))
            {
                ci = vertexKeyToIndex.Count;
                vertexKeyToIndex[key] = ci;
            }
            canonicalIdx[vi] = ci;
        }

        int uniqueVerts = vertexKeyToIndex.Count;

        var canonicalPositions = new (double X, double Y, double Z)[uniqueVerts];
        var seenCanonical = new HashSet<int>();
        for (int vi = 0; vi < triangleCount * 3; vi++)
        {
            int ci = canonicalIdx[vi];
            if (seenCanonical.Add(ci))
            {
                int baseIdx = vi * 3;
                canonicalPositions[ci] = (vertices[baseIdx], vertices[baseIdx + 1], vertices[baseIdx + 2]);
            }
        }

        var vertToTris = new HashSet<int>[uniqueVerts];
        for (int i = 0; i < uniqueVerts; i++) vertToTris[i] = new HashSet<int>();
        for (int t = 0; t < triangleCount; t++)
            for (int v = 0; v < 3; v++)
                vertToTris[canonicalIdx[t * 3 + v]].Add(t);

        var triNormals = new (double X, double Y, double Z)[triangleCount];
        for (int t = 0; t < triangleCount; t++)
        {
            var p0 = canonicalPositions[canonicalIdx[t * 3]];
            var p1 = canonicalPositions[canonicalIdx[t * 3 + 1]];
            var p2 = canonicalPositions[canonicalIdx[t * 3 + 2]];
            triNormals[t] = Normalize(Cross(Sub(p1, p0), Sub(p2, p0)));
        }

        double cosSplit = Math.Cos(splitThresholdDeg * Math.PI / 180.0);
        var splitPairs = new HashSet<(int, int)>();

        for (int vi = 0; vi < uniqueVerts; vi++)
        {
            var triArray = vertToTris[vi].ToArray();
            if (triArray.Length < 2) continue;

            var clusters = new List<List<(int Triangle, (double X, double Y, double Z) Normal)>>();
            foreach (int t in triArray)
            {
                var n = triNormals[t];
                int bestCluster = -1;
                double bestDot = double.MinValue;
                for (int c = 0; c < clusters.Count; c++)
                {
                    double d = Dot(n, clusters[c][0].Normal);
                    if (d > bestDot) { bestDot = d; bestCluster = c; }
                }

                if (bestCluster >= 0 && bestDot >= cosSplit)
                    clusters[bestCluster].Add((t, n));
                else
                    clusters.Add(new List<(int, (double, double, double))> { (t, n) });
            }

            if (clusters.Count > 1)
            {
                for (int ci = 0; ci < clusters.Count; ci++)
                    for (int cj = ci + 1; cj < clusters.Count; cj++)
                        foreach (var (tA, _) in clusters[ci])
                            foreach (var (tB, _) in clusters[cj])
                                splitPairs.Add(tA < tB ? (tA, tB) : (tB, tA));
            }
        }

        var adjacency = new HashSet<int>[triangleCount];
        for (int i = 0; i < triangleCount; i++) adjacency[i] = new HashSet<int>();

        for (int vi = 0; vi < uniqueVerts; vi++)
        {
            var triArray = vertToTris[vi].ToArray();
            for (int i = 0; i < triArray.Length; i++)
                for (int j = i + 1; j < triArray.Length; j++)
                {
                    int tA = triArray[i], tB = triArray[j];
                    var key = tA < tB ? (tA, tB) : (tB, tA);
                    if (splitPairs.Contains(key)) continue;
                    adjacency[tA].Add(tB);
                    adjacency[tB].Add(tA);
                }
        }

        var visited = new bool[triangleCount];
        var components = new List<List<int>>();
        var smalls = new List<List<int>>();

        for (int start = 0; start < triangleCount; start++)
        {
            if (visited[start]) continue;
            var comp = new List<int>();
            var queue = new Queue<int>();
            queue.Enqueue(start);
            visited[start] = true;

            while (queue.Count > 0)
            {
                int t = queue.Dequeue();
                comp.Add(t);
                foreach (int neighbor in adjacency[t])
                {
                    if (!visited[neighbor])
                    {
                        visited[neighbor] = true;
                        queue.Enqueue(neighbor);
                    }
                }
            }

            if (comp.Count >= minTrianglesPerComponent)
                components.Add(comp);
            else
                smalls.Add(comp);
        }

        foreach (var sg in smalls)
        {
            if (components.Count > 0)
            {
                var sc = Centroid(vertices, sg);
                int best = 0;
                double bestDist = double.MaxValue;
                for (int i = 0; i < components.Count; i++)
                {
                    var lc = Centroid(vertices, components[i]);
                    double d = SqrDist(sc, lc);
                    if (d < bestDist) { bestDist = d; best = i; }
                }
                components[best].AddRange(sg);
            }
            else components.Add(sg);
        }

        components.Sort((a, b) => b.Count.CompareTo(a.Count));

        var result = new List<StlComponentInfo>();
        for (int i = 0; i < components.Count; i++)
        {
            var c = components[i];
            var aabbMin = new Vector3D(double.MaxValue, double.MaxValue, double.MaxValue);
            var aabbMax = new Vector3D(double.MinValue, double.MinValue, double.MinValue);

            foreach (int t in c)
                for (int v = 0; v < 3; v++)
                {
                    int idx = t * 9 + v * 3;
                    double x = vertices[idx], y = vertices[idx + 1], z = vertices[idx + 2];
                    if (x < aabbMin.X) aabbMin = aabbMin with { X = x };
                    if (y < aabbMin.Y) aabbMin = aabbMin with { Y = y };
                    if (z < aabbMin.Z) aabbMin = aabbMin with { Z = z };
                    if (x > aabbMax.X) aabbMax = aabbMax with { X = x };
                    if (y > aabbMax.Y) aabbMax = aabbMax with { Y = y };
                    if (z > aabbMax.Z) aabbMax = aabbMax with { Z = z };
                }

            result.Add(new StlComponentInfo
            {
                Name = components.Count == 1 ? "MainBody" : $"Component_{i + 1}",
                TriangleCount = c.Count,
                TriangleIndices = c.OrderBy(t => t).ToList(),
                MinBound = aabbMin,
                MaxBound = aabbMax,
                Center = new Vector3D(
                    (aabbMin.X + aabbMax.X) / 2.0,
                    (aabbMin.Y + aabbMax.Y) / 2.0,
                    (aabbMin.Z + aabbMax.Z) / 2.0)
            });
        }

        return result;
    }

    private static (double X, double Y, double Z) Sub((double X, double Y, double Z) a, (double X, double Y, double Z) b)
        => (a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    private static (double X, double Y, double Z) Cross((double X, double Y, double Z) a, (double X, double Y, double Z) b)
        => (a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    private static double Dot((double X, double Y, double Z) a, (double X, double Y, double Z) b)
        => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    private static (double X, double Y, double Z) Normalize((double X, double Y, double Z) v)
    {
        double len = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
        return len < 1e-10 ? (0, 0, 1) : (v.X / len, v.Y / len, v.Z / len);
    }

    private static long HashVertex(double x, double y, double z, double quantize)
    {
        long ix = (long)Math.Round(x * quantize);
        long iy = (long)Math.Round(y * quantize);
        long iz = (long)Math.Round(z * quantize);
        return ((ix & 0x1FFFFF) << 42) | ((iy & 0x1FFFFF) << 21) | (iz & 0x1FFFFF);
    }

    private static (double X, double Y, double Z) Centroid(List<float> vertices, List<int> triangles)
    {
        double cx = 0, cy = 0, cz = 0;
        int n = 0;
        foreach (int t in triangles)
            for (int v = 0; v < 3; v++)
            {
                int idx = t * 9 + v * 3;
                cx += vertices[idx]; cy += vertices[idx + 1]; cz += vertices[idx + 2];
                n++;
            }
        return (cx / n, cy / n, cz / n);
    }

    private static double SqrDist((double X, double Y, double Z) a, (double X, double Y, double Z) b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return dx * dx + dy * dy + dz * dz;
    }

    private async Task<(List<float> vertices, List<float> normals, int triangleCount, List<ushort> triComponentIds)> ParseStlAsync(
        Stream stream, CancellationToken cancellationToken)
    {
        var isBinary = await IsBinaryStlAsync(stream, cancellationToken);
        stream.Position = 0;

        if (isBinary)
        {
            _logger.LogInformation("Detected binary STL format");
            return await ParseBinaryStlAsync(stream, cancellationToken);
        }
        else
        {
            _logger.LogInformation("Detected ASCII STL format");
            return await ParseAsciiStlAsync(stream, cancellationToken);
        }
    }

    private async Task<bool> IsBinaryStlAsync(Stream stream, CancellationToken cancellationToken)
    {
        const int headerSize = 80;
        const int triangleCountSize = 4;
        if (stream.Length < headerSize + triangleCountSize) return false;

        var buffer = new byte[5];
        await stream.ReadExactlyAsync(buffer, 0, 5, cancellationToken);
        var firstBytes = System.Text.Encoding.ASCII.GetString(buffer);

        if (firstBytes.StartsWith("solid", StringComparison.OrdinalIgnoreCase))
        {
            stream.Position = 0;
            var asciiContent = await ReadAllTextAsync(stream, cancellationToken);
            stream.Position = 0;
            if (asciiContent.Contains("facet normal", StringComparison.OrdinalIgnoreCase) &&
                asciiContent.Contains("outer loop", StringComparison.OrdinalIgnoreCase))
                return false;
        }

        stream.Position = 0;
        var header = new byte[headerSize];
        await stream.ReadExactlyAsync(header, 0, headerSize, cancellationToken);
        var tcBytes = new byte[triangleCountSize];
        await stream.ReadExactlyAsync(tcBytes, 0, triangleCountSize, cancellationToken);
        var expectedCount = BitConverter.ToUInt32(tcBytes, 0);
        var expectedSize = headerSize + triangleCountSize + (expectedCount * 50);
        return Math.Abs(stream.Length - (long)expectedSize) < 100;
    }

    private async Task<(List<float> vertices, List<float> normals, int triangleCount, List<ushort> triComponentIds)> ParseBinaryStlAsync(
        Stream stream, CancellationToken cancellationToken)
    {
        var vertices = new List<float>();
        var normals = new List<float>();
        var triComponentIds = new List<ushort>();
        stream.Position = 80;

        var tcBytes = new byte[4];
        await stream.ReadExactlyAsync(tcBytes, 0, 4, cancellationToken);
        var triangleCount = BitConverter.ToUInt32(tcBytes, 0);

        _logger.LogInformation("Binary STL: {TriangleCount} triangles", triangleCount);

        var triData = new byte[50];
        for (int i = 0; i < triangleCount; i++)
        {
            await stream.ReadExactlyAsync(triData, 0, 50, cancellationToken);
            var nx = BitConverter.ToSingle(triData, 0);
            var ny = BitConverter.ToSingle(triData, 4);
            var nz = BitConverter.ToSingle(triData, 8);
            vertices.AddRange(new[] {
                BitConverter.ToSingle(triData, 12), BitConverter.ToSingle(triData, 16), BitConverter.ToSingle(triData, 20),
                BitConverter.ToSingle(triData, 24), BitConverter.ToSingle(triData, 28), BitConverter.ToSingle(triData, 32),
                BitConverter.ToSingle(triData, 36), BitConverter.ToSingle(triData, 40), BitConverter.ToSingle(triData, 44)
            });
            normals.AddRange(new[] { nx, ny, nz, nx, ny, nz, nx, ny, nz });

            // Read component index from attribute byte count (bytes 48-49)
            var compId = BitConverter.ToUInt16(triData, 48);
            triComponentIds.Add(compId);
        }

        return (vertices, normals, (int)triangleCount, triComponentIds);
    }

    private async Task<(List<float> vertices, List<float> normals, int triangleCount, List<ushort> triComponentIds)> ParseAsciiStlAsync(
        Stream stream, CancellationToken cancellationToken)
    {
        var vertices = new List<float>();
        var normals = new List<float>();
        int triangleCount = 0;
        using var reader = new StreamReader(stream, System.Text.Encoding.ASCII, leaveOpen: true);
        string? line;
        float nx = 0, ny = 0, nz = 0;
        int vi = 0;

        while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
        {
            line = line.Trim();
            if (line.StartsWith("facet normal", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 5)
                {
                    float.TryParse(parts[2], out nx);
                    float.TryParse(parts[3], out ny);
                    float.TryParse(parts[4], out nz);
                }
            }
            else if (line.StartsWith("vertex", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 4)
                {
                    float x = 0, y = 0, z = 0;
                    float.TryParse(parts[1], out x);
                    float.TryParse(parts[2], out y);
                    float.TryParse(parts[3], out z);
                    vertices.AddRange(new[] { x, y, z });
                    normals.AddRange(new[] { nx, ny, nz });
                    vi++;
                    if (vi == 3) { triangleCount++; vi = 0; }
                }
            }
        }

        _logger.LogInformation("ASCII STL: {TriangleCount} triangles parsed", triangleCount);
        // ASCII STL has no attribute bytes — return empty component IDs for fallback
        return (vertices, normals, triangleCount, new List<ushort>());
    }

    private async Task<string> ReadAllTextAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, System.Text.Encoding.ASCII,
            detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }
}

/// <summary>Detected sub-mesh component within an STL file.</summary>
public class StlComponentInfo
{
    public string Name { get; set; } = "Component";
    public int TriangleCount { get; set; }
    public List<int> TriangleIndices { get; set; } = new();
    public Vector3D MinBound { get; set; }
    public Vector3D MaxBound { get; set; }
    public Vector3D Center { get; set; }
}

/// <summary>Double-precision 3D vector.</summary>
public readonly record struct Vector3D(double X, double Y, double Z);
