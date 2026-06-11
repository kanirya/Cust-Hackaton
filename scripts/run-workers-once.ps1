$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$projects = @(
  "TaxNetGuardian.Workers.Ingestion",
  "TaxNetGuardian.Workers.IdentityResolution",
  "TaxNetGuardian.Workers.GraphIntelligence",
  "TaxNetGuardian.Workers.RiskScoring",
  "TaxNetGuardian.Workers.RagPolicy",
  "TaxNetGuardian.Workers.Report",
  "TaxNetGuardian.Workers.AuditLog"
)

foreach ($project in $projects) {
  Write-Host "Running $project"
  dotnet run --project "$root\$project\$project.csproj"
}
