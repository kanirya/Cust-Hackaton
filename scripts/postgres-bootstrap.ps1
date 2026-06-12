<#
.SYNOPSIS
  Brings up the TaxNet Guardian PostgreSQL operational database, seeds Pakistani
  synthetic identities, and projects them into the relational tables.

.DESCRIPTION
  1. Starts the dedicated Postgres 17 container (docker-compose.postgres.yml) on host port 5433.
  2. Waits for the database to accept connections.
  3. Seeds synthetic citizens via the API sandbox generator.
  4. Migrates the relational schema and projects state into taxnet_* tables.
  5. Prints projection counts.

  The API must already be running on http://localhost:5028 with
  TaxNet:Storage:OperationalStore = PostgreSql (see appsettings.Development.json).

.PARAMETER Count
  Number of synthetic identities to generate (default 400).

.EXAMPLE
  ./scripts/postgres-bootstrap.ps1 -Count 500
#>
param(
    [int]$Count = 400,
    [int]$SuspiciousPercent = 22,
    [int]$NoisePercent = 16,
    [string]$ApiBaseUrl = "http://localhost:5028"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

Write-Host "==> Starting Postgres container..." -ForegroundColor Cyan
docker compose -f (Join-Path $repoRoot "docker-compose.postgres.yml") up -d | Out-Null

Write-Host "==> Waiting for Postgres to accept connections..." -ForegroundColor Cyan
$ready = $false
for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep -Seconds 2
    $r = docker exec taxnet-postgres pg_isready -U taxnet -d taxnetguardian 2>&1
    if ($r -match "accepting connections") { $ready = $true; break }
}
if (-not $ready) { throw "Postgres did not become ready in time." }
Write-Host "    Postgres is ready on localhost:5433 (db=taxnetguardian, user=taxnet)." -ForegroundColor Green

$admin = @{ "X-Demo-Role" = "taxnet-admin"; "X-Demo-User" = "taxnet-bootstrap" }
$sandbox = @{ "X-Demo-Role" = "taxnet-sandbox-admin"; "X-Demo-User" = "taxnet-bootstrap" }

Write-Host "==> Verifying API persistence mode..." -ForegroundColor Cyan
$pers = Invoke-RestMethod -Uri "$ApiBaseUrl/api/system/persistence" -Headers $admin -TimeoutSec 15
if ($pers.operationalStore -ne "PostgreSql") {
    throw "API operationalStore is '$($pers.operationalStore)'. Set TaxNet:Storage:OperationalStore=PostgreSql and restart the API."
}
Write-Host "    operationalStore=PostgreSql, postgres.reachable=$($pers.postgres.reachable)" -ForegroundColor Green

Write-Host "==> Seeding $Count synthetic Pakistani identities..." -ForegroundColor Cyan
$body = @{ count = $Count; suspiciousPercent = $SuspiciousPercent; noisePercent = $NoisePercent } | ConvertTo-Json
$gen = Invoke-RestMethod -Uri "$ApiBaseUrl/api/sandbox/admin/generate" -Method Post -Headers $sandbox -ContentType "application/json" -Body $body -TimeoutSec 180
Write-Host "    Generated profiles=$($gen.profiles), cases=$($gen.cases)" -ForegroundColor Green

Write-Host "==> Migrating relational schema + projecting state..." -ForegroundColor Cyan
Invoke-RestMethod -Uri "$ApiBaseUrl/api/system/storage/migrate" -Method Post -Headers $admin -TimeoutSec 60 | Out-Null
$sync = Invoke-RestMethod -Uri "$ApiBaseUrl/api/system/storage/sync" -Method Post -Headers $admin -TimeoutSec 180
Write-Host "    rowsProjected=$($sync.rowsProjected) error=$($sync.error)" -ForegroundColor Green

Write-Host "==> Projection counts:" -ForegroundColor Cyan
$proj = Invoke-RestMethod -Uri "$ApiBaseUrl/api/system/storage/projection" -Headers $admin -TimeoutSec 30
$proj.counts | Format-Table -AutoSize | Out-String | Write-Host

Write-Host "Done. Query with: docker exec -it taxnet-postgres psql -U taxnet -d taxnetguardian" -ForegroundColor Green
