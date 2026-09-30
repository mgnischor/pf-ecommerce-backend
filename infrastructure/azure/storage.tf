# Blob storage for the observability backends (Loki, Tempo, long-term metrics).
# Access is Entra ID only (shared keys disabled), over TLS 1.2+, through a private endpoint.
resource "azurerm_storage_account" "observability" {
  name                            = local.storage_account
  location                        = azurerm_resource_group.main.location
  resource_group_name             = azurerm_resource_group.main.name
  account_tier                    = "Standard"
  account_replication_type        = var.storage_replication_type
  min_tls_version                 = "TLS1_2"
  https_traffic_only_enabled      = true
  allow_nested_items_to_be_public = false
  shared_access_key_enabled       = false
  default_to_oauth_authentication = true
  public_network_access_enabled   = false
  tags                            = local.common_tags

  blob_properties {
    versioning_enabled = true

    delete_retention_policy {
      days = 7
    }
  }

  network_rules {
    default_action = "Deny"
    bypass         = ["AzureServices"]
  }
}

resource "azurerm_storage_container" "observability" {
  for_each = var.observability_containers

  name                  = each.key
  storage_account_id    = azurerm_storage_account.observability.id
  container_access_type = "private"
}

resource "azurerm_storage_management_policy" "observability" {
  storage_account_id = azurerm_storage_account.observability.id

  rule {
    name    = "expire-telemetry"
    enabled = true

    filters {
      blob_types = ["blockBlob"]
    }

    actions {
      base_blob {
        delete_after_days_since_modification_greater_than = var.observability_retention_days
      }
    }
  }
}
