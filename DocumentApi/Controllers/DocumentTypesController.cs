using System.Linq;
using DocumentApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DocumentApi.Controllers;

[ApiController]
[Route("api/v1/projects/{projectId:guid}/document-types")]
public class DocumentTypesController : ControllerBase
{
    private const int DefaultLimit = 100;

    private readonly IDocumentTypesService _documentTypesService;

    public DocumentTypesController(IDocumentTypesService documentTypesService)
    {
        _documentTypesService = documentTypesService;
    }

    [HttpGet]
    [Authorize(Policy = "proj.document.read")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(
        Guid projectId,
        [FromQuery] string? q,
        [FromQuery] bool activeOnly = true,
        [FromQuery] int limit = DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        var headers = Request.Headers.ToDictionary(static h => h.Key, static h => (string?)h.Value);
        var result = await _documentTypesService.GetAsync(projectId, User, headers, q, activeOnly, limit, cancellationToken);

        if (result.Error is not null)
        {
            return StatusCode(result.StatusCode, CreateError(result.Error.Error, result.Error.Message));
        }

        return StatusCode(result.StatusCode, new
        {
            items = result.Payload?.Items.Select(item => new
            {
                id = item.Id,
                code = item.Code,
                name = item.Name,
                is_active = item.IsActive,
                order = item.Order
            }),
            total = result.Payload?.Total ?? 0
        });
    }

    private object CreateError(string error, string message)
    {
        return new
        {
            error,
            message,
            traceId = HttpContext.TraceIdentifier
        };
    }
}
