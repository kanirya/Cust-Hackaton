using System.Security.Claims;

namespace TaxNetGuardian.Api;

public static class AuthorizationCatalog
{
    private static readonly IReadOnlyList<PathAccessPolicy> PathPolicies =
    [
        new("/api/citizen", ["taxnet-citizen", "taxnet-support"]),
        new("/api/system/audit", ["taxnet-security-admin", "taxnet-admin"]),
        new("/api/system/object-store", ["taxnet-security-admin", "taxnet-admin"]),
        new("/api/system/model-gateway", ["taxnet-model-admin", "taxnet-policy-analyst", "taxnet-admin"]),
        new("/api/system/rag", ["taxnet-policy-analyst", "taxnet-model-admin", "taxnet-admin"]),
        new("/api/system", ["taxnet-admin", "taxnet-security-admin", "taxnet-model-admin", "taxnet-policy-analyst"]),
        new("/api/connectors", ["taxnet-admin", "taxnet-sandbox-admin", "taxnet-data-engineer"]),
        new("/api/sandbox", ["taxnet-admin", "taxnet-sandbox-admin", "taxnet-data-engineer"]),
        new("/sandbox/admin", ["taxnet-admin", "taxnet-sandbox-admin", "taxnet-data-engineer"]),
        new("/sandbox/nadra", ["taxnet-admin", "taxnet-sandbox-admin", "taxnet-data-engineer"]),
        new("/sandbox/fbr", ["taxnet-admin", "taxnet-sandbox-admin", "taxnet-data-engineer"]),
        new("/sandbox/excise", ["taxnet-admin", "taxnet-sandbox-admin", "taxnet-data-engineer"]),
        new("/sandbox/secp", ["taxnet-admin", "taxnet-sandbox-admin", "taxnet-data-engineer"]),
        new("/sandbox/property", ["taxnet-admin", "taxnet-sandbox-admin", "taxnet-data-engineer"]),
        new("/sandbox/utilities", ["taxnet-admin", "taxnet-sandbox-admin", "taxnet-data-engineer"]),
        new("/sandbox/travel", ["taxnet-admin", "taxnet-sandbox-admin", "taxnet-data-engineer"]),
        new("/api/cases", ["taxnet-admin", "taxnet-auditor", "taxnet-senior-auditor", "taxnet-supervisor"]),
        new("/api/graph", ["taxnet-admin", "taxnet-auditor", "taxnet-senior-auditor"]),
        new("/api/assistant", ["taxnet-admin", "taxnet-auditor", "taxnet-senior-auditor", "taxnet-policy-analyst"]),
        new("/api/reports", ["taxnet-admin", "taxnet-auditor", "taxnet-senior-auditor"]),
        new("/api/ingestion", ["taxnet-admin", "taxnet-data-engineer", "taxnet-sandbox-admin"]),
        new("/api/identity", ["taxnet-admin", "taxnet-security-admin", "taxnet-data-engineer"])
    ];

    public static IReadOnlyList<AuthzRole> Roles { get; } =
    [
        new("taxnet-admin", "Full platform administration with provider, case, and dashboard access.", ["taxnet/dashboard.read", "taxnet/cases.read", "taxnet/cases.write", "taxnet/sandbox.admin", "taxnet/models.manage", "taxnet/audit.read"], ["/dashboard", "/cases", "/sandbox", "/models", "/audit"]),
        new("taxnet-security-admin", "Security, audit, policy enforcement, and authorization review.", ["taxnet/audit.read", "taxnet/cases.read"], ["/audit", "/dashboard"]),
        new("taxnet-sandbox-admin", "Manages synthetic data providers and failure simulation.", ["taxnet/sandbox.admin"], ["/sandbox"]),
        new("taxnet-data-engineer", "Runs imports, validates schemas, and reviews ingestion quality.", ["taxnet/ingestion.write", "taxnet/dashboard.read"], ["/dashboard", "/sandbox/real-provider-readiness"]),
        new("taxnet-auditor", "Reviews assigned cases, evidence, graph, explanations, and reports.", ["taxnet/cases.read", "taxnet/cases.review", "taxnet/graph.read", "taxnet/reports.generate"], ["/cases", "/cases/:caseId"]),
        new("taxnet-senior-auditor", "Approves escalations and closes major cases.", ["taxnet/cases.read", "taxnet/cases.write", "taxnet/cases.review", "taxnet/graph.read", "taxnet/reports.generate"], ["/dashboard", "/cases"]),
        new("taxnet-supervisor", "Monitors regional workload and assigns cases.", ["taxnet/dashboard.read", "taxnet/cases.read", "taxnet/cases.write"], ["/dashboard", "/cases"]),
        new("taxnet-policy-analyst", "Manages RAG policy documents and citation quality.", ["taxnet/policy.manage"], ["/policy-search"]),
        new("taxnet-model-admin", "Manages model routing, model evaluation, and prompt policy.", ["taxnet/models.manage"], ["/models"]),
        new("taxnet-readonly-analyst", "Views aggregate dashboards and anonymized case analytics.", ["taxnet/dashboard.read"], ["/dashboard"]),
        new("taxnet-citizen", "Views own safe profile and submits corrections.", ["taxnet/citizen.read_self", "taxnet/citizen.submit_correction"], ["/citizen"]),
        new("taxnet-support", "Supports citizens without access to investigative internals.", ["taxnet/citizen.read_self"], ["/citizen/support"])
    ];

    public static bool HasRole(HttpContext context, params string[] allowedRoles)
    {
        var roles = GetCurrentRoles(context);
        return roles.Any(role =>
            allowedRoles.Contains(role, StringComparer.OrdinalIgnoreCase) ||
            role.Equals("taxnet-admin", StringComparison.OrdinalIgnoreCase));
    }

    public static string GetCurrentRole(HttpContext context)
    {
        var roles = GetCurrentRoles(context);
        return roles.FirstOrDefault() ?? "taxnet-anonymous";
    }

    public static IReadOnlyList<string> GetCurrentRoles(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var roles = context.User.Claims
                .Where(IsRoleClaim)
                .SelectMany(SplitClaimValue)
                .Where(x => x.StartsWith("taxnet-", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (roles.Length > 0)
            {
                return roles;
            }
        }

        if (context.Request.Headers.TryGetValue("X-Demo-Role", out var role) && !string.IsNullOrWhiteSpace(role))
        {
            return SplitHeaderRoles(role.ToString());
        }

        return ["taxnet-admin"];
    }

    public static string GetCurrentActor(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            return context.User.FindFirstValue("sub")
                ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? context.User.Identity.Name
                ?? GetCurrentRole(context);
        }

        return context.Request.Headers.TryGetValue("X-Demo-User", out var user) && !string.IsNullOrWhiteSpace(user)
            ? user.ToString()
            : GetCurrentRole(context);
    }

    public static object CurrentUser(HttpContext context)
    {
        var role = GetCurrentRole(context);
        var user = GetCurrentActor(context);
        return new
        {
            userId = user,
            role,
            roles = GetCurrentRoles(context),
            mode = context.User.Identity?.IsAuthenticated == true ? "JWT claims" : "Development header auth",
            scopes = GetCurrentScopes(context)
        };
    }

    public static bool TryGetAccessDecision(HttpContext context, out AccessDecision decision)
    {
        var path = context.Request.Path.Value ?? "";
        var policy = PathPolicies
            .Where(x => path.StartsWith(x.PathPrefix, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.PathPrefix.Length)
            .FirstOrDefault();

        if (policy is null)
        {
            decision = new AccessDecision(true, GetCurrentRole(context), [], path);
            return false;
        }

        var roles = GetCurrentRoles(context);
        var role = roles.FirstOrDefault() ?? "taxnet-anonymous";
        var allowed = roles.Any(current =>
            policy.AllowedRoles.Contains(current, StringComparer.OrdinalIgnoreCase) ||
            current.Equals("taxnet-admin", StringComparison.OrdinalIgnoreCase));
        decision = new AccessDecision(allowed, role, policy.AllowedRoles, path);
        return true;
    }

    public static IReadOnlyList<string> GetCurrentScopes(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            return context.User.Claims
                .Where(x => x.Type is "scope" or "scp" or "client_scope")
                .SelectMany(SplitClaimValue)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        var role = GetCurrentRole(context);
        return Roles.FirstOrDefault(x => x.Role.Equals(role, StringComparison.OrdinalIgnoreCase))?.Scopes ?? [];
    }

    private static bool IsRoleClaim(Claim claim)
        => claim.Type is ClaimTypes.Role or "role" or "roles" or "cognito:groups" or "groups";

    private static IReadOnlyList<string> SplitHeaderRoles(string value)
        => value.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static IEnumerable<string> SplitClaimValue(Claim claim)
        => claim.Value.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
