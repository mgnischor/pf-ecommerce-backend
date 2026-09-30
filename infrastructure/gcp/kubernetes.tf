# Dedicated node service account with the minimum roles GKE nodes need.
resource "google_service_account" "gke_nodes" {
  account_id   = "${var.environment}-gke-nodes"
  display_name = "${local.name_prefix} GKE nodes"

  depends_on = [google_project_service.required]
}

resource "google_project_iam_member" "gke_nodes" {
  for_each = toset([
    "roles/logging.logWriter",
    "roles/monitoring.metricWriter",
    "roles/monitoring.viewer",
    "roles/stackdriver.resourceMetadata.writer",
  ])

  project = var.project_id
  role    = each.value
  member  = "serviceAccount:${google_service_account.gke_nodes.email}"
}

# Autopilot: Google manages nodes, scaling, hardening, and upgrades.
resource "google_container_cluster" "main" {
  name                = "${local.name_prefix}-gke"
  location            = var.region
  enable_autopilot    = true
  network             = google_compute_network.main.id
  subnetwork          = google_compute_subnetwork.workloads.id
  deletion_protection = var.deletion_protection
  resource_labels     = local.labels

  ip_allocation_policy {
    cluster_secondary_range_name  = "pods"
    services_secondary_range_name = "services"
  }

  private_cluster_config {
    enable_private_nodes    = true
    enable_private_endpoint = false
    master_ipv4_cidr_block  = var.master_ipv4_cidr
  }

  master_authorized_networks_config {
    dynamic "cidr_blocks" {
      for_each = var.master_authorized_networks

      content {
        cidr_block   = cidr_blocks.value.cidr_block
        display_name = cidr_blocks.value.display_name
      }
    }
  }

  workload_identity_config {
    workload_pool = local.workload_pool
  }

  release_channel {
    channel = "REGULAR"
  }

  cluster_autoscaling {
    auto_provisioning_defaults {
      service_account = google_service_account.gke_nodes.email
      oauth_scopes    = ["https://www.googleapis.com/auth/cloud-platform"]
    }
  }

  security_posture_config {
    mode               = "BASIC"
    vulnerability_mode = "VULNERABILITY_BASIC"
  }

  depends_on = [
    google_project_service.required,
    google_project_iam_member.gke_nodes,
  ]
}
