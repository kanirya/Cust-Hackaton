param(
  [string] $BaseUrl = "http://localhost:5191",
  [switch] $SkipLocalStack,
  [switch] $Json
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$checks = New-Object System.Collections.Generic.List[object]

function Add-Check {
  param(
    [string] $Name,
    [string] $Status,
    [object] $Details = $null
  )

  $checks.Add([pscustomobject]@{
    name = $Name
    status = $Status
    details = $Details
  }) | Out-Null
}

function Invoke-TaxNetJson {
  param(
    [string] $Method,
    [string] $Path,
    [object] $Body = $null,
    [string] $Role = "taxnet-admin"
  )

  $headers = @{
    "Content-Type" = "application/json"
    "X-Demo-Role" = $Role
    "X-Demo-User" = "stack-verify"
  }

  $options = @{
    Method = $Method
    Uri = "$BaseUrl$Path"
    Headers = $headers
    TimeoutSec = 20
  }

  if ($null -ne $Body) {
    $options.Body = ($Body | ConvertTo-Json -Depth 20)
  }

  Invoke-RestMethod @options
}

try {
  $health = Invoke-TaxNetJson -Method "GET" -Path "/api/health"
  Add-Check "api.health" "pass" $health
}
catch {
  Add-Check "api.health" "fail" $_.Exception.Message
}

try {
  $ready = Invoke-TaxNetJson -Method "GET" -Path "/health/ready"
  Add-Check "api.readiness" "pass" @{ status = $ready.status; checks = $ready.checks.Count }
}
catch {
  Add-Check "api.readiness" "fail" $_.Exception.Message
}

try {
  $rag = Invoke-TaxNetJson -Method "POST" -Path "/api/system/rag/query" -Role "taxnet-policy-analyst" -Body @{
    query = "asset mismatch human review policy"
    taskType = "AuditExplanation"
    jurisdiction = "Pakistan"
    topK = 3
    tags = @("audit", "asset")
  }
  $hasVector = (($rag.qualityChecks -join " ") -like "*vector similarity*")
  Add-Check "rag.hybrid_query" ($(if ($rag.chunks.Count -gt 0 -and $hasVector) { "pass" } else { "fail" })) @{
    chunks = $rag.chunks.Count
    confidence = $rag.retrievalConfidence
    vectorScoring = $hasVector
  }
}
catch {
  Add-Check "rag.hybrid_query" "fail" $_.Exception.Message
}

try {
  $cnic = Invoke-TaxNetJson -Method "POST" -Path "/api/investigations/cnic" -Role "taxnet-auditor" -Body @{
    cnic = "42201-***01"
    caseId = "case-P001"
    preferredProvider = "claude"
    allowExternalProvider = $false
  }
  Add-Check "cases.cnic_investigation" ($(if ($cnic.status -eq "Completed" -and $cnic.matchedRecords.Count -gt 0 -and -not [string]::IsNullOrWhiteSpace($cnic.aiNarrative)) { "pass" } else { "fail" })) @{
    status = $cnic.status
    records = $cnic.matchedRecords.Count
    signals = $cnic.signals.Count
    provider = $cnic.model.selectedProvider
  }
}
catch {
  Add-Check "cases.cnic_investigation" "fail" $_.Exception.Message
}

try {
  $model = Invoke-TaxNetJson -Method "GET" -Path "/api/system/model-gateway" -Role "taxnet-model-admin"
  Add-Check "model_gateway.status" "pass" @{
    defaultProvider = $model.defaultProvider
    providers = $model.providerStatus.Count
  }
}
catch {
  Add-Check "model_gateway.status" "fail" $_.Exception.Message
}

try {
  $persistence = Invoke-TaxNetJson -Method "GET" -Path "/api/system/persistence"
  Add-Check "storage.persistence" "pass" @{
    operationalStore = $persistence.operationalStore
    activeSnapshotStore = $persistence.activeSnapshotStore
    postgresConfigured = $persistence.postgres.configured
    postgresReachable = $persistence.postgres.reachable
  }
}
catch {
  Add-Check "storage.persistence" "fail" $_.Exception.Message
}

try {
  $projection = Invoke-TaxNetJson -Method "GET" -Path "/api/system/storage/projection"
  Add-Check "storage.postgres_projection" ($(if ($projection.configured -eq $false -or $projection.reachable -eq $true) { "pass" } else { "fail" })) $projection
}
catch {
  Add-Check "storage.postgres_projection" "fail" $_.Exception.Message
}

if (-not $SkipLocalStack) {
  try {
    $health = Invoke-RestMethod -Uri "http://localhost:4566/_localstack/health" -TimeoutSec 3
    Add-Check "localstack.health" "pass" $health
  }
  catch {
    Add-Check "localstack.health" "skip" "LocalStack is not reachable at http://localhost:4566."
  }

  $outputsPath = Join-Path $repoRoot ".appdata\terraform\localstack\outputs.json"
  if (Test-Path $outputsPath) {
    try {
      $outputs = Get-Content -Raw $outputsPath | ConvertFrom-Json
      Add-Check "terraform.outputs" "pass" @{
        queues = @($outputs.sqs_queue_urls.value.PSObject.Properties).Count
        buckets = @($outputs.s3_buckets.value).Count
        secrets = @($outputs.secret_names.value).Count
        cognitoPool = $outputs.cognito_user_pool_id.value
      }
    }
    catch {
      Add-Check "terraform.outputs" "fail" $_.Exception.Message
    }
  }
  else {
    Add-Check "terraform.outputs" "skip" "Run scripts/localstack-terraform-apply.ps1 through Aspire or manually after Docker starts."
  }
}

$failed = @($checks | Where-Object { $_.status -eq "fail" })
$result = [pscustomobject]@{
  status = $(if ($failed.Count -eq 0) { "pass" } else { "fail" })
  baseUrl = $BaseUrl
  checkedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
  checks = $checks
}

if ($Json) {
  $result | ConvertTo-Json -Depth 20
}
else {
  $result.checks | Format-Table -AutoSize
  Write-Host ""
  Write-Host "TaxNet stack verification: $($result.status)"
}

if ($failed.Count -gt 0) {
  exit 1
}
