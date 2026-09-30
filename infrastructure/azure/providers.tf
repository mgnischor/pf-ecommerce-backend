provider "azurerm" {
  subscription_id     = var.subscription_id
  storage_use_azuread = true # data-plane calls use Entra ID; storage account keys stay disabled

  features {
    key_vault {
      purge_soft_delete_on_destroy = false
    }

    resource_group {
      prevent_deletion_if_contains_resources = true
    }
  }
}
