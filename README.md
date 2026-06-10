# TaxNet Guardian

Explainable graph intelligence MVP for the CUST hackathon problem: Graph AI for Broadening the National Tax Net.

## What Is Implemented

- White-theme auditor dashboard at `/`
- Separate Gov Data Sandbox UI at `/sandbox`
- Citizen correction portal at `/citizen`
- Synthetic NADRA/FBR/Excise/SECP/Property/Utility/Travel provider records
- Replaceable government provider API shape through sandbox endpoints
- Identity resolution output with confidence and match reasons
- Knowledge graph neighborhood API
- Tax Compliance Deviation Score
- Evidence-backed audit explanations
- RAG policy memory metadata and citations
- AI Orchestrator / Model Gateway mock route
- Worker/SQS-style pipeline status
- Cognito-ready role/scope metadata with development header auth
- Runtime report generation endpoint

The hackathon MVP runs as one .NET host for speed, but the code and API surface follow the service boundaries in `docs/TaxNetGuardian_System_Design.md`.

## Run Locally

From the workspace root:

```powershell
$env:APPDATA='C:\Users\hp\Documents\New project\.appdata'
$env:NUGET_PACKAGES='C:\Users\hp\Documents\New project\.nuget\packages'
dotnet restore TaxNetGuardian.Api\TaxNetGuardian.Api.csproj --configfile NuGet.Config
dotnet build TaxNetGuardian.Api\TaxNetGuardian.Api.csproj --no-restore
dotnet TaxNetGuardian.Api\bin\Debug\net10.0\TaxNetGuardian.Api.dll --urls http://localhost:5187
```

Open:

```text
http://localhost:5187/
http://localhost:5187/sandbox
http://localhost:5187/citizen
```

## Demo Flow

1. Open the Gov Data Sandbox UI.
2. Generate synthetic profiles with suspicious patterns and noisy identity fields.
3. Open the Auditor Dashboard.
4. Run import pipeline.
5. Select a critical case.
6. Inspect score breakdown, evidence cards, and graph explorer.
7. Ask the audit assistant why the case was flagged.
8. Generate a report.
9. Open Citizen Portal and submit a correction.

## Key APIs

```http
GET  /api/health
GET  /api/dashboard/summary
GET  /api/cases
GET  /api/cases/{caseId}
GET  /api/graph/entities/{entityId}/neighborhood
POST /api/assistant/cases/{caseId}/ask
POST /api/reports/cases/{caseId}
POST /api/ingestion/run
GET  /api/system/workers
GET  /api/system/rag
GET  /api/system/model-gateway
GET  /api/authz
```

Sandbox APIs:

```http
GET  /api/sandbox/providers
GET  /api/sandbox/profiles
GET  /api/sandbox/profiles/{id}
POST /api/sandbox/admin/generate

GET /sandbox/nadra/identity/{identityToken}
GET /sandbox/fbr/taxpayer/{identityToken}
GET /sandbox/fbr/atl-status/{ntn}
GET /sandbox/excise/vehicles?identityToken={token}
GET /sandbox/secp/companies?identityToken={token}
GET /sandbox/property/ownership?identityToken={token}
GET /sandbox/utilities/bills?identityToken={token}
GET /sandbox/travel/history?identityToken={token}
```

## Development Auth

The MVP uses headers to simulate Cognito roles:

```http
X-Demo-Role: taxnet-admin
X-Demo-User: demo-user
```

Important roles:

- `taxnet-admin`
- `taxnet-sandbox-admin`
- `taxnet-auditor`
- `taxnet-supervisor`
- `taxnet-citizen`
- `taxnet-model-admin`
- `taxnet-policy-analyst`

Production target:

- Cognito User Pools for users
- Cognito OAuth client credentials for internal services
- AWS Secrets Manager for provider/model/database secrets
- SQS, S3, CloudWatch, Redis, PostgreSQL, Graph DB, Vector DB

## Verification Commands

```powershell
Invoke-RestMethod -Uri 'http://localhost:5187/api/dashboard/summary'
Invoke-RestMethod -Uri 'http://localhost:5187/api/cases'
Invoke-RestMethod -Uri 'http://localhost:5187/api/graph/entities/entity-P001/neighborhood'
```

