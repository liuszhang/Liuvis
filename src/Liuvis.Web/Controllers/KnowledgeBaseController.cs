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

    [HttpPost("models")]
    public async Task<ActionResult<ApiResponse<Guid>>> SaveModel()
    {
        return BadRequest(ApiResponse<Guid>.Error(400, "Model saving from API not yet implemented."));
    }
}
