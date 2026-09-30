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

variable "tenancy_ocid" {
  description = "OCID of the tenancy."
  type        = string
}

variable "compartment_ocid" {
  description = "OCID of the compartment that holds the environment (one compartment per environment)."
  type        = string
}

variable "region" {
  description = "OCI region identifier (for example us-ashburn-1)."
  type        = string
}

variable "vcn_cidr" {
  description = "CIDR block of the VCN (a /16 is expected)."
  type        = string

  validation {
    condition     = can(cidrhost(var.vcn_cidr, 0))
    error_message = "vcn_cidr must be a valid CIDR block."
  }
}

variable "kubernetes_version" {
  description = "OKE Kubernetes version, including the leading v (for example v1.33.1)."
  type        = string
}

variable "kubernetes_api_allowed_cidrs" {
  description = "CIDR blocks allowed to reach the OKE API endpoint (CI runners, operators). Required."
  type        = list(string)

  validation {
    condition     = length(var.kubernetes_api_allowed_cidrs) > 0
    error_message = "Provide at least one allowed CIDR for the Kubernetes API endpoint."
  }
}

variable "pods_cidr" {
  description = "Overlay pod CIDR (must not overlap the VCN)."
  type        = string
  default     = "10.244.0.0/16"
}

variable "services_cidr" {
  description = "Kubernetes service CIDR (must not overlap the VCN)."
  type        = string
  default     = "10.96.0.0/16"
}

variable "node_shape" {
  description = "Compute shape of worker nodes."
  type        = string
  default     = "VM.Standard.E5.Flex"
}

variable "node_ocpus" {
  description = "OCPUs per worker node (flexible shapes)."
  type        = number
  default     = 4
}

variable "node_memory_gbs" {
  description = "Memory in GB per worker node (flexible shapes)."
  type        = number
  default     = 32
}

variable "node_count" {
  description = "Number of worker nodes (spread across availability domains)."
  type        = number
  default     = 3
}

variable "node_image_id" {
  description = "OCID of an OKE-compatible node image for the chosen Kubernetes version and shape (see `oci ce node-pool-options get`)."
  type        = string
}

variable "postgres_db_version" {
  description = "PostgreSQL major version offered by OCI Database with PostgreSQL in the region."
  type        = string
  default     = "16"
}

variable "postgres_shape" {
  description = "Database system shape."
  type        = string
  default     = "PostgreSQL.VM.Standard.E5.Flex.4.64GB"
}

variable "postgres_ocpus" {
  description = "OCPUs per database instance (must match the shape)."
  type        = number
  default     = 4
}

variable "postgres_memory_gbs" {
  description = "Memory in GB per database instance (must match the shape)."
  type        = number
  default     = 64
}

variable "postgres_instance_count" {
  description = "Number of database instances (1 primary, plus replicas for high availability; maximum 3)."
  type        = number
  default     = 2
}

variable "postgres_admin_username" {
  description = "Administrator user name."
  type        = string
  default     = "app_admin"
}

variable "postgres_admin_secret_id" {
  description = "OCID of a pre-existing OCI Vault secret holding the administrator password (created out of band so the value never enters state)."
  type        = string
}

variable "postgres_admin_secret_version" {
  description = "Version number of the Vault secret to use; increment after rotation."
  type        = number
  default     = 1
}

variable "postgres_backup_retention_days" {
  description = "Backup retention (point-in-time recovery window) in days."
  type        = number
  default     = 14
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
