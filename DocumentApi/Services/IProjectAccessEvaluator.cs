using System.Security.Claims;

namespace DocumentApi.Services;

public interface IProjectAccessEvaluator
{
    bool HasAccess(ClaimsPrincipal user, Guid projectId, Guid? projectOrgId);
}
