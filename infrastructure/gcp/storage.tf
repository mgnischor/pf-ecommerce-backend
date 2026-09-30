# Object storage for the observability backends (Loki, Tempo, long-term metrics).
resource "google_storage_bucket" "observability" {
  for_each = var.observability_buckets

  name                        = "${var.project_id}-${local.name_prefix}-${each.key}"
  location                    = var.region
  storage_class               = "STANDARD"
  uniform_bucket_level_access = true
  public_access_prevention    = "enforced"
  force_destroy               = false
  labels                      = local.labels

  versioning {
    enabled = true
  }

  lifecycle_rule {
    action {
      type = "Delete"
    }

    condition {
      age = var.observability_retention_days
    }
  }

  lifecycle_rule {
    action {
      type = "Delete"
    }

    condition {
      days_since_noncurrent_time = 7
      with_state                 = "ARCHIVED"
    }
  }
}
