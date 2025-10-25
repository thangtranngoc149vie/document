using System.Collections.Generic;
using System.Security.Claims;

namespace DocumentApi.Services;

public sealed class ProjectAccessEvaluator : IProjectAccessEvaluator
{
    private static readonly string[] ProjectClaimTypes =
    {
        "project_id",
        "project",
        "projects",
        "project_ids",
        "projectIds"
    };

    private static readonly string[] OrgClaimTypes =
    {
        "org_id",
        "organisation_id",
        "organization_id",
        "org",
        "orgs",
        "org_ids",
        "organization",
        "organization_ids"
    };

    public bool HasAccess(ClaimsPrincipal user, Guid projectId, Guid? projectOrgId)
    {
        if (user?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var projectIds = ExtractGuids(user, ProjectClaimTypes);
        if (projectIds.Contains(projectId))
        {
            return true;
        }

        if (projectOrgId is null)
        {
            return false;
        }

        var orgIds = ExtractGuids(user, OrgClaimTypes);
        return orgIds.Contains(projectOrgId.Value);
    }

    private static HashSet<Guid> ExtractGuids(ClaimsPrincipal user, IEnumerable<string> claimTypes)
    {
        var results = new HashSet<Guid>();
        foreach (var claimType in claimTypes)
        {
            foreach (var claim in user.FindAll(claimType))
            {
                foreach (var value in SplitValues(claim.Value))
                {
                    if (Guid.TryParse(value, out var parsed))
                    {
                        results.Add(parsed);
                    }
                }
            }
        }

        return results;
    }

    private static IEnumerable<string> SplitValues(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            yield break;
        }

        var segments = raw.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var segment in segments)
        {
            yield return segment;
        }
    }
}
