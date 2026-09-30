# Key Vault with Azure RBAC, purge protection, and no public access. Secret VALUES are written out
# of band (portal, CLI, or rotation) so they never enter Terraform code, variables, plans, or state.
resource "azurerm_key_vault" "main" {
  name                          = local.key_vault_name
  location                      = azurerm_resource_group.main.location
  resource_group_name           = azurerm_resource_group.main.name
  tenant_id                     = data.azurerm_client_config.current.tenant_id
  sku_name                      = "standard"
  rbac_authorization_enabled    = true
  purge_protection_enabled      = true
  soft_delete_retention_days    = 90
  public_network_access_enabled = false
  tags                          = local.common_tags

  network_acls {
    default_action = "Deny"
    bypass         = "AzureServices"
  }
}

# Identity used by External Secrets Operator (federated with the AKS service account, no secrets).
resource "azurerm_user_assigned_identity" "external_secrets" {
  name                = "${local.name_prefix}-external-secrets"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  tags                = local.common_tags
}

resource "azurerm_federated_identity_credential" "external_secrets" {
  name                = "external-secrets"
  resource_group_name = azurerm_resource_group.main.name
  parent_id           = azurerm_user_assigned_identity.external_secrets.id
  audience            = ["api://AzureADTokenExchange"]
  issuer              = azurerm_kubernetes_cluster.main.oidc_issuer_url
  subject             = "system:serviceaccount:external-secrets:external-secrets"
}

resource "azurerm_role_assignment" "external_secrets_reader" {
  scope                = azurerm_key_vault.main.id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = azurerm_user_assigned_identity.external_secrets.principal_id
}

resource "azurerm_management_lock" "key_vault" {
  count = var.deletion_protection ? 1 : 0

  name       = "protect-key-vault"
  scope      = azurerm_key_vault.main.id
  lock_level = "CanNotDelete"
  notes      = "Stateful resource: remove the lock through a reviewed change before deleting."
}
