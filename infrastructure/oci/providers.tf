# Authentication comes from the environment, never from this repository: OCI CLI config profile,
# OIDC/workload identity federation in CI, or instance principals (TF_VAR / OCI_* variables).
provider "oci" {
  region = var.region
}
