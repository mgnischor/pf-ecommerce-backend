# Premium SKU: zone redundancy, private endpoint support, and retention policy.
# Tag immutability is configured per repository/tag with `az acr repository update --write-enabled false`
# by the release pipeline; vulnerability scanning is provided by Microsoft Defender for Containers.
resource "azurerm_container_registry" "main" {
  name                          = local.acr_name
  location                      = azurerm_resource_group.main.location
  resource_group_name           = azurerm_resource_group.main.name
  sku                           = "Premium"
  admin_enabled                 = false
  anonymous_pull_enabled        = false
  public_network_access_enabled = false
  zone_redundancy_enabled       = true
  retention_policy_in_days      = 30
  tags                          = local.common_tags
}
