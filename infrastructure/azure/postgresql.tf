resource "azurerm_private_dns_zone" "postgres" {
  name                = local.postgres_zone_name
  resource_group_name = azurerm_resource_group.main.name
  tags                = local.common_tags
}

resource "azurerm_private_dns_zone_virtual_network_link" "postgres" {
  name                  = "postgres-link"
  resource_group_name   = azurerm_resource_group.main.name
  private_dns_zone_name = azurerm_private_dns_zone.postgres.name
  virtual_network_id    = azurerm_virtual_network.main.id
  tags                  = local.common_tags
}

resource "azurerm_postgresql_flexible_server" "main" {
  name                = "${local.name_prefix}-postgres"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  version             = var.postgres_version
  sku_name            = var.postgres_sku_name
  storage_mb          = var.postgres_storage_mb
  zone                = length(var.availability_zones) > 0 ? var.availability_zones[0] : null
  tags                = local.common_tags

  # Private access only: the server lives in a delegated subnet and resolves through private DNS.
  delegated_subnet_id           = azurerm_subnet.postgres.id
  private_dns_zone_id           = azurerm_private_dns_zone.postgres.id
  public_network_access_enabled = false

  backup_retention_days        = var.postgres_backup_retention_days
  geo_redundant_backup_enabled = var.postgres_geo_redundant_backup

  # Entra ID authentication only: no administrator password exists, so none enters code or state.
  # Applications use workload identity and request short-lived access tokens.
  authentication {
    active_directory_auth_enabled = true
    password_auth_enabled         = false
    tenant_id                     = data.azurerm_client_config.current.tenant_id
  }

  dynamic "high_availability" {
    for_each = var.postgres_high_availability ? [1] : []

    content {
      mode                      = "ZoneRedundant"
      standby_availability_zone = length(var.availability_zones) > 1 ? var.availability_zones[1] : null
    }
  }

  maintenance_window {
    day_of_week  = 0
    start_hour   = 5
    start_minute = 0
  }

  lifecycle {
    ignore_changes = [zone]
  }

  depends_on = [azurerm_private_dns_zone_virtual_network_link.postgres]
}

resource "azurerm_postgresql_flexible_server_active_directory_administrator" "main" {
  server_name         = azurerm_postgresql_flexible_server.main.name
  resource_group_name = azurerm_resource_group.main.name
  tenant_id           = data.azurerm_client_config.current.tenant_id
  object_id           = var.postgres_entra_admin_object_id
  principal_name      = var.postgres_entra_admin_name
  principal_type      = var.postgres_entra_admin_type
}

resource "azurerm_postgresql_flexible_server_database" "ecommerce" {
  name      = "ecommerce"
  server_id = azurerm_postgresql_flexible_server.main.id
  charset   = "UTF8"
  collation = "en_US.utf8"
}

resource "azurerm_postgresql_flexible_server_configuration" "main" {
  for_each = {
    "azure.extensions"                    = "PG_STAT_STATEMENTS"
    "shared_preload_libraries"            = "pg_stat_statements"
    "require_secure_transport"            = "on"
    "log_min_duration_statement"          = "500"
    "log_lock_waits"                      = "on"
    "idle_in_transaction_session_timeout" = "60000"
  }

  name      = each.key
  server_id = azurerm_postgresql_flexible_server.main.id
  value     = each.value
}

resource "azurerm_management_lock" "postgres" {
  count = var.deletion_protection ? 1 : 0

  name       = "protect-postgres"
  scope      = azurerm_postgresql_flexible_server.main.id
  lock_level = "CanNotDelete"
  notes      = "Stateful resource: remove the lock through a reviewed change before deleting."
}
