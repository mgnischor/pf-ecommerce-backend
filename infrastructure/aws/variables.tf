variable "project_name" {
  description = "Project name used as the first segment of every resource name."
  type        = string
  default     = "pf-ecommerce"
}

variable "environment" {
  description = "Deployment environment."
  type        = string

  validation {
    condition     = contains(["development", "staging", "production"], var.environment)
    error_message = "environment must be development, staging, or production."
  }
}

variable "owner" {
  description = "Owning team; used for the owner tag."
  type        = string
}

variable "git_sha" {
  description = "Commit SHA (or pipeline run ID) that produced this change; used for the revision tag."
  type        = string
  default     = "local"
}

variable "region" {
  description = "AWS region."
  type        = string
}

variable "vpc_cidr" {
  description = "CIDR block of the VPC (a /16 is expected)."
  type        = string

  validation {
    condition     = can(cidrhost(var.vpc_cidr, 0))
    error_message = "vpc_cidr must be a valid CIDR block."
  }
}

variable "az_count" {
  description = "Number of availability zones to use (2 or 3)."
  type        = number
  default     = 3

  validation {
    condition     = var.az_count >= 2 && var.az_count <= 3
    error_message = "az_count must be 2 or 3."
  }
}

variable "kubernetes_version" {
  description = "EKS Kubernetes minor version."
  type        = string
  default     = "1.33"
}

variable "kubernetes_api_allowed_cidrs" {
  description = "CIDR blocks allowed to reach the public EKS API endpoint. Empty keeps the endpoint private-only."
  type        = list(string)
  default     = []
}

variable "node_instance_types" {
  description = "EC2 instance types of the managed node group."
  type        = list(string)
  default     = ["m7i.large"]
}

variable "node_min_size" {
  description = "Minimum number of worker nodes."
  type        = number
  default     = 2
}

variable "node_max_size" {
  description = "Maximum number of worker nodes."
  type        = number
  default     = 6
}

variable "node_desired_size" {
  description = "Initial number of worker nodes (the autoscaler owns the value afterwards)."
  type        = number
  default     = 2
}

variable "postgres_engine_version" {
  description = "PostgreSQL major version."
  type        = string
  default     = "17"
}

variable "postgres_instance_class" {
  description = "RDS instance class."
  type        = string
  default     = "db.m7g.large"
}

variable "postgres_allocated_storage_gb" {
  description = "Initial allocated storage in GiB."
  type        = number
  default     = 50
}

variable "postgres_max_allocated_storage_gb" {
  description = "Upper bound for storage autoscaling in GiB."
  type        = number
  default     = 500
}

variable "postgres_multi_az" {
  description = "Run a synchronous standby in a second availability zone."
  type        = bool
  default     = true
}

variable "postgres_backup_retention_days" {
  description = "Automated backup retention (point-in-time recovery window) in days."
  type        = number
  default     = 14
}

variable "deletion_protection" {
  description = "Protect stateful resources from deletion. Must be true in production."
  type        = bool
  default     = true
}

variable "valkey_node_type" {
  description = "ElastiCache node type for Valkey."
  type        = string
  default     = "cache.m7g.large"
}

variable "valkey_replicas" {
  description = "Number of read replicas (0 disables automatic failover and Multi-AZ)."
  type        = number
  default     = 1
}

variable "secret_names" {
  description = "Names of the secret containers to create (values are injected out of band, never by Terraform)."
  type        = set(string)
  default     = ["jwt-signing-key", "rabbitmq-credentials", "payment-provider-api-key", "payment-webhook-secret"]
}

variable "observability_buckets" {
  description = "Object storage buckets for the observability backends."
  type        = set(string)
  default     = ["loki", "tempo", "metrics"]
}

variable "observability_retention_days" {
  description = "Expiration of objects in the observability buckets, in days."
  type        = number
  default     = 90
}
