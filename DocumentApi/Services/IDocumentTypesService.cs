using System.Security.Claims;

namespace DocumentApi.Services;

public interface IDocumentTypesService
{
    Task<DocumentTypesServiceResult> GetAsync(
        Guid projectId,
        ClaimsPrincipal user,
        IReadOnlyDictionary<string, string?> headers,
        string? query,
        bool activeOnly,
        int limit,
        CancellationToken cancellationToken);
}
