data "oci_identity_availability_domains" "main" {
  compartment_id = var.tenancy_ocid
}

data "oci_core_services" "all" {
  filter {
    name   = "name"
    values = ["All .* Services In Oracle Services Network"]
    regex  = true
  }
}

data "oci_objectstorage_namespace" "main" {
  compartment_id = var.compartment_ocid
}

locals {
  name_prefix = "${var.project_name}-${var.environment}"

  # OCI tags (freeform). Keys must not contain spaces; values are free text.
  common_tags = {
    environment = var.environment
    project     = var.project_name
    owner       = var.owner
    managed-by  = "terraform"
    revision    = var.git_sha
  }

  availability_domains = data.oci_identity_availability_domains.main.availability_domains[*].name
  service_cidr_block   = data.oci_core_services.all.services[0].cidr_block
  service_id           = data.oci_core_services.all.services[0].id

  # Subnets carved from the /16 VCN.
  api_subnet_cidr   = cidrsubnet(var.vcn_cidr, 12, 0) # /28 Kubernetes API endpoint
  lb_subnet_cidr    = cidrsubnet(var.vcn_cidr, 8, 1)  # /24 load balancers
  nodes_subnet_cidr = cidrsubnet(var.vcn_cidr, 4, 1)  # /20 worker nodes
  db_subnet_cidr    = cidrsubnet(var.vcn_cidr, 8, 32) # /24 database

  # Production keys are HSM-protected; other environments use software keys.
  key_protection_mode = var.environment == "production" ? "HSM" : "SOFTWARE"
}
