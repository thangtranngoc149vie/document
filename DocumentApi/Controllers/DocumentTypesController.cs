using System.Data;
using System.Linq;
using Dapper;
using DocumentApi.Data;
using DocumentApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Serilog.Context;

namespace DocumentApi.Controllers;

[ApiController]
[Route("api/v1/projects/{projectId:guid}/document-types")]
public class DocumentTypesController : ControllerBase
{
    private const int DefaultLimit = 100;
    private const int MaxLimit = 500;

    private readonly NpgsqlConnection _connection;
    private readonly IDocumentTypesRepository _repository;
    private readonly IProjectAccessEvaluator _accessEvaluator;
    private readonly ILogger<DocumentTypesController> _logger;

    public DocumentTypesController(
        NpgsqlConnection connection,
        IDocumentTypesRepository repository,
        IProjectAccessEvaluator accessEvaluator,
        ILogger<DocumentTypesController> logger)
    {
        _connection = connection;
        _repository = repository;
        _accessEvaluator = accessEvaluator;
        _logger = logger;
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
        limit = Math.Clamp(limit, 1, MaxLimit);
        await EnsureConnectionOpenAsync(cancellationToken);

        var projectOrgId = await GetProjectOrgIdAsync(projectId, cancellationToken);
        if (projectOrgId is null)
        {
            return StatusCode(StatusCodes.Status404NotFound, CreateError("not_found", "Project not found."));
        }

        if (!_accessEvaluator.HasAccess(User, projectId, projectOrgId))
        {
            return StatusCode(StatusCodes.Status403Forbidden, CreateError("forbidden", "You don't have access to this project."));
        }

        using (LogContext.PushProperty("UserId", User.FindFirst("sub")?.Value ?? "unknown"))
        using (LogContext.PushProperty("ProjectId", projectId))
        using (LogContext.PushProperty("Endpoint", "GET /document-types"))
        {
            var (items, total) = await _repository.GetAsync(_connection, projectId, q, activeOnly, limit, cancellationToken);

            var safeItems = items.Select(item => new
            {
                id = item.Id,
                code = Sanitize(item.Code),
                name = Sanitize(item.Name),
                is_active = item.IsActive,
                order = item.Order
            });

            _logger.LogInformation("Returned {Count} document types for project {ProjectId}", items.Count, projectId);
            return Ok(new { items = safeItems, total });
        }
    }

    private async Task<Guid?> GetProjectOrgIdAsync(Guid projectId, CancellationToken cancellationToken)
    {
        const string sql = "SELECT org_id FROM projects WHERE id = @projectId";
        var command = new CommandDefinition(sql, new { projectId }, cancellationToken: cancellationToken);
        return await _connection.ExecuteScalarAsync<Guid?>(command);
    }

    private async Task EnsureConnectionOpenAsync(CancellationToken cancellationToken)
    {
        if (_connection.State != ConnectionState.Open)
        {
            await _connection.OpenAsync(cancellationToken);
        }
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

    private static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var cleaned = value
            .Replace("<", string.Empty, StringComparison.Ordinal)
            .Replace(">", string.Empty, StringComparison.Ordinal)
            .Trim();

        return cleaned.Length > 255 ? cleaned[..255] : cleaned;
    }
}
