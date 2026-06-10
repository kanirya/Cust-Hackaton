namespace TaxNetGuardian.Api;

public static class AuthorizationCatalog
{
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
        var role = GetCurrentRole(context);
        return allowedRoles.Contains(role, StringComparer.OrdinalIgnoreCase) ||
               role.Equals("taxnet-admin", StringComparison.OrdinalIgnoreCase);
    }

    public static string GetCurrentRole(HttpContext context)
        => context.Request.Headers.TryGetValue("X-Demo-Role", out var role) && !string.IsNullOrWhiteSpace(role)
            ? role.ToString()
            : "taxnet-admin";

    public static string GetCurrentActor(HttpContext context)
        => context.Request.Headers.TryGetValue("X-Demo-User", out var user) && !string.IsNullOrWhiteSpace(user)
            ? user.ToString()
            : GetCurrentRole(context);

    public static object CurrentUser(HttpContext context)
    {
        var role = GetCurrentRole(context);
        var user = GetCurrentActor(context);
        return new
        {
            userId = user,
            role,
            mode = "Development header auth; production target is Cognito JWT + OAuth scopes.",
            scopes = Roles.FirstOrDefault(x => x.Role.Equals(role, StringComparison.OrdinalIgnoreCase))?.Scopes ?? []
        };
    }
}
