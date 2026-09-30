# Non-secret values only. Apply with: terraform plan -var-file=environments/development.tfvars
environment = "development"
owner       = "team-platform"
region      = "us-east-1"
vpc_cidr    = "10.10.0.0/16"
az_count    = 2

node_instance_types = ["t3.large"]
node_min_size       = 1
node_max_size       = 3
node_desired_size   = 1

postgres_instance_class           = "db.t4g.medium"
postgres_allocated_storage_gb     = 20
postgres_max_allocated_storage_gb = 100
postgres_multi_az                 = false
postgres_backup_retention_days    = 7
deletion_protection               = false

valkey_node_type = "cache.t4g.small"
valkey_replicas  = 0
