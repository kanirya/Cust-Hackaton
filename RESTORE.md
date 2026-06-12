# TaxNet Guardian — Restore Guide

This archive contains the **complete project**, including git history (`.git`) and the PostgreSQL
data volume (`.appdata/postgres`). Only the trivially regenerable build outputs are excluded to keep
the size down:

- `node_modules/` (restored with `npm install`)
- `bin/`, `obj/` (restored with `dotnet build`)

Everything else — source, specs (`.kiro`), docs, infra, scripts, the JSON state snapshot
(`App_Data/taxnet-state.json`), and the Postgres data — is included.

---

## Prerequisites
- .NET 10 SDK
- Node.js 18+ and npm
- Docker Desktop (for PostgreSQL + LocalStack)
- A Claude API key (or another provider) for live AI; the app falls back to deterministic output without one.

## 1. Unzip
Extract the archive anywhere. You should get a `Cust-Hackaton` folder.

## 2. Restore backend build artifacts
```powershell
cd Cust-Hackaton
dotnet restore
dotnet build TaxNetGuardian.Api/TaxNetGuardian.Api.csproj
```

## 3. Restore the web app and build the UI
```powershell
cd TaxNetGuardian.Web
npm install
npm run build      # outputs to ../TaxNetGuardian.Api/wwwroot
cd ..
```

## 4. Start the database (PostgreSQL)
The Postgres data is included in `.appdata/postgres`, so this brings the database back **with all
408 seeded identities intact**:
```powershell
docker compose -f docker-compose.postgres.yml up -d
```
- Host: `localhost:5433`  ·  DB: `taxnetguardian`  ·  user: `taxnet`  ·  password: `taxnet_dev_pw`

If you deleted `.appdata/postgres` (empty DB), reseed after the API is running:
```powershell
./scripts/postgres-bootstrap.ps1 -Count 400
```
(The API also auto-falls back to `App_Data/taxnet-state.json` and repopulates Postgres on first save,
so data is never lost even without the volume.)

## 5. (Optional) Start LocalStack
```powershell
docker compose -f docker-compose.localstack.yml up -d
```

## 6. Run the API
```powershell
$env:CLAUDE_API_KEY = 'sk-ant-...'                       # your key
$env:CLAUDE_MODEL   = 'claude-sonnet-4-5-20250929'
dotnet run --project TaxNetGuardian.Api --urls http://localhost:5028
```
Open http://localhost:5028

## 7. (Optional) Enable the fine-tuned local model
After fine-tuning per `docs/Custom_Model_FineTuning.md` and registering it in Ollama:
```powershell
$env:OLLAMA_ENABLED = 'true'
$env:OLLAMA_MODEL   = 'taxnet-guardian'
```
Then pick **Fine-tuned (Ollama)** on the Model Training page.

---

## Config / persistence notes
- Operational store is PostgreSQL (`appsettings.Development.json` → `TaxNet:Storage:OperationalStore=PostgreSql`).
  To run without Docker/Postgres, set it back to `JsonSnapshot`.
- The default inference mode is **Frontier LLM**; switch to Retrieval / Hybrid / Fine-tuned on the
  Model Training page.
- **Rotate the Claude API key** if it was ever committed or shared.

## Common gotchas
- "File in use / COM Surrogate" when zipping or deleting: stop the Postgres container first
  (`docker compose -f docker-compose.postgres.yml down`).
- Before a fresh `dotnet build`, stop any running API: `Get-Process -Name "TaxNetGuardian*" | Stop-Process -Force`.
