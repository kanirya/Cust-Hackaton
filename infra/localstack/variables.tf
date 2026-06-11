variable "aws_region" {
  type    = string
  default = "us-east-1"
}

variable "localstack_endpoint" {
  type    = string
  default = "http://localhost:4566"
}

variable "environment" {
  type    = string
  default = "dev"
}

variable "project" {
  type    = string
  default = "taxnet"
}

variable "enable_cognito" {
  type        = bool
  default     = false
  description = "Enable Cognito resources when the LocalStack edition supports cognito-idp."
}
