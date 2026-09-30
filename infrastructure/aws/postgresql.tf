resource "aws_db_subnet_group" "postgres" {
  name       = "${local.name_prefix}-postgres"
  subnet_ids = aws_subnet.data[*].id
}

resource "aws_security_group" "postgres" {
  name        = "${local.name_prefix}-postgres"
  description = "PostgreSQL: reachable only from the EKS cluster security group"
  vpc_id      = aws_vpc.main.id

  tags = { Name = "${local.name_prefix}-postgres" }
}

resource "aws_vpc_security_group_ingress_rule" "postgres_from_cluster" {
  security_group_id            = aws_security_group.postgres.id
  description                  = "PostgreSQL from EKS workloads"
  referenced_security_group_id = aws_eks_cluster.main.vpc_config[0].cluster_security_group_id
  from_port                    = 5432
  to_port                      = 5432
  ip_protocol                  = "tcp"
}

resource "aws_db_parameter_group" "postgres" {
  name_prefix = "${local.name_prefix}-postgres-"
  family      = "postgres${var.postgres_engine_version}"

  parameter {
    name  = "rds.force_ssl"
    value = "1"
  }

  parameter {
    name  = "log_min_duration_statement"
    value = "500"
  }

  parameter {
    name  = "log_lock_waits"
    value = "1"
  }

  parameter {
    name  = "idle_in_transaction_session_timeout"
    value = "60000"
  }

  parameter {
    name         = "shared_preload_libraries"
    value        = "pg_stat_statements"
    apply_method = "pending-reboot"
  }

  lifecycle {
    create_before_destroy = true
  }
}

resource "aws_db_instance" "postgres" {
  identifier     = "${local.name_prefix}-postgres"
  engine         = "postgres"
  engine_version = var.postgres_engine_version
  instance_class = var.postgres_instance_class

  db_name  = "ecommerce"
  username = "app_admin"

  # RDS generates the master password and stores it (and rotates it) in Secrets Manager:
  # nothing secret enters Terraform code, variables, or state.
  manage_master_user_password   = true
  master_user_secret_kms_key_id = aws_kms_key.main.arn

  # Applications authenticate with IAM database authentication (short-lived tokens).
  iam_database_authentication_enabled = true

  allocated_storage     = var.postgres_allocated_storage_gb
  max_allocated_storage = var.postgres_max_allocated_storage_gb
  storage_type          = "gp3"
  storage_encrypted     = true
  kms_key_id            = aws_kms_key.main.arn

  multi_az               = var.postgres_multi_az
  db_subnet_group_name   = aws_db_subnet_group.postgres.name
  vpc_security_group_ids = [aws_security_group.postgres.id]
  publicly_accessible    = false
  parameter_group_name   = aws_db_parameter_group.postgres.name

  backup_retention_period   = var.postgres_backup_retention_days
  copy_tags_to_snapshot     = true
  deletion_protection       = var.deletion_protection
  skip_final_snapshot       = false
  final_snapshot_identifier = "${local.name_prefix}-postgres-final"

  auto_minor_version_upgrade            = true
  performance_insights_enabled          = true
  performance_insights_kms_key_id       = aws_kms_key.main.arn
  enabled_cloudwatch_logs_exports       = ["postgresql", "upgrade"]
  maintenance_window                    = "sun:05:00-sun:06:00"
  backup_window                         = "03:00-04:00"
  performance_insights_retention_period = 7
}
