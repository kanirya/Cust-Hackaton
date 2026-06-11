namespace TaxNetGuardian.Api;

public sealed class TaxNetPlatformOptions
{
    public const string SectionName = "TaxNet";

    public string Environment { get; init; } = "dev";
    public string PublicBaseUrl { get; init; } = "http://localhost:5191";
    public AuthOptions Auth { get; init; } = new();
    public AwsIntegrationOptions Aws { get; init; } = new();
    public RateLimitOptions RateLimits { get; init; } = new();
    public ObservabilityOptions Observability { get; init; } = new();
    public StorageOptions Storage { get; init; } = new();
}

public sealed class AuthOptions
{
    public string Mode { get; init; } = "DevelopmentHeaders";
    public string Authority { get; init; } = "";
    public string Audience { get; init; } = "";
    public bool RequireHttpsMetadata { get; init; } = true;
    public string RoleClaim { get; init; } = "cognito:groups";
    public string ScopeClaim { get; init; } = "scope";
    public string LocalStackAuthority { get; init; } = "";
    public bool AllowDevelopmentHeaders => Mode.Equals("DevelopmentHeaders", StringComparison.OrdinalIgnoreCase);
    public bool RequireJwt => Mode.Equals("CognitoJwt", StringComparison.OrdinalIgnoreCase);
}

public sealed class AwsIntegrationOptions
{
    public string Region { get; init; } = "us-east-1";
    public string LocalStackEndpoint { get; init; } = "";
    public bool UseLocalStack { get; init; }

    public bool ShouldUseLocalStack()
        => UseLocalStack || !string.IsNullOrWhiteSpace(LocalStackEndpoint) || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("LOCALSTACK_ENDPOINT"));

    public string EffectiveLocalStackEndpoint()
        => Environment.GetEnvironmentVariable("LOCALSTACK_ENDPOINT") ?? LocalStackEndpoint;
}

public sealed class RateLimitOptions
{
    public int PermitLimit { get; init; } = 120;
    public int WindowSeconds { get; init; } = 60;
    public int QueueLimit { get; init; } = 20;
}

public sealed class ObservabilityOptions
{
    public string ServiceName { get; init; } = "TaxNetGuardian.Api";
    public bool IncludeRequestHeaders { get; init; }
}

public sealed class StorageOptions
{
    public string OperationalStore { get; init; } = "JsonSnapshot";
    public string PostgresConnectionString { get; init; } = "";
    public string GraphStore { get; init; } = "InMemoryGraph";
    public string VectorStore { get; init; } = "LexicalRagIndex";
    public string ObjectStore { get; init; } = "LocalStackOrFile";
}
