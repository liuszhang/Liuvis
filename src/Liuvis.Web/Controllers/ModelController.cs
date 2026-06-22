using Liuvis.Core.DTOs.Responses;
using Liuvis.Core.Exceptions;
using Liuvis.Core.Interfaces;
using Liuvis.Infrastructure.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Liuvis.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ModelController : ControllerBase
{
    private readonly IModelGenerator _generator;
    private readonly IObjectStorageService _storage;
    private readonly ModelRepository _modelRepo;
    private readonly ILogger<ModelController> _logger;

    public ModelController(
        IModelGenerator generator,
        IObjectStorageService storage,
        ModelRepository modelRepo,
        ILogger<ModelController> logger)
    {
        _generator = generator;
        _storage = storage;
        _modelRepo = modelRepo;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<ModelResponse>>>> ListModels(
        [FromQuery] int limit = 100)
    {
        var models = await _modelRepo.GetAllAsync(limit);
        var responses = models.Select(m => new ModelResponse
        {
            ModelId = m.ModelId,
            Name = m.Name,
            Description = m.Description,
            Format = m.Format,
            FileUrl = $"/storage/{m.FilePath}",
            ThumbnailUrl = m.ThumbnailPath != null ? $"/storage/{m.ThumbnailPath}" : null,
            ComponentCount = m.Components.Count,
            Version = m.Version,
            Tags = m.Tags,
            CreatedAt = m.CreatedAt
        }).ToList();

        return Ok(ApiResponse<List<ModelResponse>>.Ok(responses));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<ModelResponse>>> GetModel(Guid id)
    {
        try
        {
            var model = await _generator.GetModel(id);
            if (model == null)
                return NotFound(ApiResponse<ModelResponse>.Error(404, "Model not found."));

            var fileUrl = $"/storage/{model.FilePath}";
            var thumbnailUrl = model.ThumbnailPath != null
                ? $"/storage/{model.ThumbnailPath}" : null;

            var response = new ModelResponse
            {
                ModelId = model.ModelId,
                Name = model.Name,
                Description = model.Description,
                Format = model.Format,
                FileUrl = fileUrl,
                ThumbnailUrl = thumbnailUrl,
                ComponentCount = model.Components.Count,
                Version = model.Version,
                Tags = model.Tags,
                CreatedAt = model.CreatedAt
            };

            return Ok(ApiResponse<ModelResponse>.Ok(response));
        }
        catch (ModelNotFoundException)
        {
            return NotFound(ApiResponse<ModelResponse>.Error(404, "Model not found."));
        }
    }

    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> DownloadModel(Guid id)
    {
        try
        {
            var stream = await _generator.GetModelFile(id);
            return File(stream, "model/gltf-binary", $"{id}.glb");
        }
        catch (ModelNotFoundException)
        {
            return NotFound(ApiResponse<ModelResponse>.Error(404, "Model not found."));
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteModel(Guid id)
    {
        try
        {
            var model = await _generator.GetModel(id);
            if (model == null)
                return NotFound();

            // Delete file from storage
            if (!string.IsNullOrEmpty(model.FilePath))
            {
                try { await _storage.DeleteAsync(model.FilePath); }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete storage file for model {Id}", id); }
            }

            // Delete from database
            await _modelRepo.DeleteAsync(id);
            return NoContent();
        }
        catch (ModelNotFoundException)
        {
            return NotFound();
        }
    }
}
