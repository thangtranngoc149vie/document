using System.Data;
using Dapper;
using Npgsql;

namespace DocumentApi.Data;

public sealed class DocumentTypesRepository : IDocumentTypesRepository
{
    private const string Sql = @"WITH proj AS (
  SELECT p.org_id FROM projects p WHERE p.id = @projectId
)
SELECT dt.id AS ""Id"", dt.code AS ""Code"", dt.name AS ""Name"", dt.is_active AS ""IsActive"",
       COALESCE(dt.""order"", dt.sort_order, 0) AS ""Order""
FROM document_types dt, proj
WHERE (dt.scope IS NULL OR dt.scope = 'project')
  AND (dt.org_id IS NULL OR dt.org_id = proj.org_id)
  AND (@activeOnly = FALSE OR dt.is_active = TRUE)
  AND (@q IS NULL OR @q = ''
       OR unaccent(dt.code) ILIKE unaccent('%' || @q || '%')
       OR unaccent(dt.name) ILIKE unaccent('%' || @q || '%'))
ORDER BY COALESCE(dt.""order"", dt.sort_order, 0) NULLS FIRST, dt.name
LIMIT @limit;
SELECT COUNT(1)
FROM document_types dt, proj
WHERE (dt.scope IS NULL OR dt.scope = 'project')
  AND (dt.org_id IS NULL OR dt.org_id = proj.org_id)
  AND (@activeOnly = FALSE OR dt.is_active = TRUE)
  AND (@q IS NULL OR @q = ''
       OR unaccent(dt.code) ILIKE unaccent('%' || @q || '%')
       OR unaccent(dt.name) ILIKE unaccent('%' || @q || '%'));";

    public async Task<(IReadOnlyList<DocTypeDto> Items, int Total)> GetAsync(
        NpgsqlConnection connection,
        Guid projectId,
        string? q,
        bool activeOnly,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var search = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
        var command = new CommandDefinition(
            Sql,
            new { projectId, q = search, activeOnly, limit },
            cancellationToken: cancellationToken);

        using var multi = await connection.QueryMultipleAsync(command);
        var items = (await multi.ReadAsync<DocTypeDto>()).AsList();
        var total = await multi.ReadSingleAsync<int>();
        return (items, total);
    }
}
