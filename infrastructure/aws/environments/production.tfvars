# Non-secret values only. Apply with: terraform plan -var-file=environments/production.tfvars
environment = "production"
owner       = "team-platform"
region      = "us-east-1"
vpc_cidr    = "10.30.0.0/16"
az_count    = 3

node_instance_types = ["m7i.xlarge"]
node_min_size       = 3
node_max_size       = 12
node_desired_size   = 3

postgres_instance_class           = "db.m7g.xlarge"
postgres_allocated_storage_gb     = 100
postgres_max_allocated_storage_gb = 1000
postgres_multi_az                 = true
postgres_backup_retention_days    = 35
deletion_protection               = true

valkey_node_type = "cache.m7g.large"
valkey_replicas  = 2
