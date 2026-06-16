using Liuvis.Core.DTOs.Requests;
using Liuvis.Core.DTOs.Responses;
using Liuvis.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Liuvis.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class KnowledgeBaseController : ControllerBase
{
    private readonly IKnowledgeBaseService _kbService;
    private readonly ILogger<KnowledgeBaseController> _logger;

    public KnowledgeBaseController(IKnowledgeBaseService kbService, ILogger<KnowledgeBaseController> logger)
    {
        _kbService = kbService;
        _logger = logger;
    }

    [HttpPost("search")]
    public async Task<ActionResult<ApiResponse<List<object>>>> Search([FromBody] SearchModelRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            return BadRequest(ApiResponse<List<object>>.Error(400, "Query cannot be empty."));

        var results = await _kbService.SearchModels(request.Query, request.TopK);
        var data = results.Select(r => new
        {
            r.ModelId,
            r.Name,
            r.Similarity,
            r.MatchedComponents
        }).ToList();

        return Ok(ApiResponse<List<object>>.Ok(data.Cast<object>().ToList()));
    }

    [HttpGet("models/{id:guid}")]
    public async Task<ActionResult<ApiResponse<object>>> GetModel(Guid id)
    {
        var model = await _kbService.GetModel(id);
        if (model == null)
            return NotFound(ApiResponse<object>.Error(404, "Model not found."));

        var data = new
        {
            model.ModelId,
            model.Name,
            model.Description,
            ComponentCount = model.Components.Count,
            model.Tags,
            model.CreatedAt
        };

        return Ok(ApiResponse<object>.Ok(data));
    }

    [HttpPost("models/{id:guid}/tags")]
    public async Task<ActionResult<ApiResponse<bool>>> AddTags(Guid id, [FromBody] List<string> tags)
    {
        if (tags == null || tags.Count == 0)
            return BadRequest(ApiResponse<bool>.Error(400, "Tags cannot be empty."));

        await _kbService.AddTags(id, tags);
        return Ok(ApiResponse<bool>.Ok(true));
    }
}
