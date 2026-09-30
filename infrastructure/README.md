# Infrastructure

Terraform templates that provision the platform hosting `pf-ecommerce-backend` on **AWS**, **Azure**, **Google Cloud**, or **Oracle Cloud Infrastructure (OCI)**. The rules they implement are in [`ai/IAC.md`](../ai/IAC.md), [`ai/SECURITY.md`](../ai/SECURITY.md), and [`ai/CONTAINERS.md`](../ai/CONTAINERS.md).

> **Status: starting templates.** They were formatted with `terraform fmt` but **not** validated or applied against the provider APIs (provider plugins were not reachable when they were written). Run `terraform init` and `terraform validate`, review every resource against your requirements, and follow the checklist below before the first `apply`.

## Choose one provider

| Directory | Kubernetes | PostgreSQL | Valkey | Registry | Secrets |
| --- | --- | --- | --- | --- | --- |
| [`aws/`](aws) | EKS | RDS for PostgreSQL | ElastiCache for Valkey | ECR | Secrets Manager |
| [`azure/`](azure) | AKS | PostgreSQL Flexible Server | in-cluster | ACR | Key Vault |
| [`gcp/`](gcp) | GKE Autopilot | Cloud SQL for PostgreSQL | Memorystore for Valkey | Artifact Registry | Secret Manager |
| [`oci/`](oci) | OKE | Database with PostgreSQL | in-cluster | OCIR | OCI Vault |

Every provider also provisions the network, the observability object-storage buckets, and the identity that lets External Secrets Operator read secrets. **RabbitMQ runs in the cluster on all four** (RabbitMQ Cluster Operator, declared under [`kubernetes/`](../kubernetes)), as does Valkey on Azure and OCI. An environment uses exactly one provider directory.

## Layout of a provider directory

```
versions.tf           Terraform and provider pins, partial backend block
providers.tf          Provider configuration (default tags/labels, region)
variables.tf          Typed, validated inputs — no secrets
locals.tf             Naming, mandatory tags/labels, CIDR layout
network.tf            Network, subnets, gateways, network security
kubernetes.tf         Managed Kubernetes cluster and node capacity
registry.tf           Private container registry
postgresql.tf         Managed PostgreSQL
valkey.tf             Managed Valkey (AWS and Google Cloud only)
secrets.tf            Secret containers / vault and External Secrets identity
storage.tf            Observability object storage
outputs.tf            Values needed by the Kubernetes overlays and pipelines
backend.hcl.example   State backend settings
environments/         development.tfvars, staging.tfvars, production.tfvars (non-secret)
```

## Usage

```bash
cd infrastructure/aws                               # or azure, gcp, oci
cp backend.hcl.example backend.production.hcl       # edit; git-ignored
terraform init -backend-config=backend.production.hcl
terraform plan -var-file=environments/production.tfvars -out=tfplan
terraform apply tfplan
```

- **One state per environment.** Each `backend.<environment>.hcl` points to its own state object. Create the state bucket/container once, out of band, with versioning, encryption with a customer-managed key, no public access, and access limited to the CI identity.
- **Commit `.terraform.lock.hcl`** after the first `terraform init`.
- **Placeholders.** Identifiers in `environments/*.tfvars` (subscription, project, tenancy/compartment OCIDs, object IDs, CIDR allow-lists) are placeholders. Supply real values through the pipeline or a git-ignored `*.auto.tfvars`.
- **No secrets in code or state.** Templates create secret containers/vaults only; values are written out of band. Database credentials use provider-managed secrets (AWS), Entra ID (Azure), IAM authentication (Google Cloud), or the OCID of a pre-created Vault secret (OCI).

## Before the first apply

1. Record the provider choice in an ADR (`docs/adr/`).
2. Review sizing, regions, CIDRs, retention, and cost in `environments/*.tfvars`.
3. Replace placeholder allow-lists (Kubernetes API access) with your CI runner and operator ranges.
4. Run `terraform validate`, `tflint`, and `trivy config` (or Checkov); fix findings.
5. Create the state backend and the out-of-band prerequisites listed in each provider's `backend.hcl.example` and variable descriptions (for OCI: the Vault secret for the database password and the node image OCID; for Azure: the Entra ID groups).
6. Plan in `development`, then `staging`, then `production`; production requires the approvals in `ai/IAC.md` Section 8.3.

## Not included

- The application and in-cluster services (RabbitMQ, Valkey where applicable, OpenTelemetry Collector, External Secrets Operator, ingress, cert-manager) — deployed from [`kubernetes/`](../kubernetes).
- DNS, WAF/CDN, and certificate issuance for public hostnames.
- The observability backends (Prometheus/Mimir, Loki, Tempo, Grafana); only their object storage is provisioned here.
- Organization-level guardrails (AWS SCPs, Azure Policy, Google Organization Policies, OCI Cloud Guard) and account/subscription/project/compartment creation.
- Image tag immutability enforcement on Azure ACR (release pipeline) and CI/CD pipelines themselves (`.github/workflows/`).
