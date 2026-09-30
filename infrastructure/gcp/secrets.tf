# Secret containers only. Versions (the values) are added out of band so that no secret value
# ever enters Terraform code, variables, plans, or state.
resource "google_secret_manager_secret" "app" {
  for_each = var.secret_names

  secret_id = "${local.name_prefix}-${each.key}"
  labels    = local.labels

  replication {
    auto {}
  }

  depends_on = [google_project_service.required]
}

# External Secrets Operator reads these secrets (and only these) through Workload Identity.
resource "google_secret_manager_secret_iam_member" "external_secrets" {
  for_each = google_secret_manager_secret.app

  project   = var.project_id
  secret_id = each.value.secret_id
  role      = "roles/secretmanager.secretAccessor"
  member    = local.external_secrets_principal
}
