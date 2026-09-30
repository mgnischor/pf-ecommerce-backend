output "cluster_name" {
  description = "Name of the EKS cluster."
  value       = aws_eks_cluster.main.name
}

output "cluster_endpoint" {
  description = "Endpoint of the EKS API server."
  value       = aws_eks_cluster.main.endpoint
}

output "registry_url" {
  description = "ECR repository URL for the application image."
  value       = aws_ecr_repository.api.repository_url
}

output "postgres_endpoint" {
  description = "PostgreSQL endpoint (host:port)."
  value       = aws_db_instance.postgres.endpoint
}

output "postgres_master_secret_arn" {
  description = "Secrets Manager ARN of the RDS-managed master credentials (used only by the migration job)."
  value       = aws_db_instance.postgres.master_user_secret[0].secret_arn
}

output "valkey_primary_endpoint" {
  description = "Primary endpoint of the Valkey replication group."
  value       = aws_elasticache_replication_group.valkey.primary_endpoint_address
}

output "secret_arns" {
  description = "ARNs of the application secret containers."
  value       = { for k, s in aws_secretsmanager_secret.app : k => s.arn }
}

output "observability_buckets" {
  description = "Names of the observability object-storage buckets."
  value       = { for k, b in aws_s3_bucket.observability : k => b.bucket }
}

output "kms_key_arn" {
  description = "ARN of the platform encryption key."
  value       = aws_kms_key.main.arn
}
