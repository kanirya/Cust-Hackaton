param(
  [Parameter(Mandatory = $true)]
  [ValidateSet("openai", "deepseek", "gemini", "claude")]
  [string] $Provider,

  [Parameter(Mandatory = $true)]
  [AllowEmptyString()]
  [string] $ApiKey
)

$ErrorActionPreference = "Stop"

$endpoint = $env:LOCALSTACK_ENDPOINT
if ([string]::IsNullOrWhiteSpace($endpoint)) {
  $endpoint = "http://localhost:4566"
}

$secretName = "taxnet/dev/model-gateway/$Provider"
$payload = @{
  SecretId = $secretName
  ClientRequestToken = [guid]::NewGuid().ToString()
  SecretString = (@{
    provider = $Provider
    apiKey = $ApiKey
    updatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
  } | ConvertTo-Json -Compress)
} | ConvertTo-Json -Compress

Invoke-RestMethod `
  -Method Post `
  -Uri $endpoint `
  -Headers @{
    "X-Amz-Target" = "secretsmanager.PutSecretValue"
    "Content-Type" = "application/x-amz-json-1.1"
  } `
  -Body $payload | Out-Null

Write-Host "Updated $secretName in LocalStack Secrets Manager."
