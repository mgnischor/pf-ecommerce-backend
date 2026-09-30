# OCI Database with PostgreSQL: private subnet only, regionally durable storage, daily backups.
# The administrator password lives in an OCI Vault secret created out of band; Terraform receives
# only its OCID, so the password never enters code, variables, or state.
resource "oci_psql_db_system" "main" {
  compartment_id = var.compartment_ocid
  display_name   = "${local.name_prefix}-postgres"
  db_version     = var.postgres_db_version
  shape          = var.postgres_shape
  freeform_tags  = local.common_tags

  instance_count              = var.postgres_instance_count
  instance_ocpu_count         = var.postgres_ocpus
  instance_memory_size_in_gbs = var.postgres_memory_gbs

  storage_details {
    is_regionally_durable = true
    system_type           = "OCI_OPTIMIZED_STORAGE"
  }

  network_details {
    subnet_id = oci_core_subnet.db.id
    nsg_ids   = [oci_core_network_security_group.db.id]
  }

  credentials {
    username = var.postgres_admin_username

    password_details {
      password_type  = "VAULT_SECRET"
      secret_id      = var.postgres_admin_secret_id
      secret_version = tostring(var.postgres_admin_secret_version)
    }
  }

  management_policy {
    maintenance_window_start = "SUN 05:00"

    backup_policy {
      kind           = "DAILY"
      backup_start   = "03:00"
      retention_days = var.postgres_backup_retention_days
    }
  }
}
