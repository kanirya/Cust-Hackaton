# TaxNet Guardian Worker Runtime

The backend now has separate worker executables for the production service boundaries described in the system design.

## Worker Projects

- `TaxNetGuardian.Workers.Ingestion`: consumes `taxnet-dev-ingestion-jobs`, stores raw snapshots, and can trigger dataset ingestion.
- `TaxNetGuardian.Workers.IdentityResolution`: consumes `taxnet-dev-identity-resolution-jobs` and writes identity evaluation artifacts.
- `TaxNetGuardian.Workers.GraphIntelligence`: consumes `taxnet-dev-graph-build-jobs` and writes graph feature artifacts.
- `TaxNetGuardian.Workers.RiskScoring`: consumes `taxnet-dev-risk-score-jobs` and triggers scoring pipeline refresh.
- `TaxNetGuardian.Workers.RagPolicy`: consumes `taxnet-dev-rag-index-jobs`, stores policy snapshots, and calls RAG indexing.
- `TaxNetGuardian.Workers.Report`: consumes `taxnet-dev-report-jobs`, generates case reports, and stores report artifacts.
- `TaxNetGuardian.Workers.AuditLog`: consumes `taxnet-dev-audit-log-jobs` and writes immutable audit-event artifacts.

## Runtime Modes

The workers support two infrastructure modes without external NuGet packages:

- `File`: local filesystem queues/object store under `.appdata/workers`. This is the default and works offline.
- `LocalStack`: HTTP adapters for LocalStack SQS/S3.

Environment variables:

```powershell
$env:TAXNET_QUEUE_MODE="File" # or LocalStack
$env:TAXNET_OBJECT_STORE_MODE="File" # or LocalStack
$env:TAXNET_API_BASE_URL="http://localhost:5191"
$env:LOCALSTACK_ENDPOINT="http://localhost:4566"
```

Run one cycle:

```powershell
.\scripts\seed-worker-demo.ps1
.\scripts\run-workers-once.ps1
```

Run continuously:

```powershell
dotnet run --project .\TaxNetGuardian.Workers.RagPolicy\TaxNetGuardian.Workers.RagPolicy.csproj -- --watch
```

LocalStack bootstrap:

```powershell
docker compose -f docker-compose.localstack.yml up -d
.\scripts\localstack-bootstrap.ps1
$env:TAXNET_QUEUE_MODE="LocalStack"
$env:TAXNET_OBJECT_STORE_MODE="LocalStack"
```
