# Enhanced cluster: required for workload identity, virtual nodes, and add-on management.
resource "oci_containerengine_cluster" "main" {
  compartment_id     = var.compartment_ocid
  name               = "${local.name_prefix}-oke"
  kubernetes_version = var.kubernetes_version
  vcn_id             = oci_core_vcn.main.id
  type               = "ENHANCED_CLUSTER"
  freeform_tags      = local.common_tags

  endpoint_config {
    is_public_ip_enabled = true # reachable only from the CIDRs allowed in the API NSG
    subnet_id            = oci_core_subnet.api.id
    nsg_ids              = [oci_core_network_security_group.api.id]
  }

  cluster_pod_network_options {
    cni_type = "FLANNEL_OVERLAY"
  }

  options {
    service_lb_subnet_ids = [oci_core_subnet.lb.id]

    kubernetes_network_config {
      pods_cidr     = var.pods_cidr
      services_cidr = var.services_cidr
    }

    add_ons {
      is_kubernetes_dashboard_enabled = false
      is_tiller_enabled               = false
    }
  }
}

resource "oci_containerengine_node_pool" "main" {
  compartment_id     = var.compartment_ocid
  cluster_id         = oci_containerengine_cluster.main.id
  name               = "${local.name_prefix}-default"
  kubernetes_version = var.kubernetes_version
  node_shape         = var.node_shape
  freeform_tags      = local.common_tags

  node_shape_config {
    ocpus         = var.node_ocpus
    memory_in_gbs = var.node_memory_gbs
  }

  node_source_details {
    image_id                = var.node_image_id
    source_type             = "IMAGE"
    boot_volume_size_in_gbs = 100
  }

  node_config_details {
    size                                = var.node_count
    nsg_ids                             = [oci_core_network_security_group.nodes.id]
    kms_key_id                          = oci_kms_key.main.id
    is_pv_encryption_in_transit_enabled = true
    freeform_tags                       = local.common_tags

    dynamic "placement_configs" {
      for_each = local.availability_domains

      content {
        availability_domain = placement_configs.value
        subnet_id           = oci_core_subnet.nodes.id
      }
    }

    node_pool_pod_network_option_details {
      cni_type = "FLANNEL_OVERLAY"
    }
  }

  node_eviction_node_pool_settings {
    eviction_grace_duration              = "PT60M"
    is_force_delete_after_grace_duration = false
  }

  node_pool_cycling_details {
    is_node_cycling_enabled = true
    maximum_surge           = "1"
    maximum_unavailable     = "0"
  }

  lifecycle {
    ignore_changes = [node_config_details[0].size]
  }
}
