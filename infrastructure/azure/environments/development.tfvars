# Non-secret values only. Apply with: terraform plan -var-file=environments/development.tfvars
# Identifiers below are placeholders; supply real ones through the pipeline or a git-ignored *.auto.tfvars.
environment     = "development"
owner           = "team-platform"
location        = "eastus2"
subscription_id = "00000000-0000-0000-0000-000000000000"
vnet_cidr       = "10.10.0.0/16"

availability_zones               = ["1", "2"]
kubernetes_api_authorized_ranges = ["203.0.113.0/24"]
aks_admin_group_object_ids       = ["00000000-0000-0000-0000-000000000000"]

node_vm_size   = "Standard_D2ds_v5"
node_min_count = 1
node_max_count = 3

postgres_sku_name              = "B_Standard_B2ms"
postgres_storage_mb            = 32768
postgres_backup_retention_days = 7
postgres_high_availability     = false
postgres_entra_admin_object_id = "00000000-0000-0000-0000-000000000000"
postgres_entra_admin_name      = "platform-db-admins"

deletion_protection      = false
storage_replication_type = "LRS"
