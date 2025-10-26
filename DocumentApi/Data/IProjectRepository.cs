using Npgsql;

namespace DocumentApi.Data;

public interface IProjectRepository
{
    Task<Guid?> GetOrgIdAsync(NpgsqlConnection connection, Guid projectId, CancellationToken cancellationToken);
}
