# Non-secret values only. Apply with: terraform plan -var-file=environments/development.tfvars
# Identifiers below are placeholders; supply real ones through the pipeline or a git-ignored *.auto.tfvars.
environment = "development"
owner       = "team-platform"
project_id  = "pf-ecommerce-dev"
region      = "us-central1"
subnet_cidr = "10.10.0.0/20"

master_authorized_networks = [
  { cidr_block = "203.0.113.0/24", display_name = "ci-runners" },
]

postgres_tier                   = "db-custom-2-7680"
postgres_disk_size_gb           = 20
postgres_availability_type      = "ZONAL"
postgres_backup_retention_count = 7
deletion_protection             = false

valkey_node_type     = "SHARED_CORE_NANO"
valkey_replica_count = 0
