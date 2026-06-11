# TaxNet Guardian

Explainable graph intelligence MVP for the CUST hackathon problem: Graph AI for Broadening the National Tax Net.

## What Is Implemented

- React/Vite white-theme intelligence dashboard at `/`
- React Gov Data Sandbox workspace at `/sandbox`
- React Citizen correction portal at `/citizen`
- Synthetic NADRA/FBR/Excise/SECP/Property/Utility/Travel provider records
- Replaceable government provider API shape through sandbox endpoints
- Dataset Feed Console for CSV/JSON imports into identity, tax, vehicle, property, utility, business, and travel domains
- Provider readiness controls that switch sandbox providers to official-API-ready configuration
- Identity resolution output with confidence and match reasons
- Synthetic evaluation state with precision/recall surfaced from labeled sandbox data
- Knowledge graph neighborhood API
- Tax Compliance Deviation Score
- Evidence-backed audit explanations
- RAG policy memory metadata, citations, and UI-based policy document indexing
- AI Orchestrator / Model Gateway demo route with deterministic fallback when no provider is configured
- Worker/SQS-style pipeline status
- Cognito-ready role/scope metadata with development header auth
- Runtime report generation endpoint
- Stitch-inspired UI patterns: fixed analyst sidebar, command search, KPI cards, regional risk map, case worklist, graph workspace, evidence drawer, and assistant drawer

The hackathon MVP runs as one .NET API/static host for speed, but the code and API surface follow the service boundaries in `docs/TaxNetGuardian_System_Design.md`. The React source lives in `TaxNetGuardian.Web` and builds into `TaxNetGuardian.Api/wwwroot`.

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

## Build Frontend

```powershell
cd TaxNetGuardian.Web
npm install
npm run build
```

The Vite build writes directly to:

```text
TaxNetGuardian.Api/wwwroot
```

## Evaluation And Quality Gates

The README and design both need to support the judge-facing claim that the system can be measured, not just demoed. The repo already includes a synthetic evaluation surface for that:

- The sandbox can generate noisy citizen and connector data instead of only clean records.
- Identity resolution exposes evaluation metadata against synthetic labels.
- The current local demo state reports precision, recall, and ambiguity rate for the resolver.
- The evaluation set is synthetic, so there is no claim of access to private NADRA, FBR, or utility ground truth.

Local snapshot from the current implementation:

- Precision: `0.93`
- Recall: `0.89`
- Evaluation set: synthetic labels from the Gov Data Sandbox

This is the right framing for the hackathon: show the metric, show the ambiguity cases, and explain that production metrics would be recomputed from labeled review outcomes.

## Demo Flow

1. Open the Gov Data Sandbox UI.
2. Use Dataset Feed Console to paste CSV/JSON, load templates, or upload a file.
3. Keep Run risk pipeline enabled so the imported records immediately update scoring.
4. Mark providers Official-ready to show how NADRA/FBR/Excise/etc. adapters can later swap to real APIs through Secrets Manager configuration.
5. Open the Auditor Dashboard.
6. Run import pipeline.
7. Select a critical case.
8. Inspect score breakdown, evidence cards, and graph explorer.
9. Ask the audit assistant why the case was flagged; if no live provider is configured, it returns the deterministic demo response rather than pretending to call a real external model.
10. Open System Control and index a RAG policy document.
11. Generate a report.
12. Open Citizen Portal and submit a correction.

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
POST /api/system/rag/documents
GET  /api/system/model-gateway
GET  /api/authz
```

Sandbox APIs:

```http
GET  /api/sandbox/providers
PATCH /api/sandbox/providers/{providerCode}
GET  /api/sandbox/datasets
GET  /api/sandbox/datasets/templates
POST /api/sandbox/datasets/feed
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

Dataset feed payload:

```json
{
  "datasetType": "tax",
  "format": "csv",
  "fileName": "fbr-feed.csv",
  "content": "personId,ntn,filerStatus,declaredAnnualIncome,taxPaid,taxYear\nEXT001,NTN-EXT001,Non-Filer,0,0,2025",
  "runPipeline": true
}
```

Provider replacement payload:

```json
{
  "mode": "OfficialReady",
  "baseUrl": "https://api.fbr.gov.pk",
  "credentialSecretName": "/taxnet/dev/providers/fbr/credentials",
  "enabled": true,
  "rateLimitPerMinute": 120,
  "notes": "Ready to replace sandbox with official provider adapter."
}
```

RAG policy feed payload:

```json
{
  "title": "Property valuation bulletin",
  "sourceType": "GovernmentPage",
  "url": "https://example.gov.pk/property-valuation",
  "content": "Policy text, circular notes, valuation rules, or public guidance.",
  "tags": ["property", "valuation", "tax-risk"]
}
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

## Demo Boundaries

This project is intentionally honest about what is mocked in the hackathon build:

- The public UI, sandbox UI, and citizen UI are real.
- The Gov Data Sandbox uses synthetic provider responses and replaceable contracts.
- Development headers simulate Cognito roles locally.
- The Model Gateway is a demo-time route with a deterministic fallback path when no provider is wired.
- The production target for these surfaces remains Cognito, Secrets Manager, SQS, S3, PostgreSQL, graph storage, and vector storage.

That keeps the demo believable: the product behavior is real enough to judge, while the integrations are clearly presented as replaceable.

## Verification Commands

```powershell
Invoke-RestMethod -Uri 'http://localhost:5187/api/dashboard/summary'
Invoke-RestMethod -Uri 'http://localhost:5187/api/cases'
Invoke-RestMethod -Uri 'http://localhost:5187/api/graph/entities/entity-P001/neighborhood'
Invoke-RestMethod -Uri 'http://localhost:5187/api/sandbox/datasets/templates'
```

Latest local verification:

- `dotnet build TaxNetGuardian.Api\TaxNetGuardian.Api.csproj --no-restore` passed.
- `npm run build` passed and emitted static assets into `TaxNetGuardian.Api/wwwroot`.
- Dataset feed, provider replacement, and RAG indexing endpoints were exercised successfully.
- Playwright checked `/sandbox` and System Control for required panels, console errors, and layout overflow.
