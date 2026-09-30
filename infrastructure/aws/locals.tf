data "aws_availability_zones" "available" {
  state = "available"
}

data "aws_caller_identity" "current" {}

locals {
  name_prefix = "${var.project_name}-${var.environment}"

  common_tags = {
    environment = var.environment
    project     = var.project_name
    owner       = var.owner
    managed-by  = "terraform"
    revision    = var.git_sha
  }

  azs = slice(data.aws_availability_zones.available.names, 0, var.az_count)

  # /16 VPC -> /20 public, /20 private (workloads), /22 data (database and cache)
  public_subnet_cidrs  = [for i in range(var.az_count) : cidrsubnet(var.vpc_cidr, 4, i)]
  private_subnet_cidrs = [for i in range(var.az_count) : cidrsubnet(var.vpc_cidr, 4, i + 4)]
  data_subnet_cidrs    = [for i in range(var.az_count) : cidrsubnet(var.vpc_cidr, 6, i + 48)]

  # One NAT gateway per AZ in production, a single shared one elsewhere.
  nat_gateway_count = var.environment == "production" ? var.az_count : 1
}
