using System.Text.Json;
using CJCore.AgentTool.MCPTools;
using CJCore.CoreLog;
using Liuvis.Core.Entities;
using Liuvis.Core.Enums;
using Liuvis.Core.Interfaces;
using Liuvis.Core.ValueObjects;
using Liuvis.Generation.Geometry;
using Liuvis.Generation.Services;
using Liuvis.Infrastructure.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace Liuvis.Agent.Tools;

/// <summary>
/// 阶段四：模型导出工具（危险操作）。
/// 触发几何构建 → STEP/STL/GLB 导出流水线，将 LLM 生成的场景定义转化为物理模型文件。
/// 标记为危险操作，需经审批流程确认后才执行。
/// </summary>
/// <remarks>
/// 生命周期说明：工具池（DefaultToolRegistry）为 Singleton，而
/// ProceduralGeometryBuilder/StepExporter/ModelRepository/IObjectStorageService 均为
/// Scoped，因此本工具注入 IServiceScopeFactory，在每次执行时创建临时 scope 解析，
/// 避免 Singleton 依赖 Scoped 的 DI 生命周期错误（与 KnowledgeQueryTool 同模式）。
/// </remarks>
public class ModelExportTool : IToolExecutor
{
    private readonly IServiceScopeFactory _scopeFactory;

    public ModelExportTool(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public string Name => "model_export";
    public string Description => "将 SceneDescription 导出为 STEP/STL/GLB 三维模型文件。此操作会调用几何构建引擎生成实际模型文件，属危险操作，需二次确认。";
    public bool IsDangerous => true;
    public bool Enabled { get; set; } = true;

    public string ParametersJson => """
    {
      "type": "object",
      "properties": {
        "scene_json": {
          "type": "object",
          "description": "SceneDescription，含 objects 数组及每个对象的 type/size/position/color/material"
        },
        "model_name": {
          "type": "string",
          "description": "模型名称，用于生成文件名"
        },
        "output_format": {
          "type": "string",
          "enum": ["step", "stl", "glb"],
          "description": "输出格式，默认 step",
          "default": "step"
        }
      },
      "required": ["scene_json", "model_name"]
    }
    """;

    public async Task<object?> ExecuteAsync(
        string argumentsJson,
        ToolExecutionContext ctx,
        CancellationToken ct = default)
    {
        CJLog.Information(
            $"ModelExportTool.ExecuteAsync: args={argumentsJson[..Math.Min(argumentsJson.Length, 300)]}",
            source: "ModelExportTool");

        using var doc = JsonDocument.Parse(argumentsJson);
        var root = doc.RootElement;

        if (!root.TryGetProperty("model_name", out var mn))
            return new { error = "缺少 model_name 参数" };

        var modelName = mn.GetString() ?? "unnamed";
        var format = root.TryGetProperty("output_format", out var of)
            ? of.GetString() ?? "step" : "step";

        if (!root.TryGetProperty("scene_json", out var sceneJson))
            return new { error = "缺少 scene_json 参数" };

        SceneDescription scene;
        try
        {
            var jsonOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            scene = sceneJson.Deserialize<SceneDescription>(jsonOpts) ?? new SceneDescription();
        }
        catch (Exception ex)
        {
            CJLog.Warning($"ModelExportTool: scene_json 解析失败: {ex.Message}", source: "ModelExportTool");
            return new { error = "scene_json 解析失败", detail = ex.Message };
        }

        if (scene.Objects.Count == 0)
            return new { error = "scene_json 中不含 objects" };

        try
        {
            return await ExportModel(modelName, format, scene, ct);
        }
        catch (Exception ex)
        {
            CJLog.Error(ex, "ModelExportTool: 导出失败", source: "ModelExportTool");
            return new { error = "导出失败", detail = ex.Message };
        }
    }

    /// <summary>
    /// 在临时 scope 中解析 Scoped 的生成/持久化服务，按格式调用构建器生成模型并落库。
    /// </summary>
    private async Task<object> ExportModel(string modelName, string format, SceneDescription scene, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var geometryBuilder = sp.GetRequiredService<ProceduralGeometryBuilder>();
        var stepExporter = sp.GetRequiredService<StepExporter>();
        var repository = sp.GetRequiredService<ModelRepository>();
        var storage = sp.GetRequiredService<IObjectStorageService>();

        var (bytes, contentType, extension, modelFormat) = format.ToLowerInvariant() switch
        {
            "stl" => (geometryBuilder.BuildStl(scene), "model/stl", "stl", ModelFormat.STL),
            "glb" => (geometryBuilder.BuildGlb(scene), "model/gltf-binary", "glb", ModelFormat.GLB),
            _ => (stepExporter.Export(scene), "application/step", "step", ModelFormat.STEP)
        };

        var model = new Model3D(
            $"{modelName}_{DateTime.UtcNow:yyyyMMddHHmmss}",
            $"Exported by ModelExportTool ({format})",
            modelFormat);

        model.Metadata["_scene"] = JsonSerializer.Serialize(scene, new JsonSerializerOptions { WriteIndented = false });

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

        var key = $"{model.ModelId}.{extension}";
        using var stream = new MemoryStream(bytes);
        var fileUrl = await storage.UploadAsync(key, stream, contentType, ct);
        model.SetFilePath(key);

        await repository.CreateAsync(model, ct);

        CJLog.Information(
            $"ModelExportTool: 导出成功 model_id={model.ModelId}, format={modelFormat}, size={bytes.Length} bytes, url={fileUrl}",
            source: "ModelExportTool");

        return new
        {
            status = "success",
            model_id = model.ModelId,
            model_name = modelName,
            output_format = format,
            size_bytes = bytes.Length,
            file_url = fileUrl
        };
    }
}
