$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$terraformDir = Join-Path $repoRoot "infra\localstack"
$stateDir = Join-Path $repoRoot ".appdata\terraform\localstack"
$pluginCache = Join-Path $repoRoot ".appdata\terraform\plugin-cache"
$endpoint = $env:LOCALSTACK_ENDPOINT
if ([string]::IsNullOrWhiteSpace($endpoint)) {
  $endpoint = "http://localhost:4566"
}

New-Item -ItemType Directory -Force -Path $stateDir, $pluginCache | Out-Null

Write-Host "Waiting for LocalStack at $endpoint ..."
for ($i = 0; $i -lt 90; $i++) {
  try {
    $health = Invoke-RestMethod -Uri "$endpoint/_localstack/health" -TimeoutSec 2
    if ($health) {
      Write-Host "LocalStack health endpoint responded."
      break
    }
  }
  catch {
    Start-Sleep -Seconds 2
  }

  if ($i -eq 89) {
    throw "LocalStack did not become reachable at $endpoint."
  }
}

$terraformImage = $env:TERRAFORM_IMAGE
if ([string]::IsNullOrWhiteSpace($terraformImage)) {
  $terraformImage = "hashicorp/terraform:1.9.8"
}

$containerEndpoint = $env:LOCALSTACK_CONTAINER_ENDPOINT
if ([string]::IsNullOrWhiteSpace($containerEndpoint)) {
  $containerEndpoint = "http://host.docker.internal:4566"
}

function Invoke-Terraform {
  param([string[]] $TerraformArgs)

  $dockerArgs = @(
    "run", "--rm",
    "-v", "${terraformDir}:/workspace",
    "-v", "${stateDir}:/state",
    "-v", "${pluginCache}:/plugin-cache",
    "-w", "/workspace",
    "-e", "AWS_ACCESS_KEY_ID=test",
    "-e", "AWS_SECRET_ACCESS_KEY=test",
    "-e", "AWS_DEFAULT_REGION=us-east-1",
    "-e", "TF_PLUGIN_CACHE_DIR=/plugin-cache",
    "-e", "TF_DATA_DIR=/state/.terraform",
    "-e", "TF_VAR_localstack_endpoint=$containerEndpoint",
    "-e", "TF_VAR_aws_region=us-east-1",
    "-e", "TF_VAR_enable_cognito=false",
    $terraformImage
  ) + $TerraformArgs

  Write-Host "docker $($dockerArgs -join ' ')"
  & docker @dockerArgs
  if ($LASTEXITCODE -ne 0) {
    throw "Terraform command failed with exit code $LASTEXITCODE"
  }
}

Invoke-Terraform @("init", "-input=false")
Invoke-Terraform @("apply", "-auto-approve", "-input=false", "-state=/state/terraform.tfstate")

Write-Host "LocalStack Terraform provisioning completed."
