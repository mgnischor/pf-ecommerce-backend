terraform {
  required_version = ">= 1.12.0, < 2.0.0" # 1.12+ for the native OCI Object Storage backend

  required_providers {
    oci = {
      source  = "oracle/oci"
      version = "~> 7.0"
    }
  }

  # Partial configuration: supply the rest with `terraform init -backend-config=backend.hcl`
  # (see backend.hcl.example). State lives in a versioned, private Object Storage bucket.
  backend "oci" {}
}
