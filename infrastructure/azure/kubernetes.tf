resource "azurerm_kubernetes_cluster" "main" {
  name                = local.cluster_name
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  dns_prefix          = local.cluster_name
  kubernetes_version  = var.kubernetes_version
  sku_tier            = "Standard"
  tags                = local.common_tags

  automatic_upgrade_channel = "patch"
  node_os_upgrade_channel   = "NodeImage"

  # Identity and access: Entra ID + Azure RBAC, no local accounts, workload identity enabled.
  local_account_disabled            = true
  role_based_access_control_enabled = true
  oidc_issuer_enabled               = true
  workload_identity_enabled         = true

  azure_active_directory_role_based_access_control {
    azure_rbac_enabled     = true
    admin_group_object_ids = var.aks_admin_group_object_ids
    tenant_id              = data.azurerm_client_config.current.tenant_id
  }

  identity {
    type = "SystemAssigned"
  }

  default_node_pool {
    name                        = "system"
    vm_size                     = var.node_vm_size
    vnet_subnet_id              = azurerm_subnet.aks.id
    zones                       = var.availability_zones
    auto_scaling_enabled        = true
    min_count                   = var.node_min_count
    max_count                   = var.node_max_count
    temporary_name_for_rotation = "systemtmp"

    upgrade_settings {
      max_surge = "33%"
    }
  }

  # Azure CNI overlay with Cilium: pod IPs do not consume VNet space and network policies
  # (default-deny, see CONTAINERS.md §7.3) are enforced by the data plane.
  network_profile {
    network_plugin      = "azure"
    network_plugin_mode = "overlay"
    network_policy      = "cilium"
    network_data_plane  = "cilium"
    load_balancer_sku   = "standard"
    outbound_type       = "loadBalancer"
    service_cidr        = var.aks_service_cidr
    dns_service_ip      = var.aks_dns_service_ip
    pod_cidr            = var.aks_pod_cidr
  }

  api_server_access_profile {
    authorized_ip_ranges = var.kubernetes_api_authorized_ranges
  }

  azure_policy_enabled         = true
  image_cleaner_enabled        = true
  image_cleaner_interval_hours = 48

  lifecycle {
    ignore_changes = [default_node_pool[0].node_count]
  }
}

# Nodes pull images from the registry with the kubelet identity (no credentials).
resource "azurerm_role_assignment" "aks_acr_pull" {
  scope                            = azurerm_container_registry.main.id
  role_definition_name             = "AcrPull"
  principal_id                     = azurerm_kubernetes_cluster.main.kubelet_identity[0].object_id
  skip_service_principal_aad_check = true
}
