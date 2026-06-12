var builder = DistributedApplication.CreateBuilder(args);

var postgresPort = int.TryParse(Environment.GetEnvironmentVariable("POSTGRES_PORT"), out var configuredPostgresPort)
    ? configuredPostgresPort
    : 15432;
var postgres = builder
    .AddPostgres("postgres", port: postgresPort)
    .WithDataVolume();
var taxnetDatabase = postgres.AddDatabase("taxnet", "taxnet");

var localstack = builder
    .AddContainer("localstack", "localstack/localstack", "3")
    .WithHttpEndpoint(port: 4566, targetPort: 4566, name: "edge")
    .WithEnvironment("SERVICES", "sqs,s3,secretsmanager,logs,cloudwatch,cognito-idp,iam,kms,sts,sns,events")
    .WithEnvironment("AWS_DEFAULT_REGION", "us-east-1")
    .WithEnvironment("PERSISTENCE", "1");

var workerDataRoot = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", ".appdata", "aspire-workers"));
var apiBaseUrl = "http://localhost:5191";
var localStackEndpoint = "http://localhost:4566";
var useCognito = Environment.GetEnvironmentVariable("TAXNET_USE_COGNITO") ?? "false";
var terraformApply = builder
    .AddExecutable(
        "localstack-terraform",
        "powershell",
        Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..")),
        "-NoProfile",
        "-ExecutionPolicy",
        "Bypass",
        "-File",
        "scripts/localstack-terraform-apply.ps1")
    .WithEnvironment("LOCALSTACK_ENDPOINT", localStackEndpoint)
    .WithEnvironment("LOCALSTACK_CONTAINER_ENDPOINT", "http://host.docker.internal:4566")
    .WithEnvironment("LOCALSTACK_ENABLE_COGNITO", Environment.GetEnvironmentVariable("LOCALSTACK_ENABLE_COGNITO") ?? "true")
    .WithEnvironment("TERRAFORM_IMAGE", "hashicorp/terraform:1.9.8")
    .WaitFor(localstack);

var api = builder
    .AddProject<Projects.TaxNetGuardian_Api>("taxnet-api")
    .WithHttpEndpoint(port: 5191, name: "http")
    .WithReference(taxnetDatabase)
    .WithEnvironment("TAXNET_QUEUE_MODE", "LocalStack")
    .WithEnvironment("TAXNET_OBJECT_STORE_MODE", "LocalStack")
    .WithEnvironment("TAXNET_SECRET_PROVIDER", "Auto")
    .WithEnvironment("TaxNet__Environment", "local-aspire")
    .WithEnvironment("TaxNet__Auth__Mode", useCognito.Equals("true", StringComparison.OrdinalIgnoreCase) ? "CognitoJwt" : "DevelopmentHeaders")
    .WithEnvironment("TaxNet__Auth__Authority", Environment.GetEnvironmentVariable("COGNITO_AUTHORITY") ?? "")
    .WithEnvironment("TaxNet__Auth__Audience", Environment.GetEnvironmentVariable("COGNITO_AUDIENCE") ?? "")
    .WithEnvironment("TaxNet__Aws__UseLocalStack", "true")
    .WithEnvironment("TaxNet__Aws__LocalStackEndpoint", localStackEndpoint)
    .WithEnvironment("TaxNet__Aws__Region", "us-east-1")
    .WithEnvironment("TaxNet__Storage__OperationalStore", "PostgreSql")
    .WithEnvironment("TaxNet__Storage__GraphStore", "InMemoryGraph")
    .WithEnvironment("TaxNet__Storage__VectorStore", "LexicalRagIndex")
    .WithEnvironment("TaxNet__Storage__ObjectStore", "LocalStack")
    .WithEnvironment("TAXNET_WORKER_DATA_ROOT", workerDataRoot)
    .WithEnvironment("TAXNET_API_BASE_URL", apiBaseUrl)
    .WithEnvironment("LOCALSTACK_ENDPOINT", localStackEndpoint)
    .WithEnvironment("MODEL_GATEWAY_DEFAULT_PROVIDER", Environment.GetEnvironmentVariable("MODEL_GATEWAY_DEFAULT_PROVIDER") ?? "auto")
    .WithEnvironment("OPENAI_API_KEY", Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "")
    .WithEnvironment("DEEPSEEK_API_KEY", Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY") ?? "")
    .WithEnvironment("GEMINI_API_KEY", Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? "")
    .WithEnvironment("CLAUDE_API_KEY", Environment.GetEnvironmentVariable("CLAUDE_API_KEY") ?? "")
    .WaitFor(taxnetDatabase)
    .WaitForCompletion(terraformApply);

var web = builder
    .AddExecutable(
        "taxnet-web",
        "cmd",
        Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "TaxNetGuardian.Web")),
        "/c",
        "npm",
        "run",
        "dev:aspire")
    .WithHttpEndpoint(port: 5173, targetPort: 5174, name: "http")
    .WithEnvironment("BROWSER", "none")
    .WaitFor(api);

AddWorker<Projects.TaxNetGuardian_Workers_Ingestion>("ingestion-worker", "Ingestion.Worker", "taxnet-dev-ingestion-jobs");
AddWorker<Projects.TaxNetGuardian_Workers_IdentityResolution>("identity-resolution-worker", "IdentityResolution.Worker", "taxnet-dev-identity-resolution-jobs");
AddWorker<Projects.TaxNetGuardian_Workers_GraphIntelligence>("graph-intelligence-worker", "GraphIntelligence.Worker", "taxnet-dev-graph-build-jobs");
AddWorker<Projects.TaxNetGuardian_Workers_RiskScoring>("risk-scoring-worker", "RiskScoring.Worker", "taxnet-dev-risk-score-jobs");
AddWorker<Projects.TaxNetGuardian_Workers_RagPolicy>("rag-policy-worker", "RagPolicy.Worker", "taxnet-dev-rag-index-jobs");
AddWorker<Projects.TaxNetGuardian_Workers_Report>("report-worker", "Report.Worker", "taxnet-dev-report-jobs");
AddWorker<Projects.TaxNetGuardian_Workers_AuditLog>("audit-log-worker", "AuditLog.Worker", "taxnet-dev-audit-log-jobs");

builder.Build().Run();

void AddWorker<TProject>(string resourceName, string workerName, string queueName)
    where TProject : class, IProjectMetadata, new()
{
    builder
        .AddProject<TProject>(resourceName)
        .WithArgs("--watch")
        .WithEnvironment("TAXNET_WORKER_NAME", workerName)
        .WithEnvironment("TAXNET_QUEUE_NAME", queueName)
        .WithEnvironment("TAXNET_QUEUE_MODE", "LocalStack")
        .WithEnvironment("TAXNET_OBJECT_STORE_MODE", "LocalStack")
        .WithEnvironment("TAXNET_WORKER_DATA_ROOT", workerDataRoot)
        .WithEnvironment("TAXNET_API_BASE_URL", apiBaseUrl)
        .WithEnvironment("TAXNET_DEMO_ROLE", "taxnet-admin")
        .WithEnvironment("LOCALSTACK_ENDPOINT", localStackEndpoint)
        .WaitForCompletion(terraformApply);
}
