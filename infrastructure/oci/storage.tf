# Object storage for the observability backends (Loki, Tempo, long-term metrics).
resource "oci_objectstorage_bucket" "observability" {
  for_each = var.observability_buckets

  compartment_id = var.compartment_ocid
  namespace      = data.oci_objectstorage_namespace.main.namespace
  name           = "${local.name_prefix}-${each.key}"
  access_type    = "NoPublicAccess"
  storage_tier   = "Standard"
  versioning     = "Enabled"
  kms_key_id     = oci_kms_key.main.id
  freeform_tags  = local.common_tags

  depends_on = [oci_identity_policy.platform]
}

resource "oci_objectstorage_object_lifecycle_policy" "observability" {
  for_each = oci_objectstorage_bucket.observability

  namespace = data.oci_objectstorage_namespace.main.namespace
  bucket    = each.value.name

  rules {
    name        = "expire-telemetry"
    action      = "DELETE"
    is_enabled  = true
    target      = "objects"
    time_amount = var.observability_retention_days
    time_unit   = "DAYS"
  }

  rules {
    name        = "expire-previous-versions"
    action      = "DELETE"
    is_enabled  = true
    target      = "previous-object-versions"
    time_amount = 7
    time_unit   = "DAYS"
  }
}
