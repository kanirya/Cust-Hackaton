output "localstack_endpoint" {
  value = var.localstack_endpoint
}

output "sqs_queue_urls" {
  value = { for name, queue in aws_sqs_queue.jobs : name => queue.url }
}

output "s3_buckets" {
  value = keys(aws_s3_bucket.buckets)
}

output "cognito_user_pool_id" {
  value = aws_cognito_user_pool.taxnet.id
}

output "cognito_spa_client_id" {
  value = aws_cognito_user_pool_client.spa.id
}

output "cognito_service_client_id" {
  value = aws_cognito_user_pool_client.service.id
}

output "secret_names" {
  value = keys(aws_secretsmanager_secret.secrets)
}

