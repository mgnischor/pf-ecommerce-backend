# Vault and master key. Secrets themselves are created out of band (console, CLI, or rotation)
# because an OCI Vault secret cannot exist without content, and the content must never enter
# Terraform code, variables, plans, or state. External Secrets Operator reads them at runtime.
resource "oci_kms_vault" "main" {
  compartment_id = var.compartment_ocid
  display_name   = "${local.name_prefix}-vault"
  vault_type     = "DEFAULT"
  freeform_tags  = local.common_tags
}

resource "oci_kms_key" "main" {
  compartment_id      = var.compartment_ocid
  display_name        = "${local.name_prefix}-key"
  management_endpoint = oci_kms_vault.main.management_endpoint
  protection_mode     = local.key_protection_mode
  freeform_tags       = local.common_tags

  key_shape {
    algorithm = "AES"
    length    = 32
  }
}

# Workload identity: only the external-secrets service account of this cluster may read secrets.
resource "oci_identity_policy" "platform" {
  compartment_id = var.compartment_ocid
  name           = "${local.name_prefix}-platform"
  description    = "Platform policies for ${local.name_prefix}"
  freeform_tags  = local.common_tags

  statements = [
    "Allow any-user to read secret-family in compartment id ${var.compartment_ocid} where all {request.principal.type = 'workload', request.principal.namespace = 'external-secrets', request.principal.service_account = 'external-secrets', request.principal.cluster_id = '${oci_containerengine_cluster.main.id}'}",
    "Allow service blockstorage, objectstorage-${var.region} to use keys in compartment id ${var.compartment_ocid}",
    "Allow service objectstorage-${var.region} to manage object-family in compartment id ${var.compartment_ocid}",
  ]
}
