terraform {
  required_version = ">= 1.11.0, < 2.0.0" # 1.11+ for write-only arguments

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 6.0"
    }
  }

  # Partial configuration: supply the rest with `terraform init -backend-config=backend.hcl`
  # (see backend.hcl.example). State is encrypted, versioned, and locked natively (use_lockfile).
  backend "s3" {}
}
