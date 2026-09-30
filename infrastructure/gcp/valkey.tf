# Memorystore for Valkey is reached through Private Service Connect.
resource "google_network_connectivity_service_connection_policy" "memorystore" {
  name          = "${local.name_prefix}-memorystore"
  location      = var.region
  service_class = "gcp-memorystore"
  description   = "Private Service Connect policy for Memorystore for Valkey"
  network       = google_compute_network.main.id

  psc_config {
    subnetworks = [google_compute_subnetwork.workloads.id]
  }

  depends_on = [google_project_service.required]
}

resource "google_memorystore_instance" "valkey" {
  instance_id                 = "${local.name_prefix}-valkey"
  location                    = var.region
  shard_count                 = 1
  replica_count               = var.valkey_replica_count
  node_type                   = var.valkey_node_type
  engine_version              = "VALKEY_8_0"
  mode                        = "CLUSTER_DISABLED"
  authorization_mode          = "IAM_AUTH"
  transit_encryption_mode     = "SERVER_AUTHENTICATION"
  deletion_protection_enabled = var.deletion_protection
  labels                      = local.labels

  desired_psc_auto_connections {
    network    = google_compute_network.main.id
    project_id = var.project_id
  }

  zone_distribution_config {
    mode = var.valkey_replica_count > 0 ? "MULTI_ZONE" : "SINGLE_ZONE"
  }

  depends_on = [google_network_connectivity_service_connection_policy.memorystore]
}
