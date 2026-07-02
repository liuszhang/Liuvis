using Liuvis.Core.DTOs.Responses;
using Liuvis.Core.Entities;
using Liuvis.Core.Interfaces;
using Liuvis.Generation.Importers;
using Liuvis.Infrastructure.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Liuvis.Web.Controllers;

/// <summary>
/// API controller for importing 3D model files (STL, etc.)
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ImportController : ControllerBase
{
    private readonly StlImporter _stlImporter;
    private readonly IObjectStorageService _storage;
    private readonly ModelRepository _modelRepo;
    private readonly ILogger<ImportController> _logger;

    public ImportController(
        StlImporter stlImporter,
        IObjectStorageService storage,
        ModelRepository modelRepo,
        ILogger<ImportController> logger)
    {
        _stlImporter = stlImporter;
        _storage = storage;
        _modelRepo = modelRepo;
        _logger = logger;
    }

    /// <summary>
    /// Upload and import an STL file.
    /// Returns model metadata including ModelId for subsequent operations.
    /// </summary>
    /// <param name="file">STL file (binary or ASCII format)</param>
    /// <param name="name">Optional model name (defaults to filename)</param>
    /// <returns>ModelResponse with ModelId and metadata</returns>
    [HttpPost("stl")]
    [RequestSizeLimit(100 * 1024 * 1024)] // 100 MB limit
    public async Task<ActionResult<ApiResponse<ModelResponse>>> ImportStl(IFormFile file, [FromQuery] string? name = null)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(ApiResponse<ModelResponse>.Error(400, "No file uploaded."));
        }

        // Validate file extension
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension != ".stl")
        {
            return BadRequest(ApiResponse<ModelResponse>.Error(400, "Only STL files are supported."));
        }

        try
        {
            _logger.LogInformation("Importing STL file: {FileName}, Size: {Size} bytes", file.FileName, file.Length);

            // Parse STL file
            using var stream = file.OpenReadStream();
            var modelName = name ?? Path.GetFileNameWithoutExtension(file.FileName);
            var model = await _stlImporter.ImportAsync(stream, modelName);

            // Save model to storage (serialize to GLB or keep as STL)
            var storagePath = $"models/{model.ModelId}/original.stl";
            using (var fileStream = file.OpenReadStream())
            {
                var fileUrl = await _storage.UploadAsync(storagePath, fileStream, "model/stl");
            }

            // Update model file path
            model.SetFilePath(storagePath);

            // Save model metadata to database
            await _modelRepo.CreateAsync(model);

            _logger.LogInformation("STL imported successfully. ModelId: {ModelId}", model.ModelId);

            // Build response
            var componentTriangleCounts = new List<int>();
            for (int i = 0; i < model.Components.Count; i++)
            {
                if (model.Metadata.TryGetValue($"Component_{i}_TriangleCount", out var tcStr) &&
                    int.TryParse(tcStr, out var tc))
                    componentTriangleCounts.Add(tc);
            }

            var response = new ModelResponse
            {
                ModelId = model.ModelId,
                Name = model.Name,
                Description = $"Imported STL file: {file.FileName}",
                Format = model.Format,
                FileUrl = $"/storage/{storagePath}",
                ComponentCount = model.Components.Count,
                ComponentTriangleCounts = componentTriangleCounts,
                Version = model.Version,
                Tags = new List<string> { "imported", "stl" },
                CreatedAt = model.CreatedAt
            };

            return Ok(ApiResponse<ModelResponse>.Ok(response, "STL file imported successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import STL file: {FileName}", file.FileName);
            return StatusCode(500, ApiResponse<ModelResponse>.Error(500, $"Import failed: {ex.Message}"));
        }
    }

    /// <summary>
    /// Get import capabilities and supported formats.
    /// </summary>
    [HttpGet("capabilities")]
    public ActionResult<ApiResponse<ImportCapabilitiesResponse>> GetCapabilities()
    {
        var capabilities = new ImportCapabilitiesResponse
        {
            SupportedFormats = new List<string> { "stl" },
            MaxFileSizeBytes = 100 * 1024 * 1024, // 100 MB
            Features = new List<string>
            {
                "binary-stl",
                "ascii-stl",
                "component-extraction"
            }
        };

        return Ok(ApiResponse<ImportCapabilitiesResponse>.Ok(capabilities));
    }
}

/// <summary>
/// Response DTO for import capabilities.
/// </summary>
public class ImportCapabilitiesResponse
{
    public List<string> SupportedFormats { get; set; } = new();
    public long MaxFileSizeBytes { get; set; }
    public List<string> Features { get; set; } = new();
}
