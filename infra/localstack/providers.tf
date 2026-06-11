provider "aws" {
  region                      = var.aws_region
  access_key                  = "test"
  secret_key                  = "test"
  s3_use_path_style           = true
  skip_credentials_validation = true
  skip_metadata_api_check     = true
  skip_requesting_account_id  = true

  endpoints {
    cloudwatch     = var.localstack_endpoint
    cloudwatchlogs = var.localstack_endpoint
    cognitoidp     = var.localstack_endpoint
    events         = var.localstack_endpoint
    iam            = var.localstack_endpoint
    kms            = var.localstack_endpoint
    s3             = var.localstack_endpoint
    secretsmanager = var.localstack_endpoint
    sns            = var.localstack_endpoint
    sqs            = var.localstack_endpoint
    sts            = var.localstack_endpoint
  }

  default_tags {
    tags = {
      Project     = "TaxNetGuardian"
      Environment = var.environment
      ManagedBy   = "TerraformLocalStack"
    }
  }
}

