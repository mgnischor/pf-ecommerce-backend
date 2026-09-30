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
  description = "Owning team; used for the owner label (lowercase letters, digits, hyphens, underscores)."
  type        = string
}

variable "git_sha" {
  description = "Commit SHA (or pipeline run ID) that produced this change; used for the revision label (lowercase)."
  type        = string
  default     = "local"
}

variable "project_id" {
  description = "Google Cloud project ID (one project per environment is recommended)."
  type        = string
}

variable "region" {
  description = "Google Cloud region."
  type        = string
}

variable "subnet_cidr" {
  description = "Primary CIDR range of the workload subnet."
  type        = string

  validation {
    condition     = can(cidrhost(var.subnet_cidr, 0))
    error_message = "subnet_cidr must be a valid CIDR block."
  }
}

variable "pods_cidr" {
  description = "Secondary CIDR range for GKE pods."
  type        = string
  default     = "10.64.0.0/14"
}

variable "services_cidr" {
  description = "Secondary CIDR range for GKE services."
  type        = string
  default     = "10.68.0.0/20"
}

variable "master_ipv4_cidr" {
  description = "/28 CIDR range for the GKE control plane private endpoint."
  type        = string
  default     = "172.16.0.0/28"
}

variable "master_authorized_networks" {
  description = "Networks allowed to reach the GKE control plane endpoint (CI runners, operators). Required."
  type = list(object({
    cidr_block   = string
    display_name = string
  }))

  validation {
    condition     = length(var.master_authorized_networks) > 0
    error_message = "Provide at least one authorized network for the GKE control plane."
  }
}

variable "postgres_version" {
  description = "Cloud SQL database version."
  type        = string
  default     = "POSTGRES_17"
}

variable "postgres_tier" {
  description = "Cloud SQL machine tier."
  type        = string
  default     = "db-custom-4-16384"
}

variable "postgres_disk_size_gb" {
  description = "Initial disk size in GB (autoresize is enabled)."
  type        = number
  default     = 100
}

variable "postgres_availability_type" {
  description = "REGIONAL (high availability) or ZONAL."
  type        = string
  default     = "REGIONAL"

  validation {
    condition     = contains(["REGIONAL", "ZONAL"], var.postgres_availability_type)
    error_message = "postgres_availability_type must be REGIONAL or ZONAL."
  }
}

variable "postgres_backup_retention_count" {
  description = "Number of automated backups to retain."
  type        = number
  default     = 14
}

variable "deletion_protection" {
  description = "Protect stateful resources from deletion. Must be true in production."
  type        = bool
  default     = true
}

variable "valkey_node_type" {
  description = "Memorystore for Valkey node type."
  type        = string
  default     = "STANDARD_SMALL"
}

variable "valkey_replica_count" {
  description = "Number of Valkey replicas (0 disables high availability)."
  type        = number
  default     = 1
}

variable "secret_names" {
  description = "Names of the secret containers to create (versions are added out of band, never by Terraform)."
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
