using Liuvis.Core.DTOs.Responses;
using Liuvis.Core.Exceptions;
using Liuvis.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Liuvis.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ModelController : ControllerBase
{
    private readonly IModelGenerator _generator;
    private readonly IObjectStorageService _storage;
    private readonly ILogger<ModelController> _logger;

    public ModelController(IModelGenerator generator, IObjectStorageService storage, ILogger<ModelController> logger)
    {
        _generator = generator;
        _storage = storage;
        _logger = logger;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<ModelResponse>>> GetModel(Guid id)
    {
        try
        {
            var model = await _generator.GetModel(id);
            if (model == null)
                return NotFound(ApiResponse<ModelResponse>.Error(404, "Model not found."));

            var fileUrl = await _storage.GetUrlAsync(model.FilePath);
            var thumbnailUrl = model.ThumbnailPath != null
                ? await _storage.GetUrlAsync(model.ThumbnailPath) : null;

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
}
