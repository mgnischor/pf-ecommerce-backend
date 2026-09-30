# Non-secret values only. Apply with: terraform plan -var-file=environments/staging.tfvars
# Identifiers below are placeholders; supply real ones through the pipeline or a git-ignored *.auto.tfvars.
environment = "staging"
owner       = "team-platform"
project_id  = "pf-ecommerce-stg"
region      = "us-central1"
subnet_cidr = "10.20.0.0/20"
pods_cidr   = "10.72.0.0/14"

services_cidr    = "10.76.0.0/20"
master_ipv4_cidr = "172.16.0.16/28"

master_authorized_networks = [
  { cidr_block = "203.0.113.0/24", display_name = "ci-runners" },
]

postgres_tier                   = "db-custom-4-16384"
postgres_disk_size_gb           = 50
postgres_availability_type      = "REGIONAL"
postgres_backup_retention_count = 14
deletion_protection             = true

valkey_node_type     = "STANDARD_SMALL"
valkey_replica_count = 1
