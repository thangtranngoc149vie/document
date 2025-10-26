using System.Linq;
using System.Security.Claims;
using DocumentApi.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Npgsql;
using Serilog.Context;

namespace DocumentApi.Services;

public sealed class DocumentTypesService : IDocumentTypesService
{
    private const int DefaultLimit = 100;
    private const int MaxLimit = 500;

    private readonly NpgsqlConnection _connection;
    private readonly IDocumentTypesRepository _documentTypesRepository;
    private readonly IProjectRepository _projectRepository;
    private readonly IProjectAccessEvaluator _accessEvaluator;
    private readonly ILogger<DocumentTypesService> _logger;

    public DocumentTypesService(
        NpgsqlConnection connection,
        IDocumentTypesRepository documentTypesRepository,
        IProjectRepository projectRepository,
        IProjectAccessEvaluator accessEvaluator,
        ILogger<DocumentTypesService> logger)
    {
        _connection = connection;
        _documentTypesRepository = documentTypesRepository;
        _projectRepository = projectRepository;
        _accessEvaluator = accessEvaluator;
        _logger = logger;
    }

    public async Task<DocumentTypesServiceResult> GetAsync(
        Guid projectId,
        ClaimsPrincipal user,
        IReadOnlyDictionary<string, string?> headers,
        string? query,
        bool activeOnly,
        int limit,
        CancellationToken cancellationToken)
    {
        var normalizedLimit = Math.Clamp(limit <= 0 ? DefaultLimit : limit, 1, MaxLimit);
        var sanitizedQuery = string.IsNullOrWhiteSpace(query) ? null : query.Trim();

        var principal = user ?? new ClaimsPrincipal(new ClaimsIdentity());

        using var endpointScope = LogContext.PushProperty("Endpoint", "GET /document-types");
        using var projectScope = LogContext.PushProperty("ProjectId", projectId);
        using var userScope = LogContext.PushProperty("UserId", principal.FindFirst("sub")?.Value ?? "unknown");
        using var requestScope = PushRequestId(headers);

        var projectOrgId = await _projectRepository.GetOrgIdAsync(_connection, projectId, cancellationToken);
        if (projectOrgId is null)
        {
            _logger.LogWarning("Project {ProjectId} not found when listing document types", projectId);
            return DocumentTypesServiceResult.NotFound("not_found", "Project not found.");
        }

        if (!_accessEvaluator.HasAccess(principal, projectId, projectOrgId))
        {
            _logger.LogWarning("User {UserId} denied access to project {ProjectId}", principal.FindFirst("sub")?.Value ?? "unknown", projectId);
            return DocumentTypesServiceResult.Forbidden("forbidden", "You don't have access to this project.");
        }

        var (items, total) = await _documentTypesRepository.GetAsync(
            _connection,
            projectId,
            sanitizedQuery,
            activeOnly,
            normalizedLimit,
            cancellationToken);

        var sanitizedItems = items
            .Select(item => new DocumentTypeItem(
                item.Id,
                Sanitize(item.Code),
                Sanitize(item.Name),
                item.IsActive,
                item.Order))
            .ToList();

        _logger.LogInformation(
            "Returned {Count} document types for project {ProjectId}",
            sanitizedItems.Count,
            projectId);

        return DocumentTypesServiceResult.Success(sanitizedItems, total);
    }

    private static IDisposable? PushRequestId(IReadOnlyDictionary<string, string?> headers)
    {
        foreach (var key in new[] { "X-Request-Id", "X-Correlation-Id" })
        {
            if (headers.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return LogContext.PushProperty("RequestId", value);
            }
        }

        return null;
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

public sealed record DocumentTypesServiceResult(int StatusCode, DocumentTypesPayload? Payload, ServiceError? Error)
{
    public static DocumentTypesServiceResult Success(IReadOnlyList<DocumentTypeItem> items, int total) =>
        new(StatusCodes.Status200OK, new DocumentTypesPayload(items, total), null);

    public static DocumentTypesServiceResult NotFound(string error, string message) =>
        new(StatusCodes.Status404NotFound, null, new ServiceError(error, message));

    public static DocumentTypesServiceResult Forbidden(string error, string message) =>
        new(StatusCodes.Status403Forbidden, null, new ServiceError(error, message));
}

public sealed record DocumentTypesPayload(IReadOnlyList<DocumentTypeItem> Items, int Total);

public sealed record DocumentTypeItem(Guid Id, string Code, string Name, bool IsActive, int Order);

public sealed record ServiceError(string Error, string Message);
