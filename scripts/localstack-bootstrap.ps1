$ErrorActionPreference = "Stop"

$endpoint = $env:LOCALSTACK_ENDPOINT
if ([string]::IsNullOrWhiteSpace($endpoint)) {
  $endpoint = "http://localhost:4566"
}

$queues = @(
  "taxnet-dev-ingestion-jobs",
  "taxnet-dev-identity-resolution-jobs",
  "taxnet-dev-graph-build-jobs",
  "taxnet-dev-risk-score-jobs",
  "taxnet-dev-rag-index-jobs",
  "taxnet-dev-report-jobs",
  "taxnet-dev-audit-log-jobs"
)

$buckets = @(
  "taxnet-dev-raw-source-snapshots",
  "taxnet-dev-audit-reports",
  "taxnet-dev-audit-events",
  "taxnet-dev-worker-artifacts",
  "taxnet-dev-worker-failures"
)

foreach ($queue in $queues) {
  Invoke-WebRequest -Method Post -Uri $endpoint -Body @{
    Action = "CreateQueue"
    QueueName = $queue
    Version = "2012-11-05"
  } -UseBasicParsing | Out-Null
  Write-Host "Created SQS queue $queue"
}

foreach ($bucket in $buckets) {
  Invoke-WebRequest -Method Put -Uri "$endpoint/$bucket" -UseBasicParsing | Out-Null
  Write-Host "Created S3 bucket $bucket"
}
