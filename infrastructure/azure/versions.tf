terraform {
  required_version = ">= 1.11.0, < 2.0.0"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.0"
    }
  }

  # Partial configuration: supply the rest with `terraform init -backend-config=backend.hcl`
  # (see backend.hcl.example). Blob leases provide state locking; access uses Entra ID, not keys.
  backend "azurerm" {}
}
