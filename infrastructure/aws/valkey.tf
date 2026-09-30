resource "aws_elasticache_subnet_group" "valkey" {
  name       = "${local.name_prefix}-valkey"
  subnet_ids = aws_subnet.data[*].id
}

resource "aws_security_group" "valkey" {
  name        = "${local.name_prefix}-valkey"
  description = "Valkey: reachable only from the EKS cluster security group"
  vpc_id      = aws_vpc.main.id

  tags = { Name = "${local.name_prefix}-valkey" }
}

resource "aws_vpc_security_group_ingress_rule" "valkey_from_cluster" {
  security_group_id            = aws_security_group.valkey.id
  description                  = "Valkey from EKS workloads"
  referenced_security_group_id = aws_eks_cluster.main.vpc_config[0].cluster_security_group_id
  from_port                    = 6379
  to_port                      = 6379
  ip_protocol                  = "tcp"
}

# Access control: the built-in default user is disabled; the application connects as a
# dedicated user with IAM authentication, limited to its own key namespace and without
# dangerous commands (FLUSHALL, CONFIG, KEYS, ...).
resource "aws_elasticache_user" "default" {
  user_id       = "${local.name_prefix}-default"
  user_name     = "default"
  engine        = "VALKEY"
  access_string = "off ~* -@all"

  authentication_mode {
    type = "no-password-required"
  }
}

resource "aws_elasticache_user" "app" {
  user_id       = "${local.name_prefix}-app"
  user_name     = "${local.name_prefix}-app" # must equal user_id for IAM authentication
  engine        = "VALKEY"
  access_string = "on ~ecommerce:* +@all -@dangerous -@admin"

  authentication_mode {
    type = "iam"
  }
}

resource "aws_elasticache_user_group" "valkey" {
  user_group_id = "${local.name_prefix}-valkey"
  engine        = "VALKEY"
  user_ids      = [aws_elasticache_user.default.user_id, aws_elasticache_user.app.user_id]
}

resource "aws_elasticache_replication_group" "valkey" {
  replication_group_id = "${local.name_prefix}-valkey"
  description          = "${local.name_prefix} cache"
  engine               = "valkey"
  engine_version       = "8.0"
  node_type            = var.valkey_node_type
  port                 = 6379

  num_cache_clusters         = 1 + var.valkey_replicas
  automatic_failover_enabled = var.valkey_replicas > 0
  multi_az_enabled           = var.valkey_replicas > 0

  subnet_group_name    = aws_elasticache_subnet_group.valkey.name
  security_group_ids   = [aws_security_group.valkey.id]
  parameter_group_name = "default.valkey8"
  user_group_ids       = [aws_elasticache_user_group.valkey.user_group_id]

  at_rest_encryption_enabled = true
  kms_key_id                 = aws_kms_key.main.arn
  transit_encryption_enabled = true
  transit_encryption_mode    = "required"

  # The cache is an optimization, not a source of truth: short snapshot retention only.
  snapshot_retention_limit   = 1
  auto_minor_version_upgrade = true
  maintenance_window         = "sun:06:00-sun:07:00"
  apply_immediately          = false
}
