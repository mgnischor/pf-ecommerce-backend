output "cluster_name" {
  description = "Name of the GKE cluster."
  value       = google_container_cluster.main.name
}

output "cluster_endpoint" {
  description = "Endpoint of the GKE control plane."
  value       = google_container_cluster.main.endpoint
}

output "registry_url" {
  description = "Artifact Registry repository URL."
  value       = "${var.region}-docker.pkg.dev/${var.project_id}/${google_artifact_registry_repository.main.repository_id}"
}

output "postgres_connection_name" {
  description = "Cloud SQL connection name (project:region:instance)."
  value       = google_sql_database_instance.postgres.connection_name
}

output "postgres_private_ip" {
  description = "Private IP address of the Cloud SQL instance."
  value       = google_sql_database_instance.postgres.private_ip_address
}

output "workload_service_accounts" {
  description = "Google service accounts for the workload roles (api, migrator)."
  value       = { for k, sa in google_service_account.workload : k => sa.email }
}

output "valkey_discovery_endpoints" {
  description = "Discovery endpoints of the Valkey instance."
  value       = google_memorystore_instance.valkey.discovery_endpoints
}

output "secret_ids" {
  description = "IDs of the application secret containers."
  value       = { for k, s in google_secret_manager_secret.app : k => s.secret_id }
}

output "observability_buckets" {
  description = "Names of the observability object-storage buckets."
  value       = { for k, b in google_storage_bucket.observability : k => b.name }
}
