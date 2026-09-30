# Cloud SQL instance names cannot be reused for about a week after deletion: add a random suffix.
resource "random_id" "postgres" {
  byte_length = 3
}

resource "google_sql_database_instance" "postgres" {
  name                = "${local.name_prefix}-pg-${random_id.postgres.hex}"
  region              = var.region
  database_version    = var.postgres_version
  deletion_protection = var.deletion_protection

  settings {
    tier                        = var.postgres_tier
    edition                     = "ENTERPRISE"
    availability_type           = var.postgres_availability_type
    disk_type                   = "PD_SSD"
    disk_size                   = var.postgres_disk_size_gb
    disk_autoresize             = true
    deletion_protection_enabled = var.deletion_protection
    user_labels                 = local.labels

    # Private IP only, TLS required.
    ip_configuration {
      ipv4_enabled    = false
      private_network = google_compute_network.main.id
      ssl_mode        = "ENCRYPTED_ONLY"
    }

    backup_configuration {
      enabled                        = true
      point_in_time_recovery_enabled = true
      start_time                     = "03:00"
      transaction_log_retention_days = 7

      backup_retention_settings {
        retained_backups = var.postgres_backup_retention_count
        retention_unit   = "COUNT"
      }
    }

    # IAM database authentication: workloads use short-lived tokens, no database passwords.
    database_flags {
      name  = "cloudsql.iam_authentication"
      value = "on"
    }

    database_flags {
      name  = "log_min_duration_statement"
      value = "500"
    }

    database_flags {
      name  = "log_lock_waits"
      value = "on"
    }

    database_flags {
      name  = "idle_in_transaction_session_timeout"
      value = "60000"
    }

    insights_config {
      query_insights_enabled  = true
      record_application_tags = false
      record_client_address   = false
    }

    maintenance_window {
      day          = 7
      hour         = 5
      update_track = "stable"
    }
  }

  depends_on = [google_service_networking_connection.private_services]
}

resource "google_sql_database" "ecommerce" {
  name     = "ecommerce"
  instance = google_sql_database_instance.postgres.name
}

# One identity per role: the runtime identity cannot change the schema; the migrator can.
resource "google_service_account" "workload" {
  for_each = {
    api      = "ecommerce-api"
    migrator = "ecommerce-migrator"
  }

  account_id   = "${var.environment}-${each.key}"
  display_name = "${local.name_prefix} ${each.key}"

  depends_on = [google_project_service.required]
}

resource "google_project_iam_member" "workload_cloudsql" {
  for_each = {
    for pair in setproduct(keys(google_service_account.workload), ["roles/cloudsql.client", "roles/cloudsql.instanceUser"]) :
    "${pair[0]}-${pair[1]}" => { name = pair[0], role = pair[1] }
  }

  project = var.project_id
  role    = each.value.role
  member  = "serviceAccount:${google_service_account.workload[each.value.name].email}"
}

resource "google_sql_user" "workload" {
  for_each = google_service_account.workload

  name     = trimsuffix(each.value.email, ".gserviceaccount.com")
  instance = google_sql_database_instance.postgres.name
  type     = "CLOUD_IAM_SERVICE_ACCOUNT"
}

# Workload Identity: the Kubernetes service accounts below impersonate the Google service accounts.
resource "google_service_account_iam_member" "workload_identity" {
  for_each = {
    api      = "ecommerce/ecommerce-api"
    migrator = "ecommerce/ecommerce-migrator"
  }

  service_account_id = google_service_account.workload[each.key].name
  role               = "roles/iam.workloadIdentityUser"
  member             = "serviceAccount:${local.workload_pool}[${each.value}]"

  depends_on = [google_container_cluster.main]
}
