using Npgsql;

namespace DocumentApi.Data;

public interface IDocumentTypesRepository
{
    Task<(IReadOnlyList<DocTypeDto> Items, int Total)> GetAsync(
        NpgsqlConnection connection,
        Guid projectId,
        string? q,
        bool activeOnly,
        int limit,
        CancellationToken cancellationToken);
}
