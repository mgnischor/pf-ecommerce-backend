# Non-secret values only. Apply with: terraform plan -var-file=environments/production.tfvars
# OCIDs below are placeholders; supply real ones through the pipeline or a git-ignored *.auto.tfvars.
environment      = "production"
owner            = "team-platform"
region           = "us-ashburn-1"
tenancy_ocid     = "ocid1.tenancy.oc1..example"
compartment_ocid = "ocid1.compartment.oc1..example"
vcn_cidr         = "10.30.0.0/16"

kubernetes_version           = "v1.33.1"
kubernetes_api_allowed_cidrs = ["203.0.113.0/24"]
node_image_id                = "ocid1.image.oc1..example"
node_ocpus                   = 8
node_memory_gbs              = 64
node_count                   = 3

postgres_shape                 = "PostgreSQL.VM.Standard.E5.Flex.8.128GB"
postgres_ocpus                 = 8
postgres_memory_gbs            = 128
postgres_instance_count        = 3
postgres_admin_secret_id       = "ocid1.vaultsecret.oc1..example"
postgres_backup_retention_days = 35
