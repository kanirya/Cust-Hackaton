namespace TaxNetGuardian.Api;

public sealed record ProductionReadinessReport(
    string Status,
    string Environment,
    string AuthMode,
    IReadOnlyList<ReadinessCheck> Checks,
    DateTimeOffset CheckedAtUtc);

public sealed record ReadinessCheck(
    string Name,
    string Status,
    string Detail,
    bool RequiredForProduction);

public static class ProductionReadiness
{
    public static ProductionReadinessReport Build(
        TaxNetState state,
        TaxNetPlatformOptions options,
        ModelGatewayConfig modelGateway,
        IReadOnlyList<ModelSecretDiagnostic> secretDiagnostics)
    {
        var checks = new List<ReadinessCheck>
        {
            Check("auth.mode", options.Auth.RequireJwt ? "Ready" : "DevelopmentOnly", options.Auth.Mode, true),
            Check("auth.authority", !options.Auth.RequireJwt || !string.IsNullOrWhiteSpace(options.Auth.Authority) ? "Ready" : "Missing", options.Auth.Authority, true),
            Check("auth.audience", !options.Auth.RequireJwt || !string.IsNullOrWhiteSpace(options.Auth.Audience) ? "Ready" : "Missing", options.Auth.Audience, true),
            Check("persistence.operational", options.Storage.OperationalStore.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase) ? "Ready" : "MvpFallback", options.Storage.OperationalStore, true),
            Check("persistence.graph", options.Storage.GraphStore is "Neo4j" or "Memgraph" or "Neptune" ? "Ready" : "MvpFallback", options.Storage.GraphStore, false),
            Check("persistence.vector", options.Storage.VectorStore is "Qdrant" or "PgVector" or "OpenSearchVector" ? "Ready" : "MvpFallback", options.Storage.VectorStore, true),
            Check("rag.index", state.RagChunks.Count > 0 ? "Ready" : "Missing", $"{state.RagChunks.Count} chunks indexed", true),
            Check("cases.seeded", state.Cases.Count > 0 ? "Ready" : "Missing", $"{state.Cases.Count} cases available", false),
            Check("model.gateway", modelGateway.Providers.Any(x => x.HasApiKey && x.Provider != "deterministic-template") ? "ExternalReady" : "FallbackOnly", "External providers with keys: " + string.Join(", ", modelGateway.Providers.Where(x => x.HasApiKey && x.Provider != "deterministic-template").Select(x => x.Provider)), false),
            Check("secrets.manager", secretDiagnostics.All(x => x.SecretReadable) ? "Ready" : "Partial", $"{secretDiagnostics.Count(x => x.SecretReadable)}/{secretDiagnostics.Count} model secrets readable", true)
        };

        var requiredFailures = checks.Where(x => x.RequiredForProduction && x.Status is "Missing" or "DevelopmentOnly" or "MvpFallback").ToArray();
        var status = requiredFailures.Length == 0 ? "ProductionReady" : "ProductionBlocked";
        return new ProductionReadinessReport(status, options.Environment, options.Auth.Mode, checks, DateTimeOffset.UtcNow);
    }

    private static ReadinessCheck Check(string name, string status, string detail, bool required)
        => new(name, status, string.IsNullOrWhiteSpace(detail) ? "not configured" : detail, required);
}
