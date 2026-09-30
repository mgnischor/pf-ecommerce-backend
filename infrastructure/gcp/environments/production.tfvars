# Non-secret values only. Apply with: terraform plan -var-file=environments/production.tfvars
# Identifiers below are placeholders; supply real ones through the pipeline or a git-ignored *.auto.tfvars.
environment = "production"
owner       = "team-platform"
project_id  = "pf-ecommerce-prod"
region      = "us-central1"
subnet_cidr = "10.30.0.0/20"

master_authorized_networks = [
  { cidr_block = "203.0.113.0/24", display_name = "ci-runners" },
]

postgres_tier                   = "db-custom-8-32768"
postgres_disk_size_gb           = 200
postgres_availability_type      = "REGIONAL"
postgres_backup_retention_count = 35
deletion_protection             = true

valkey_node_type     = "STANDARD_SMALL"
valkey_replica_count = 2
