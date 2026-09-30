output "resource_group_name" {
  description = "Name of the resource group."
  value       = azurerm_resource_group.main.name
}

output "cluster_name" {
  description = "Name of the AKS cluster."
  value       = azurerm_kubernetes_cluster.main.name
}

output "cluster_oidc_issuer_url" {
  description = "OIDC issuer URL of the AKS cluster (for workload identity federation)."
  value       = azurerm_kubernetes_cluster.main.oidc_issuer_url
}

output "registry_login_server" {
  description = "Login server of the container registry."
  value       = azurerm_container_registry.main.login_server
}

output "postgres_fqdn" {
  description = "Private FQDN of the PostgreSQL flexible server."
  value       = azurerm_postgresql_flexible_server.main.fqdn
}

output "key_vault_uri" {
  description = "URI of the Key Vault holding application secrets."
  value       = azurerm_key_vault.main.vault_uri
}

output "external_secrets_identity_client_id" {
  description = "Client ID of the managed identity used by External Secrets Operator."
  value       = azurerm_user_assigned_identity.external_secrets.client_id
}

output "observability_storage_account" {
  description = "Name of the observability storage account."
  value       = azurerm_storage_account.observability.name
}
