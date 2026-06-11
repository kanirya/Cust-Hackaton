var builder = DistributedApplication.CreateBuilder(args);

var localstack = builder
    .AddContainer("localstack", "localstack/localstack", "3")
    .WithHttpEndpoint(port: 4566, targetPort: 4566, name: "edge")
    .WithEnvironment("SERVICES", "sqs,s3,secretsmanager")
    .WithEnvironment("AWS_DEFAULT_REGION", "us-east-1");

var workerDataRoot = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", ".appdata", "aspire-workers"));
var apiBaseUrl = "http://localhost:5191";
var localStackEndpoint = "http://localhost:4566";

var api = builder
    .AddProject<Projects.TaxNetGuardian_Api>("taxnet-api")
    .WithHttpEndpoint(port: 5191, name: "http")
    .WithEnvironment("TAXNET_QUEUE_MODE", "File")
    .WithEnvironment("TAXNET_OBJECT_STORE_MODE", "File")
    .WithEnvironment("TAXNET_WORKER_DATA_ROOT", workerDataRoot)
    .WithEnvironment("TAXNET_API_BASE_URL", apiBaseUrl)
    .WithEnvironment("LOCALSTACK_ENDPOINT", localStackEndpoint)
    .WithEnvironment("MODEL_GATEWAY_DEFAULT_PROVIDER", Environment.GetEnvironmentVariable("MODEL_GATEWAY_DEFAULT_PROVIDER") ?? "auto")
    .WithEnvironment("OPENAI_API_KEY", Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "")
    .WithEnvironment("DEEPSEEK_API_KEY", Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY") ?? "")
    .WithEnvironment("GEMINI_API_KEY", Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? "")
    .WithEnvironment("CLAUDE_API_KEY", Environment.GetEnvironmentVariable("CLAUDE_API_KEY") ?? "");

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
        .WithEnvironment("TAXNET_WORKER_NAME", workerName)
        .WithEnvironment("TAXNET_QUEUE_NAME", queueName)
        .WithEnvironment("TAXNET_QUEUE_MODE", "File")
        .WithEnvironment("TAXNET_OBJECT_STORE_MODE", "File")
        .WithEnvironment("TAXNET_WORKER_DATA_ROOT", workerDataRoot)
        .WithEnvironment("TAXNET_API_BASE_URL", apiBaseUrl)
        .WithEnvironment("TAXNET_DEMO_ROLE", "taxnet-admin")
        .WithEnvironment("LOCALSTACK_ENDPOINT", localStackEndpoint);
}
