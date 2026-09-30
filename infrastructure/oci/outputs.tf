output "cluster_id" {
  description = "OCID of the OKE cluster."
  value       = oci_containerengine_cluster.main.id
}

output "cluster_endpoints" {
  description = "Endpoints of the OKE cluster."
  value       = oci_containerengine_cluster.main.endpoints
}

output "registry_repository" {
  description = "OCIR repository name (prefix with <region-key>.ocir.io/<namespace>/ to pull or push)."
  value       = oci_artifacts_container_repository.api.display_name
}

output "postgres_db_system_id" {
  description = "OCID of the PostgreSQL database system."
  value       = oci_psql_db_system.main.id
}

output "vault_id" {
  description = "OCID of the OCI Vault holding application secrets."
  value       = oci_kms_vault.main.id
}

output "kms_key_id" {
  description = "OCID of the platform master encryption key."
  value       = oci_kms_key.main.id
}

output "observability_buckets" {
  description = "Names of the observability object-storage buckets."
  value       = { for k, b in oci_objectstorage_bucket.observability : k => b.name }
}
