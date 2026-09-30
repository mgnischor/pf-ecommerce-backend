data "google_project" "current" {
  project_id = var.project_id
}

locals {
  name_prefix = "${var.project_name}-${var.environment}"

  labels = {
    environment = var.environment
    project     = var.project_name
    owner       = var.owner
    managed-by  = "terraform"
    revision    = lower(var.git_sha)
  }

  # Principal of a Kubernetes service account through GKE Workload Identity Federation.
  workload_pool = "${var.project_id}.svc.id.goog"

  external_secrets_principal = "principal://iam.googleapis.com/projects/${data.google_project.current.number}/locations/global/workloadIdentityPools/${local.workload_pool}/subject/ns/external-secrets/sa/external-secrets"
}
