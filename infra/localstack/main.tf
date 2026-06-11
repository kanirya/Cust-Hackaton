locals {
  queue_names = toset([
    "taxnet-dev-ingestion-jobs",
    "taxnet-dev-identity-resolution-jobs",
    "taxnet-dev-graph-build-jobs",
    "taxnet-dev-risk-score-jobs",
    "taxnet-dev-rag-index-jobs",
    "taxnet-dev-report-jobs",
    "taxnet-dev-audit-log-jobs"
  ])

  bucket_names = toset([
    "taxnet-dev-raw-source-snapshots",
    "taxnet-dev-audit-reports",
    "taxnet-dev-audit-events",
    "taxnet-dev-worker-artifacts",
    "taxnet-dev-worker-failures",
    "taxnet-dev-rag-policy-documents",
    "taxnet-dev-sandbox-datasets"
  ])

  model_secret_names = toset([
    "taxnet/dev/model-gateway/openai",
    "taxnet/dev/model-gateway/deepseek",
    "taxnet/dev/model-gateway/gemini",
    "taxnet/dev/model-gateway/claude",
    "taxnet/dev/cognito/service-client",
    "taxnet/dev/sandbox/provider-credentials"
  ])

  cognito_groups = toset([
    "taxnet-admin",
    "taxnet-supervisor",
    "taxnet-auditor",
    "taxnet-sandbox-admin",
    "taxnet-policy-analyst",
    "taxnet-model-admin",
    "taxnet-citizen"
  ])
}

resource "aws_kms_key" "taxnet" {
  description             = "LocalStack KMS key for TaxNet Guardian local resources"
  deletion_window_in_days = 7
}

resource "aws_kms_alias" "taxnet" {
  name          = "alias/taxnet-dev"
  target_key_id = aws_kms_key.taxnet.key_id
}

resource "aws_s3_bucket" "buckets" {
  for_each = local.bucket_names
  bucket   = each.value
}

resource "aws_s3_bucket_versioning" "buckets" {
  for_each = aws_s3_bucket.buckets
  bucket   = each.value.id

  versioning_configuration {
    status = "Enabled"
  }
}

resource "aws_s3_bucket_server_side_encryption_configuration" "buckets" {
  for_each = aws_s3_bucket.buckets
  bucket   = each.value.id

  rule {
    apply_server_side_encryption_by_default {
      sse_algorithm = "AES256"
    }
  }
}

resource "aws_sqs_queue" "dlq" {
  for_each                  = local.queue_names
  name                      = "${each.value}-dlq"
  message_retention_seconds = 1209600
}

resource "aws_sqs_queue" "jobs" {
  for_each                   = local.queue_names
  name                       = each.value
  visibility_timeout_seconds = 45
  message_retention_seconds  = 345600
  receive_wait_time_seconds  = 1

  redrive_policy = jsonencode({
    deadLetterTargetArn = aws_sqs_queue.dlq[each.value].arn
    maxReceiveCount     = 3
  })
}

resource "aws_cloudwatch_log_group" "services" {
  for_each = toset([
    "/taxnet/dev/api",
    "/taxnet/dev/workers/ingestion",
    "/taxnet/dev/workers/identity-resolution",
    "/taxnet/dev/workers/graph-intelligence",
    "/taxnet/dev/workers/risk-scoring",
    "/taxnet/dev/workers/rag-policy",
    "/taxnet/dev/workers/report",
    "/taxnet/dev/workers/audit-log",
    "/taxnet/dev/localstack"
  ])

  name              = each.value
  retention_in_days = 14
}

resource "aws_cloudwatch_metric_alarm" "dlq_depth" {
  for_each            = aws_sqs_queue.dlq
  alarm_name          = "${each.key}-dlq-depth"
  comparison_operator = "GreaterThanThreshold"
  evaluation_periods  = 1
  metric_name         = "ApproximateNumberOfMessagesVisible"
  namespace           = "AWS/SQS"
  period              = 60
  statistic           = "Maximum"
  threshold           = 0
  alarm_description   = "Local alarm for failed TaxNet worker jobs in ${each.key}."

  dimensions = {
    QueueName = each.value.name
  }
}

resource "aws_secretsmanager_secret" "secrets" {
  for_each                = local.model_secret_names
  name                    = each.value
  recovery_window_in_days = 0
  kms_key_id              = aws_kms_key.taxnet.arn
}

resource "aws_secretsmanager_secret_version" "secrets" {
  for_each  = aws_secretsmanager_secret.secrets
  secret_id = each.value.id

  secret_string = jsonencode({
    provider = split("/", each.key)[length(split("/", each.key)) - 1]
    apiKey   = ""
    note     = "LocalStack placeholder. Replace with developer/user secret when available."
  })
}

resource "aws_cognito_user_pool" "taxnet" {
  name = "taxnet-dev-users"

  username_attributes      = ["email"]
  auto_verified_attributes = ["email"]

  password_policy {
    minimum_length    = 10
    require_lowercase = true
    require_numbers   = true
    require_symbols   = false
    require_uppercase = true
  }
}

resource "aws_cognito_resource_server" "taxnet_api" {
  identifier   = "taxnet-api"
  name         = "TaxNet Guardian API"
  user_pool_id = aws_cognito_user_pool.taxnet.id

  scope {
    scope_name        = "cases.read"
    scope_description = "Read compliance cases"
  }

  scope {
    scope_name        = "cases.write"
    scope_description = "Manage compliance cases"
  }

  scope {
    scope_name        = "sandbox.write"
    scope_description = "Feed sandbox datasets"
  }

  scope {
    scope_name        = "rag.write"
    scope_description = "Manage RAG policy content"
  }
}

resource "aws_cognito_user_group" "groups" {
  for_each     = local.cognito_groups
  name         = each.value
  user_pool_id = aws_cognito_user_pool.taxnet.id
  description  = "Local TaxNet Guardian role ${each.value}"
}

resource "aws_cognito_user_pool_client" "spa" {
  name                                 = "taxnet-dev-spa"
  user_pool_id                         = aws_cognito_user_pool.taxnet.id
  generate_secret                      = false
  allowed_oauth_flows_user_pool_client = true
  allowed_oauth_flows                  = ["code"]
  allowed_oauth_scopes                 = ["email", "openid", "profile", "taxnet-api/cases.read", "taxnet-api/cases.write"]
  callback_urls                        = ["http://localhost:5191/callback", "http://localhost:5173/callback"]
  logout_urls                          = ["http://localhost:5191/", "http://localhost:5173/"]
  supported_identity_providers         = ["COGNITO"]
}

resource "aws_cognito_user_pool_client" "service" {
  name                                 = "taxnet-dev-service-client"
  user_pool_id                         = aws_cognito_user_pool.taxnet.id
  generate_secret                      = true
  allowed_oauth_flows_user_pool_client = true
  allowed_oauth_flows                  = ["client_credentials"]
  allowed_oauth_scopes                 = ["taxnet-api/cases.read", "taxnet-api/cases.write", "taxnet-api/sandbox.write", "taxnet-api/rag.write"]
  supported_identity_providers         = ["COGNITO"]

  depends_on = [aws_cognito_resource_server.taxnet_api]
}

resource "aws_sns_topic" "alerts" {
  name              = "taxnet-dev-alerts"
  kms_master_key_id = aws_kms_alias.taxnet.name
}

resource "aws_cloudwatch_event_bus" "taxnet" {
  name = "taxnet-dev-events"
}

resource "aws_iam_role" "worker_role" {
  name = "taxnet-dev-worker-role"

  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect = "Allow"
      Principal = {
        Service = "ecs-tasks.amazonaws.com"
      }
      Action = "sts:AssumeRole"
    }]
  })
}

resource "aws_iam_policy" "worker_policy" {
  name = "taxnet-dev-worker-policy"

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect   = "Allow"
        Action   = ["sqs:*"]
        Resource = concat([for q in aws_sqs_queue.jobs : q.arn], [for q in aws_sqs_queue.dlq : q.arn])
      },
      {
        Effect   = "Allow"
        Action   = ["s3:*"]
        Resource = concat([for b in aws_s3_bucket.buckets : b.arn], [for b in aws_s3_bucket.buckets : "${b.arn}/*"])
      },
      {
        Effect   = "Allow"
        Action   = ["secretsmanager:GetSecretValue", "secretsmanager:DescribeSecret"]
        Resource = [for s in aws_secretsmanager_secret.secrets : s.arn]
      },
      {
        Effect   = "Allow"
        Action   = ["logs:*", "cloudwatch:*", "events:*", "sns:*", "kms:*"]
        Resource = "*"
      }
    ]
  })
}

resource "aws_iam_role_policy_attachment" "worker_policy" {
  role       = aws_iam_role.worker_role.name
  policy_arn = aws_iam_policy.worker_policy.arn
}

