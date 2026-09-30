# Non-secret values only. Apply with: terraform plan -var-file=environments/staging.tfvars
environment = "staging"
owner       = "team-platform"
region      = "us-east-1"
vpc_cidr    = "10.20.0.0/16"
az_count    = 2

node_instance_types = ["m7i.large"]
node_min_size       = 2
node_max_size       = 4
node_desired_size   = 2

postgres_instance_class           = "db.m7g.large"
postgres_allocated_storage_gb     = 50
postgres_max_allocated_storage_gb = 200
postgres_multi_az                 = true
postgres_backup_retention_days    = 14
deletion_protection               = true

valkey_node_type = "cache.m7g.large"
valkey_replicas  = 1
