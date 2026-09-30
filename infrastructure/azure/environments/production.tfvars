# Non-secret values only. Apply with: terraform plan -var-file=environments/production.tfvars
# Identifiers below are placeholders; supply real ones through the pipeline or a git-ignored *.auto.tfvars.
environment     = "production"
owner           = "team-platform"
location        = "eastus2"
subscription_id = "00000000-0000-0000-0000-000000000000"
vnet_cidr       = "10.30.0.0/16"

kubernetes_api_authorized_ranges = ["203.0.113.0/24"]
aks_admin_group_object_ids       = ["00000000-0000-0000-0000-000000000000"]

node_vm_size   = "Standard_D8ds_v5"
node_min_count = 3
node_max_count = 12

postgres_sku_name              = "GP_Standard_D4ds_v5"
postgres_storage_mb            = 262144
postgres_backup_retention_days = 35
postgres_high_availability     = true
postgres_geo_redundant_backup  = true
postgres_entra_admin_object_id = "00000000-0000-0000-0000-000000000000"
postgres_entra_admin_name      = "platform-db-admins"

deletion_protection      = true
storage_replication_type = "GZRS"
