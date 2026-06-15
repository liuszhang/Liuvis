using Liuvis.Core.Entities;
using Liuvis.Core.Enums;
using Liuvis.Core.Interfaces;
using Liuvis.Core.ValueObjects;
using Liuvis.Generation.Geometry;
using Microsoft.Extensions.Logging;

namespace Liuvis.Generation.Services;

/// <summary>Generates 3D models from design specifications using LLM-driven procedural geometry.</summary>
public class ModelGenerator : IModelGenerator
{
    private readonly IObjectStorageService _storage;
    private readonly LLMDesignService _llmDesign;
    private readonly ProceduralGeometryBuilder _geometryBuilder;
    private readonly ILogger<ModelGenerator> _logger;

    public ModelGenerator(
        IObjectStorageService storage,
        LLMDesignService llmDesign,
        ProceduralGeometryBuilder geometryBuilder,
        ILogger<ModelGenerator> logger)
    {
        _storage = storage;
        _llmDesign = llmDesign;
        _geometryBuilder = geometryBuilder;
        _logger = logger;
    }

    public async Task<Model3D> GenerateModel(DesignSpec spec, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Generating model: {ComponentCount} components from: {Description}",
            spec.Components.Count, spec.Intent.OriginalText);

        var model = new Model3D(
            $"Design_{DateTime.UtcNow:yyyyMMddHHmmss}",
            $"Generated from: {spec.Intent.OriginalText}",
            ModelFormat.GLB);

        byte[] glbData;

        try
        {
            // Step 1: Use LLM to generate structured scene description
            var scene = await _llmDesign.GenerateSceneFromText(spec.Intent.OriginalText, cancellationToken);

            if (scene.Objects.Count > 0)
            {
                // Step 2: Build real geometry from scene description
                glbData = _geometryBuilder.BuildGlb(scene);

                // Step 3: Create model components matching scene objects
                foreach (var obj in scene.Objects)
                {
                    var comp = new ModelComponent(model.ModelId, obj.Type, obj.Type);
                    comp.SetMaterial(new MaterialSpec
                    {
                        Color = obj.Color,
                        Type = MaterialType.PBR,
                        Metalness = obj.Material?.Metalness ?? 0.5,
                        Roughness = obj.Material?.Roughness ?? 0.3
                    });
                    model.AddComponent(comp);
                }
            }
            else
            {
                _logger.LogWarning("LLM returned empty scene, falling back to template geometry");
                glbData = GenerateSimpleGlb(model);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM geometry generation failed, falling back to template");
            glbData = GenerateSimpleGlb(model);
        }

        // Store the generated GLB file
        var key = $"{model.ModelId}.glb";
        using var stream = new MemoryStream(glbData);
        var fileUrl = await _storage.UploadAsync(key, stream, "model/gltf-binary", cancellationToken);
        model.SetFilePath(key);

        _logger.LogInformation("Model generated: {ModelId} ({ByteCount:n0} bytes) at {FilePath}",
            model.ModelId, glbData.Length, key);
        return model;
    }

    public async Task<Model3D?> GetModel(Guid modelId, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        _logger.LogDebug("GetModel: {ModelId}", modelId);
        return null;
    }

    public async Task<Stream> GetModelFile(Guid modelId, CancellationToken cancellationToken = default)
    {
        var key = $"{modelId}.glb";
        try
        {
            return await _storage.DownloadAsync(key, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            throw new Liuvis.Core.Exceptions.ModelNotFoundException(modelId);
        }
    }

    private static ModelComponent CreateComponentFromTemplate(Guid modelId, ComponentSpec spec)
    {
        var component = new ModelComponent(modelId, spec.Name, spec.GeometryType);
        component.SetMaterial(spec.Material);
        component.SetTransform(new Transform3D
        {
            Position = Vector3.Zero,
            Rotation = Vector3.Zero,
            Scale = spec.GeometryType switch
            {
                "sphere" => new Vector3(1, 1, 1),
                "cylinder" => new Vector3(0.5, 1, 0.5),
                "box" => new Vector3(1, 1, 1),
                _ => Vector3.One
            }
        });
        return component;
    }

    /// <summary>Minimal GLB fallback — empty scene with no geometry.</summary>
    private static byte[] GenerateSimpleGlb(Model3D model)
    {
        using var ms = new MemoryStream();
        ms.Write(System.Text.Encoding.ASCII.GetBytes("glTF"));
        ms.Write(BitConverter.GetBytes((uint)2));
        var lengthPos = ms.Position;
        ms.Write(BitConverter.GetBytes((uint)0));

        var json = @"{""asset"":{""version"":""2.0"",""generator"":""Liuvis""},""scene"":0,""scenes"":[{""nodes"":[0]}],""nodes"":[{""name"":""root""}],""meshes"":[]}";
        var jsonBytes = System.Text.Encoding.UTF8.GetBytes(json);
        var pad = (4 - (jsonBytes.Length % 4)) % 4;
        var chunkLen = (uint)(jsonBytes.Length + pad);

        ms.Write(BitConverter.GetBytes(chunkLen));
        ms.Write(BitConverter.GetBytes((uint)0x4E4F534A));
        ms.Write(jsonBytes);
        for (int i = 0; i < pad; i++) ms.WriteByte(0x20);

        var total = (uint)ms.Length;
        ms.Seek(lengthPos, SeekOrigin.Begin);
        ms.Write(BitConverter.GetBytes(total));
        return ms.ToArray();
    }
}
