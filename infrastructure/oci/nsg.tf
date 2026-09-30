# Network security groups: least-privilege rules per resource (default deny; nothing else is open).
resource "oci_core_network_security_group" "api" {
  compartment_id = var.compartment_ocid
  vcn_id         = oci_core_vcn.main.id
  display_name   = "${local.name_prefix}-api-nsg"
  freeform_tags  = local.common_tags
}

resource "oci_core_network_security_group" "nodes" {
  compartment_id = var.compartment_ocid
  vcn_id         = oci_core_vcn.main.id
  display_name   = "${local.name_prefix}-nodes-nsg"
  freeform_tags  = local.common_tags
}

resource "oci_core_network_security_group" "db" {
  compartment_id = var.compartment_ocid
  vcn_id         = oci_core_vcn.main.id
  display_name   = "${local.name_prefix}-db-nsg"
  freeform_tags  = local.common_tags
}

locals {
  nsg_ids = {
    api   = oci_core_network_security_group.api.id
    nodes = oci_core_network_security_group.nodes.id
    db    = oci_core_network_security_group.db.id
  }

  # protocol: "6" = TCP, "1" = ICMP, "all" = every protocol
  nsg_rules = merge(
    {
      # Kubernetes API endpoint
      "api-in-nodes-6443"    = { nsg = "api", direction = "INGRESS", protocol = "6", peer = "nodes", peer_type = "NETWORK_SECURITY_GROUP", min = 6443, max = 6443 }
      "api-in-nodes-12250"   = { nsg = "api", direction = "INGRESS", protocol = "6", peer = "nodes", peer_type = "NETWORK_SECURITY_GROUP", min = 12250, max = 12250 }
      "api-out-nodes-10250"  = { nsg = "api", direction = "EGRESS", protocol = "6", peer = "nodes", peer_type = "NETWORK_SECURITY_GROUP", min = 10250, max = 10250 }
      "api-out-oci-services" = { nsg = "api", direction = "EGRESS", protocol = "6", peer = local.service_cidr_block, peer_type = "SERVICE_CIDR_BLOCK", min = 443, max = 443 }

      # Worker nodes
      "nodes-in-nodes-all"    = { nsg = "nodes", direction = "INGRESS", protocol = "all", peer = "nodes", peer_type = "NETWORK_SECURITY_GROUP", min = null, max = null }
      "nodes-in-api-10250"    = { nsg = "nodes", direction = "INGRESS", protocol = "6", peer = "api", peer_type = "NETWORK_SECURITY_GROUP", min = 10250, max = 10250 }
      "nodes-in-lb-nodeports" = { nsg = "nodes", direction = "INGRESS", protocol = "6", peer = local.lb_subnet_cidr, peer_type = "CIDR_BLOCK", min = 30000, max = 32767 }
      "nodes-out-all"         = { nsg = "nodes", direction = "EGRESS", protocol = "all", peer = "0.0.0.0/0", peer_type = "CIDR_BLOCK", min = null, max = null }

      # Database
      "db-in-nodes-5432"    = { nsg = "db", direction = "INGRESS", protocol = "6", peer = "nodes", peer_type = "NETWORK_SECURITY_GROUP", min = 5432, max = 5432 }
      "db-out-oci-services" = { nsg = "db", direction = "EGRESS", protocol = "6", peer = local.service_cidr_block, peer_type = "SERVICE_CIDR_BLOCK", min = 443, max = 443 }
    },
    # The API endpoint is reachable only from the allowed CIDR ranges.
    {
      for cidr in var.kubernetes_api_allowed_cidrs :
      "api-in-${replace(cidr, "/", "-")}-6443" => { nsg = "api", direction = "INGRESS", protocol = "6", peer = cidr, peer_type = "CIDR_BLOCK", min = 6443, max = 6443 }
    },
  )
}

resource "oci_core_network_security_group_security_rule" "main" {
  for_each = local.nsg_rules

  network_security_group_id = local.nsg_ids[each.value.nsg]
  direction                 = each.value.direction
  protocol                  = each.value.protocol
  description               = each.key
  stateless                 = false

  source           = each.value.direction == "INGRESS" ? (each.value.peer_type == "NETWORK_SECURITY_GROUP" ? local.nsg_ids[each.value.peer] : each.value.peer) : null
  source_type      = each.value.direction == "INGRESS" ? each.value.peer_type : null
  destination      = each.value.direction == "EGRESS" ? (each.value.peer_type == "NETWORK_SECURITY_GROUP" ? local.nsg_ids[each.value.peer] : each.value.peer) : null
  destination_type = each.value.direction == "EGRESS" ? each.value.peer_type : null

  dynamic "tcp_options" {
    for_each = each.value.protocol == "6" ? [1] : []

    content {
      destination_port_range {
        min = each.value.min
        max = each.value.max
      }
    }
  }
}
