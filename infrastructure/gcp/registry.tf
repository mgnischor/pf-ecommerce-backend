resource "google_artifact_registry_repository" "main" {
  location      = var.region
  repository_id = "${local.name_prefix}-images"
  description   = "Container images for ${local.name_prefix}"
  format        = "DOCKER"
  labels        = local.labels

  docker_config {
    immutable_tags = true
  }

  cleanup_policy_dry_run = false

  cleanup_policies {
    id     = "delete-untagged-after-14-days"
    action = "DELETE"

    condition {
      tag_state  = "UNTAGGED"
      older_than = "1209600s"
    }
  }

  depends_on = [google_project_service.required]
}

# GKE nodes pull images with their service account (no credentials).
resource "google_artifact_registry_repository_iam_member" "gke_nodes_reader" {
  project    = var.project_id
  location   = google_artifact_registry_repository.main.location
  repository = google_artifact_registry_repository.main.name
  role       = "roles/artifactregistry.reader"
  member     = "serviceAccount:${google_service_account.gke_nodes.email}"
}
