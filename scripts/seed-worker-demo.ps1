$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
dotnet run --project "$root\TaxNetGuardian.Worker.Cli\TaxNetGuardian.Worker.Cli.csproj" -- seed-demo
