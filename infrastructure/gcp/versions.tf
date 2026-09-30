terraform {
  required_version = ">= 1.11.0, < 2.0.0"

  required_providers {
    google = {
      source  = "hashicorp/google"
      version = ">= 6.30, < 8.0"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.6"
    }
  }

  # Partial configuration: supply the rest with `terraform init -backend-config=backend.hcl`
  # (see backend.hcl.example). GCS provides native state locking and versioning.
  backend "gcs" {}
}
