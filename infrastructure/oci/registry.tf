# OCIR repository: private, immutable tags. Image pulls by worker nodes authenticate through an
# image pull secret created from a short-lived token by the deployment pipeline, or through
# workload identity policies where the OKE version supports it.
resource "oci_artifacts_container_repository" "api" {
  compartment_id = var.compartment_ocid
  display_name   = "${local.name_prefix}/ecommerce-api"
  is_public      = false
  is_immutable   = true
  freeform_tags  = local.common_tags
}
