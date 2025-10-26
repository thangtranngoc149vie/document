using System.Data;
using Dapper;
using Npgsql;

namespace DocumentApi.Data;

public sealed class ProjectRepository : IProjectRepository
{
    private const string Sql = "SELECT org_id FROM projects WHERE id = @projectId";

    public async Task<Guid?> GetOrgIdAsync(NpgsqlConnection connection, Guid projectId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var command = new CommandDefinition(Sql, new { projectId }, cancellationToken: cancellationToken);
        return await connection.ExecuteScalarAsync<Guid?>(command);
    }
}
