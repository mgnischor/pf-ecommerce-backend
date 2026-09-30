data "azurerm_client_config" "current" {}

locals {
  name_prefix = "${var.project_name}-${var.environment}"

  # Globally unique, alphanumeric names for registry, key vault, and storage account.
  unique_suffix = substr(sha1("${data.azurerm_client_config.current.subscription_id}/${local.name_prefix}"), 0, 6)
  compact_name  = replace(local.name_prefix, "-", "")

  acr_name           = "${local.compact_name}${local.unique_suffix}"
  key_vault_name     = substr("kv-${local.compact_name}-${local.unique_suffix}", 0, 24)
  storage_account    = substr("st${local.compact_name}${local.unique_suffix}", 0, 24)
  cluster_name       = "${local.name_prefix}-aks"
  postgres_zone_name = "${local.name_prefix}.private.postgres.database.azure.com"

  common_tags = {
    environment = var.environment
    project     = var.project_name
    owner       = var.owner
    managed-by  = "terraform"
    revision    = var.git_sha
  }

  # Subnets carved from the /16 virtual network.
  aks_subnet_cidr      = cidrsubnet(var.vnet_cidr, 4, 0)  # /20 nodes
  postgres_subnet_cidr = cidrsubnet(var.vnet_cidr, 8, 32) # /24 delegated to PostgreSQL
  endpoint_subnet_cidr = cidrsubnet(var.vnet_cidr, 8, 33) # /24 private endpoints

  # Private endpoints for services that must not be reachable from the internet.
  private_endpoints = {
    registry = {
      resource_id = azurerm_container_registry.main.id
      subresource = "registry"
      dns_zone    = "privatelink.azurecr.io"
    }
    vault = {
      resource_id = azurerm_key_vault.main.id
      subresource = "vault"
      dns_zone    = "privatelink.vaultcore.azure.net"
    }
    blob = {
      resource_id = azurerm_storage_account.observability.id
      subresource = "blob"
      dns_zone    = "privatelink.blob.core.windows.net"
    }
  }
}
