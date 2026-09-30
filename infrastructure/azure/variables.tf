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

variable "subscription_id" {
  description = "Azure subscription ID (one subscription per environment is recommended)."
  type        = string
}

variable "location" {
  description = "Azure region."
  type        = string
}

variable "vnet_cidr" {
  description = "CIDR block of the virtual network (a /16 is expected)."
  type        = string

  validation {
    condition     = can(cidrhost(var.vnet_cidr, 0))
    error_message = "vnet_cidr must be a valid CIDR block."
  }
}

variable "availability_zones" {
  description = "Availability zones used by AKS and PostgreSQL. Use [] in regions without zones."
  type        = list(string)
  default     = ["1", "2", "3"]
}

variable "kubernetes_version" {
  description = "AKS Kubernetes version (minor, e.g. 1.33). Null uses the AKS default."
  type        = string
  default     = null
}

variable "kubernetes_api_authorized_ranges" {
  description = "CIDR ranges allowed to reach the AKS API server (CI runners, operators). Required."
  type        = list(string)

  validation {
    condition     = length(var.kubernetes_api_authorized_ranges) > 0
    error_message = "Provide at least one authorized CIDR range for the AKS API server."
  }
}

variable "aks_admin_group_object_ids" {
  description = "Entra ID group object IDs granted cluster-admin through Azure RBAC."
  type        = list(string)
}

variable "aks_service_cidr" {
  description = "Kubernetes service CIDR (must not overlap the virtual network)."
  type        = string
  default     = "172.16.0.0/16"
}

variable "aks_dns_service_ip" {
  description = "Kubernetes DNS service IP (inside aks_service_cidr)."
  type        = string
  default     = "172.16.0.10"
}

variable "aks_pod_cidr" {
  description = "Overlay pod CIDR."
  type        = string
  default     = "192.168.0.0/16"
}

variable "node_vm_size" {
  description = "VM size of the default node pool."
  type        = string
  default     = "Standard_D4ds_v5"
}

variable "node_min_count" {
  description = "Minimum number of nodes (cluster autoscaler)."
  type        = number
  default     = 2
}

variable "node_max_count" {
  description = "Maximum number of nodes (cluster autoscaler)."
  type        = number
  default     = 6
}

variable "postgres_version" {
  description = "PostgreSQL major version."
  type        = string
  default     = "17"
}

variable "postgres_sku_name" {
  description = "Flexible Server SKU."
  type        = string
  default     = "GP_Standard_D4ds_v5"
}

variable "postgres_storage_mb" {
  description = "Allocated storage in MB."
  type        = number
  default     = 131072
}

variable "postgres_backup_retention_days" {
  description = "Backup retention (point-in-time recovery window) in days."
  type        = number
  default     = 14
}

variable "postgres_high_availability" {
  description = "Zone-redundant high availability (requires a region with availability zones)."
  type        = bool
  default     = true
}

variable "postgres_geo_redundant_backup" {
  description = "Replicate backups to the paired region."
  type        = bool
  default     = false
}

variable "postgres_entra_admin_object_id" {
  description = "Object ID of the Entra ID user/group/service principal that administers PostgreSQL (password authentication is disabled)."
  type        = string
}

variable "postgres_entra_admin_name" {
  description = "Display name of the Entra ID PostgreSQL administrator."
  type        = string
}

variable "postgres_entra_admin_type" {
  description = "Principal type of the Entra ID PostgreSQL administrator."
  type        = string
  default     = "Group"

  validation {
    condition     = contains(["User", "Group", "ServicePrincipal"], var.postgres_entra_admin_type)
    error_message = "postgres_entra_admin_type must be User, Group, or ServicePrincipal."
  }
}

variable "deletion_protection" {
  description = "Apply CanNotDelete management locks to stateful resources. Must be true in production."
  type        = bool
  default     = true
}

variable "storage_replication_type" {
  description = "Replication of the observability storage account (LRS, ZRS, GRS...)."
  type        = string
  default     = "ZRS"
}

variable "observability_containers" {
  description = "Blob containers for the observability backends."
  type        = set(string)
  default     = ["loki", "tempo", "metrics"]
}

variable "observability_retention_days" {
  description = "Expiration of blobs in the observability containers, in days."
  type        = number
  default     = 90
}
