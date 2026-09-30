# Infrastructure as Code Standards

> **Scope:** These standards apply to all infrastructure definitions of the `pf-ecommerce-backend` repository: cloud resources provisioned with **Terraform**, Kubernetes manifests (`kubernetes/`, Kustomize), and the Docker Compose production topology (`docker-compose-prod.yml`). The infrastructure hosts the .NET application and its backing services: **PostgreSQL**, **Valkey**, **RabbitMQ**, and an **OpenTelemetry Collector**. Every item marked as mandatory is a hard requirement unless a documented exception is approved by the maintainers. IaC prioritizes reproducibility, security, modularity, drift prevention, and auditability, and complies with [SECURITY.md](./SECURITY.md).
>
> Terraform templates exist for four cloud providers — **AWS, Azure, Google Cloud, and Oracle Cloud Infrastructure (OCI)** — under `infrastructure/<provider>/`. Each deployment uses exactly one of them; the choice is recorded in an ADR before the first resource is provisioned ([Section 5.1](#51-provider-templates)).

---

## Table of Contents

1. [General Principles](#1-general-principles)
2. [Repository and File Organization](#2-repository-and-file-organization)
    - 2.1 [Folder Structure](#21-folder-structure)
    - 2.2 [Naming Conventions](#22-naming-conventions)
    - 2.3 [Environment Separation](#23-environment-separation)
3. [Terraform Standards](#3-terraform-standards)
    - 3.1 [General Rules](#31-general-rules)
    - 3.2 [State Management](#32-state-management)
    - 3.3 [Module Design](#33-module-design)
    - 3.4 [Provider and Version Pinning](#34-provider-and-version-pinning)
    - 3.5 [Variables and Outputs](#35-variables-and-outputs)
    - 3.6 [Resource Naming and Tagging](#36-resource-naming-and-tagging)
4. [Kubernetes and Compose Manifests as Code](#4-kubernetes-and-compose-manifests-as-code)
5. [Backing Services Provisioning](#5-backing-services-provisioning)
    - 5.1 [Provider Templates](#51-provider-templates)
    - 5.2 [Using a Template](#52-using-a-template)
6. [Secrets and Sensitive Data in IaC](#6-secrets-and-sensitive-data-in-iac)
7. [Security and Compliance](#7-security-and-compliance)
    - 7.1 [Policy as Code](#71-policy-as-code)
    - 7.2 [Least Privilege for Infrastructure](#72-least-privilege-for-infrastructure)
    - 7.3 [Network Security Defaults](#73-network-security-defaults)
8. [CI/CD Pipeline Integration](#8-cicd-pipeline-integration)
    - 8.1 [Pipeline Stages](#81-pipeline-stages)
    - 8.2 [Drift Detection](#82-drift-detection)
    - 8.3 [Approval Gates](#83-approval-gates)
9. [Testing Infrastructure Code](#9-testing-infrastructure-code)
10. [Documentation and Change Management](#10-documentation-and-change-management)
11. [IaC Definition of Done](#11-iac-definition-of-done)

---

<GeneralPrinciples>
## 1. General Principles

| Principle | Mandatory Behavior |
| --- | --- |
| **Declarative Over Imperative** | Describe desired state. Use imperative scripts only when no declarative alternative exists. |
| **Reproducibility** | Every environment is reproducible from code. Manual changes to infrastructure are forbidden. |
| **Idempotency** | Every IaC operation is safely re-runnable. |
| **Immutable Infrastructure** | Prefer replacing over mutating. Avoid in-place patching of production resources. |
| **Version Everything** | IaC definitions, modules, variables, and policies are version-controlled. |
| **Security as Code** | Network rules, IAM policies, and encryption settings are defined in code. See [SECURITY.md](./SECURITY.md). |
| **Least Privilege by Default** | Every resource, role, and service account is provisioned with minimum permissions. Wildcards are forbidden. |
| **Modularity and Reuse** | Small, reusable, tested modules. Monolithic configurations are a defect. |
| **Drift Is a Defect** | Divergence between declared and actual state is detected, alerted, and remediated. |
| **Observability from Provisioning** | Logging, monitoring, and alerting for each resource are part of its definition. |

</GeneralPrinciples>

---

<RepositoryAndFileOrganization>
## 2. Repository and File Organization

### 2.1 Folder Structure

Infrastructure lives in this repository unless an ADR moves it to a dedicated repository.

```
kubernetes/                     # Application manifests (Kustomize) — see CONTAINERS.md
├── base/
└── overlays/
    ├── development/
    ├── staging/
    └── production/
docker-compose-dev.yml          # Local development topology
docker-compose-prod.yml         # Single-host production topology
infrastructure/                 # Cloud resources (Terraform); see infrastructure/README.md
├── README.md                   # How to choose and use a provider template
├── aws/                        # AWS: VPC, EKS, ECR, RDS, ElastiCache (Valkey), Secrets Manager, S3
├── azure/                      # Azure: VNet, AKS, ACR, PostgreSQL Flexible Server, Key Vault, Storage
├── gcp/                        # Google Cloud: VPC, GKE Autopilot, Artifact Registry, Cloud SQL, Memorystore, Secret Manager, GCS
├── oci/                        # OCI: VCN, OKE, OCIR, Database with PostgreSQL, Vault, Object Storage
│   ├── versions.tf             # Every provider directory is a root module with the same layout:
│   ├── providers.tf            #   Terraform/provider pins and partial backend configuration
│   ├── variables.tf            #   Typed, validated inputs (no secrets)
│   ├── locals.tf               #   Naming, mandatory tags/labels, CIDR layout
│   ├── network.tf              #   Network, subnets, gateways, network security
│   ├── kubernetes.tf           #   Managed Kubernetes cluster and node capacity
│   ├── registry.tf             #   Private container registry
│   ├── postgresql.tf           #   Managed PostgreSQL
│   ├── valkey.tf               #   Managed Valkey (AWS and Google Cloud only)
│   ├── secrets.tf              #   Secret containers/vault and workload identity for External Secrets
│   ├── storage.tf              #   Observability object storage
│   ├── outputs.tf
│   ├── backend.hcl.example     #   State backend settings (copy per environment)
│   └── environments/           #   development.tfvars, staging.tfvars, production.tfvars (non-secret)
├── modules/                    # Shared reusable modules, added only when a pattern repeats across providers
├── policies/                   # Policy-as-code (OPA/Conftest, Checkov custom checks)
└── tests/                      # Infrastructure tests (terraform test, Terratest)
```

### 2.2 Naming Conventions

| Element | Convention |
| --- | --- |
| **Files** | Lowercase with underscores or hyphens: `main.tf`, `variables.tf`, `outputs.tf`. |
| **Resources and modules** | Lowercase with underscores; descriptive (`api_load_balancer`, not `lb1`). |
| **Variables** | `snake_case`, self-documenting (`database_instance_class`). |
| **Tags and labels** | Consistent key-values on every resource: `environment`, `project`, `owner`, `managed-by`. |
| **Environments** | `development`, `staging`, `production`. |

### 2.3 Environment Separation

| Requirement | Mandatory Behavior |
| --- | --- |
| **Isolation** | Separate state, credentials, and (when possible) cloud accounts/subscriptions per environment. |
| **Parity** | Staging mirrors production in architecture and configuration; only scale and cost differ. |
| **Promotion** | `development` → `staging` → `production`. Never apply directly to production. |
| **Environment-specific variables** | `.tfvars` files or equivalent; never hardcode environment values in modules. |

</RepositoryAndFileOrganization>

---

<TerraformStandards>
## 3. Terraform Standards

The repository standardizes on **Terraform** and pins its version (`required_version`). Terraform is licensed under the BUSL; using it to manage this project's own infrastructure is permitted, but offering it as a competing hosted service is not. A future move to OpenTofu would be an ADR.

### 3.1 General Rules

| Rule | Mandatory Behavior |
| --- | --- |
| **One responsibility per file** | `main.tf` (primary resources), `variables.tf`, `outputs.tf`, `providers.tf`, `data.tf`. |
| **No inline blocks when avoidable** | Prefer separate resources over inline sub-blocks (e.g., separate security-group rule resources). |
| **Format, validate, lint** | `fmt -check`, `validate`, and `tflint` pass in CI. |
| **No `-target` in production** | It causes drift. |
| **Plan before apply** | Every apply is preceded by a reviewed plan; apply the saved plan file. |

### 3.2 State Management

| Requirement | Mandatory Behavior |
| --- | --- |
| **Remote state** | Stored in a remote backend (object storage with locking, or a managed service). Local state is forbidden in shared environments. |
| **State locking** | Enabled (native lock files on S3 with `use_lockfile = true`; native leases/locks on Azure Blob and GCS). |
| **State encryption** | Encrypted at rest with a customer-managed key. |
| **No secrets in state** | Prefer provider-managed secrets (the service generates and rotates its own password), **ephemeral resources**, and **write-only arguments** (`*_wo`). `sensitive = true` only redacts output; the value is still stored in state. |
| **State per environment** | Never share state across environments. |
| **State backup** | Versioning enabled on the backend. |
| **State read access** | Restricted to the CI identity and infrastructure owners — state read access is a secrets-access control. |

### 3.3 Module Design

| Principle | Mandatory Behavior |
| --- | --- |
| **Single responsibility** | One logical concern per module (networking, PostgreSQL, Valkey, RabbitMQ, cluster). |
| **Explicit interfaces** | All inputs in `variables.tf`, all outputs in `outputs.tf`; no hidden dependencies. |
| **Versioned modules** | Remote module sources are pinned to exact versions or commit SHAs; local modules are referenced by relative path within this repository. |
| **No hardcoded values** | No hardcoded regions, account IDs, image IDs, or environment-specific values in modules. |
| **Composability** | No circular dependencies between modules. |
| **Documentation** | Every module has a `README.md` (purpose, inputs, outputs, example, prerequisites). |

### 3.4 Provider and Version Pinning

Each provider template pins Terraform and its providers and declares a partial backend configuration completed at `init` time (`-backend-config=backend.<environment>.hcl`):

```hcl
# infrastructure/aws/versions.tf
terraform {
  required_version = ">= 1.11.0, < 2.0.0" # 1.11+ for write-only arguments

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 6.0"
    }
  }

  backend "s3" {} # bucket, key, region, kms_key_id, use_lockfile come from backend.<environment>.hcl
}
```

| Provider directory | Terraform | Providers | State backend (locking) |
| --- | --- | --- | --- |
| `aws/` | `>= 1.11` | `hashicorp/aws ~> 6.0` | `s3` (native lock file, SSE-KMS, versioning) |
| `azure/` | `>= 1.11` | `hashicorp/azurerm ~> 4.0` | `azurerm` (blob lease, Entra ID auth) |
| `gcp/` | `>= 1.11` | `hashicorp/google >= 6.30, < 8.0`, `hashicorp/random ~> 3.6` | `gcs` (native lock, object versioning) |
| `oci/` | `>= 1.12` | `oracle/oci ~> 7.0` | `oci` (Object Storage, versioned, private) |

| Requirement | Mandatory Behavior |
| --- | --- |
| **Pin tool version** | `required_version` with explicit constraints. |
| **Pin provider versions** | `required_providers` with pessimistic (`~>`) constraints. |
| **Lock file** | Commit `.terraform.lock.hcl`; update it explicitly during upgrades. |
| **Provider aliases** | Document each alias used for multi-region or multi-account setups. |

### 3.5 Variables and Outputs

| Rule | Mandatory Behavior |
| --- | --- |
| **Type constraints** | Every variable has an explicit `type`. |
| **Descriptions** | Every variable and output has a `description`. |
| **Defaults only when safe** | `default` only for non-critical, non-environment-specific settings. |
| **Validation blocks** | For variables with known constraints (CIDRs, allowed values). |
| **Sensitive marking** | `sensitive = true` for secrets. |
| **No `.tfvars` in modules** | Values come from the environment composition. |

```hcl
variable "vpc_cidr" {
  description = "CIDR block for the private network."
  type        = string

  validation {
    condition     = can(cidrhost(var.vpc_cidr, 0))
    error_message = "vpc_cidr must be a valid CIDR block."
  }
}
```

### 3.6 Resource Naming and Tagging

| Requirement | Mandatory Behavior |
| --- | --- |
| **Naming scheme** | `{project}-{environment}-{resource}-{qualifier}` (e.g., `pf-ecommerce-production-postgres-primary`). |
| **Mandatory tags** | `environment`, `project`, `owner`, `managed-by = "terraform"`, plus the commit SHA or pipeline run ID. |
| **Cost allocation tags** | As required by the owner's cost strategy. |
| **No manual naming** | Names are generated from variables. |

```hcl
locals {
  common_tags = {
    environment = var.environment
    project     = var.project_name
    owner       = var.owner
    managed-by  = "terraform"
    revision    = var.git_sha
  }
}
```

</TerraformStandards>

---

<KubernetesAndComposeManifests>
## 4. Kubernetes and Compose Manifests as Code

| Requirement | Mandatory Behavior |
| --- | --- |
| **Kustomize for Kubernetes** | `kubernetes/base` holds environment-neutral manifests; overlays hold environment differences (replicas, resources, hostnames, secret references). Raw duplication across environments is forbidden. |
| **Workload standards** | All manifests comply with [CONTAINERS.md](./CONTAINERS.md) Sections 3 and 6–8. |
| **Validation in CI** | `kustomize build` for every overlay, `kubeconform`, policy checks (Kyverno CLI/Conftest), and `kubectl --dry-run=server` against a test cluster. |
| **GitOps** | Argo CD or Flux reconciles the cluster from Git; manual `kubectl apply` in production is forbidden. |
| **Compose production host** | `docker-compose-prod.yml` is deployed by the pipeline to a host provisioned by IaC. The host is hardened (automatic security updates, SSH keys only, host firewall default-deny with only 80/443 open). Changes are made by editing the file in Git and redeploying, never on the host. |
| **Image references** | Manifests reference images by digest; Renovate/Dependabot updates them ([CONTAINERS.md Section 2.1](./CONTAINERS.md)). |
| **No secrets in manifests** | Secrets are references (External Secrets, Compose secrets from a secret store) — see [Section 6](#6-secrets-and-sensitive-data-in-iac). |

</KubernetesAndComposeManifests>

---

<BackingServicesProvisioning>
## 5. Backing Services Provisioning

| Service | Mandatory Behavior |
| --- | --- |
| **PostgreSQL** | Managed service preferred. Private networking only; TLS enforced; encryption at rest with a customer-managed key; automated backups with point-in-time recovery; Multi-AZ/HA in production; separate roles for runtime, migrations, and read-only use ([DATABASE.md Section 3.2](./DATABASE.md)); `pg_stat_statements` enabled; deletion protection on in production. |
| **Valkey** | Managed service or operator-managed cluster. Private networking; TLS and ACL users enforced; `maxmemory` and eviction policy set ([DATABASE.md Section 4](./DATABASE.md)); persistence off for pure-cache instances. |
| **RabbitMQ** | Managed service or the RabbitMQ Cluster Operator. Private networking; TLS; per-service users with least-privilege vhost permissions; quorum queues for durable business queues; dead-letter exchanges and topology declared as code; management UI not publicly reachable. |
| **Container registry** | Private, with tag immutability, scanning, and retention ([CONTAINERS.md Section 4](./CONTAINERS.md)). |
| **Observability backend** | The OpenTelemetry Collector and its backends are provisioned as code, with retention and access controls ([OBSERVABILITY.md](./OBSERVABILITY.md)). |
| **DNS, certificates, WAF** | Managed and defined in code; certificates automated (cert-manager or cloud-managed). |
| **Deletion protection and backups** | Stateful resources have deletion protection, backups, and a documented restore procedure tested at least quarterly. |
| **Sizing** | Instance classes, storage, and connection limits are variables per environment; the connection budget is derived from `replicas × pool size` ([CONTAINERS.md Section 9.3](./CONTAINERS.md)). |

### 5.1 Provider Templates

The templates under `infrastructure/<provider>/` provision the same logical platform on each cloud. Each is a starting point that encodes this document's rules (private networking, customer-managed encryption, no secrets in state, deletion protection, mandatory tags, observability storage) and must be reviewed and adjusted — sizes, regions, CIDRs, and hardening — before first use.

| Component | AWS | Azure | Google Cloud | OCI |
| --- | --- | --- | --- | --- |
| **Network** | VPC with public, private, and isolated data subnets; NAT per AZ in production; S3 gateway endpoint; encrypted flow logs | VNet with AKS, delegated PostgreSQL, and private-endpoint subnets; NSG; private DNS zones and private endpoints | VPC with secondary ranges for pods/services; Cloud NAT; private services access; flow logs | VCN with API, LB, node, and DB subnets; NAT and service gateways; NSGs per resource; empty security lists |
| **Kubernetes** | EKS: private nodes, KMS-encrypted secrets, API access entries, IMDSv2 with hop limit 1, Pod Identity | AKS: Entra ID + Azure RBAC, local accounts off, Cilium network policy, OIDC workload identity, authorized API ranges | GKE Autopilot: private nodes, Workload Identity, dedicated node service account, authorized networks | OKE enhanced cluster: flannel overlay, node cycling, workload identity, API restricted by NSG |
| **Registry** | ECR: immutable tags, scan on push, KMS, lifecycle policy | ACR Premium: no admin user, private endpoint, `AcrPull` for kubelet identity | Artifact Registry: immutable tags, cleanup policy, vulnerability scanning API | OCIR: private, immutable |
| **PostgreSQL** | RDS: Multi-AZ, managed master secret, IAM auth, forced TLS, PITR, Performance Insights | Flexible Server: private access, Entra-only auth (no password), zone-redundant HA, CanNotDelete lock | Cloud SQL: private IP, TLS only, IAM auth, PITR, regional HA, separate runtime and migrator identities | Database with PostgreSQL: private subnet, regional durability, daily backups, password from a pre-created Vault secret |
| **Valkey** | ElastiCache for Valkey: TLS, KMS, IAM-auth user with restricted ACL | In-cluster (see below) | Memorystore for Valkey: IAM auth, TLS, Private Service Connect | In-cluster (see below) |
| **RabbitMQ** | In-cluster (see below) | In-cluster | In-cluster | In-cluster |
| **Secrets** | Secrets Manager containers (KMS) + Pod Identity role for External Secrets | Key Vault (RBAC, purge protection, private) + federated identity for External Secrets | Secret Manager containers + workload principal accessor | OCI Vault + key, workload-identity policy for External Secrets |
| **Observability storage** | S3 (SSE-KMS, versioned, TLS-only, lifecycle) | Storage account (Entra only, private endpoint, lifecycle) | GCS (uniform access, public access prevented, lifecycle) | Object Storage (private, versioned, CMK, lifecycle) |

**Managed versus in-cluster.** RabbitMQ runs in the cluster on every provider (RabbitMQ Cluster Operator, declared under `kubernetes/`), and so does Valkey on Azure and OCI. Managed RabbitMQ offerings (for example Amazon MQ) require a broker password that Terraform would store in state, which [Section 6](#6-secrets-and-sensitive-data-in-iac) forbids without a documented exception; managed Valkey is used wherever the provider offers a Valkey-compatible service with identity-based authentication. In-cluster stateful services follow [CONTAINERS.md Section 7.4](./CONTAINERS.md).

**Secrets are never created by Terraform.** The templates create the containers (AWS, Google Cloud), the vault (Azure, OCI), and the identity that reads them; values are written out of band and delivered to workloads by External Secrets Operator. Where a service requires a credential at creation time (OCI PostgreSQL), Terraform receives only the OCID of a pre-created secret.

### 5.2 Using a Template

```bash
cd infrastructure/aws            # or azure, gcp, oci
cp backend.hcl.example backend.production.hcl   # edit; never commit account-specific values
terraform init -backend-config=backend.production.hcl
terraform plan -var-file=environments/production.tfvars -out=tfplan
terraform apply tfplan
```

| Rule | Mandatory Behavior |
| --- | --- |
| **One provider per deployment** | An environment uses exactly one provider directory. Multi-cloud deployments require an ADR. |
| **One state per provider per environment** | `backend.<environment>.hcl` points each environment to its own state object; state buckets are created out of band. |
| **Commit the lock file** | Run `terraform init` once, commit the generated `.terraform.lock.hcl`, and update it only during reviewed upgrades. |
| **Placeholders are not defaults** | Identifiers in `environments/*.tfvars` (subscription, project, compartment, tenancy, CIDR allow-lists, object IDs) are placeholders; real values come from the pipeline or git-ignored `*.auto.tfvars`. |
| **Review before first apply** | A security review of the chosen template (IAM, network exposure, sizing, cost, retention) precedes the first apply; deviations are recorded as ADRs or exceptions. |
| **Validate in CI** | `terraform fmt -check`, `terraform validate` (with provider plugins), `tflint`, and `trivy config`/Checkov run for the chosen provider on every pull request ([Section 9](#9-testing-infrastructure-code)). |

</BackingServicesProvisioning>

---

<SecretsAndSensitiveData>
## 6. Secrets and Sensitive Data in IaC

All secret handling complies with [SECURITY.md Section 5](./SECURITY.md#5-secrets-and-sensitive-data-management). IaC-specific rules:

| Requirement | Mandatory Behavior |
| --- | --- |
| **No secrets in code** | Never commit passwords, API keys, tokens, or certificates in IaC files, variable defaults, or `.tfvars`. |
| **External secret injection** | Secrets come from a secrets manager (HashiCorp Vault or the cloud provider's) and reach workloads through External Secrets Operator, Secrets Store CSI, or Compose secrets. |
| **Encrypted state** | State that may contain sensitive data is encrypted and access-controlled. |
| **Sensitive variable marking** | `sensitive = true` on secret variables and outputs. |
| **Gitignore enforcement** | `.tfvars` with secrets, `*.pem`, `*.key`, `.env`, `secrets/`, and state files are in `.gitignore`. |
| **Secret rotation support** | Infrastructure supports rotation without full redeployment (database passwords, RabbitMQ/Valkey credentials, JWT signing keys). |
| **Pre-commit scanning** | `gitleaks` (or equivalent) as a pre-commit hook and in CI. |

```hcl
# Best: let the managed service own the secret — nothing enters state or code
resource "example_database_instance" "main" {
  manage_master_user_password = true # the service generates and rotates it
}

# Good: ephemeral read + write-only argument — the value is never persisted to state or plan
ephemeral "example_secret_version" "db_password" {
  secret_id = "production/database/master-password"
}

# Forbidden: hardcoded secret
resource "example_database_instance" "main" {
  password = "SuperSecret123!"
}
```

</SecretsAndSensitiveData>

---

<SecurityAndCompliance>
## 7. Security and Compliance

### 7.1 Policy as Code

| Requirement | Mandatory Behavior |
| --- | --- |
| **Automated policy enforcement** | Policy-as-code (OPA/Rego via Conftest, Checkov, Trivy misconfiguration scanning, Kyverno for clusters) enforces security and compliance rules. `tfsec` has merged into Trivy and is not introduced in new pipelines. |
| **Pre-apply validation** | Policies are evaluated against the plan before any change is applied. |
| **Policy versioning** | Policies live in `infrastructure/policies/` and are versioned with the code. |
| **Mandatory policies** | At minimum: no public storage buckets, no open security groups (`0.0.0.0/0` on SSH/RDP or on database/cache/broker ports), encryption at rest enabled, logging enabled, deletion protection on stateful production resources, mandatory tags present. A resource missing a required tag fails the policy check. |
| **Policy exceptions** | Documented with owner, risk, rationale, scope, and expiration date. |

### 7.2 Least Privilege for Infrastructure

| Requirement | Mandatory Behavior |
| --- | --- |
| **Scoped IAM/RBAC** | Only the permissions required for the specific operation. No `*` actions or resources. |
| **Service-specific roles** | The API, worker, migration job, and CI pipeline each have their own identity. |
| **No long-lived credentials** | OIDC federation, workload identity, or managed identities. |
| **Pipeline identity isolation** | CI/CD uses dedicated identities with scoped permissions per environment. |
| **Assume-role patterns** | For cross-account access; never share root or admin credentials. |

### 7.3 Network Security Defaults

| Requirement | Mandatory Behavior |
| --- | --- |
| **Deny by default** | Security groups, NACLs, and firewall rules start deny-all and explicitly allow required traffic. |
| **No open ingress** | `0.0.0.0/0` inbound only for public load balancers on ports 80/443. |
| **Egress deny by default** | Application and database security groups deny all egress and allow only required destinations. |
| **Block cloud metadata endpoint** | Deny egress from application workloads to `169.254.169.254`, per [SECURITY.md §10.2](./SECURITY.md#102-kubernetes) and [CONTAINERS.md §7.3](./CONTAINERS.md#73-network-policies). |
| **Private subnets for workloads** | Application, database, cache, and broker run in private subnets with no direct internet access; egress through NAT or provider endpoints. |
| **Encryption in transit** | TLS for all inter-service traffic, including PostgreSQL, Valkey, and RabbitMQ. |
| **Network isolation** | Isolated virtual networks per environment; peering is explicit and documented. |

</SecurityAndCompliance>

---

<CICDPipelineIntegration>
## 8. CI/CD Pipeline Integration

### 8.1 Pipeline Stages

| Stage | Purpose |
| --- | --- |
| **Lint and format** | `fmt -check`, `validate`, `yamllint`. Run `init` without `-upgrade` so provider checksums are verified against the committed lock file; a mismatch fails the pipeline. |
| **Static analysis** | `tflint`, `trivy config`, `checkov`, `kubeconform`. |
| **Plan / Dry-run** | Generate and display the plan. No infrastructure is modified. |
| **Policy evaluation** | Evaluate the plan against policy-as-code. Block non-compliant changes. |
| **Manual approval** | Required for production. |
| **Apply** | Apply the saved plan file; never re-plan during apply. |
| **Smoke test** | Validate that provisioned resources are reachable and functional. |

### 8.2 Drift Detection

| Requirement | Mandatory Behavior |
| --- | --- |
| **Scheduled detection** | At least daily for production, comparing actual to declared state. |
| **Alerting** | Drift triggers an alert routed to the responsible owner. |
| **Remediation** | Automatic where safe; human review where unsafe. |
| **Audit trail** | Drift events are logged with timestamp, resource, expected vs. actual state, and resolution. |

### 8.3 Approval Gates

| Environment | Required Approvals |
| --- | --- |
| **Development** | Automated after all checks pass. |
| **Staging** | At least one reviewer after plan review. |
| **Production** | At least two approvals (an infrastructure owner and a second reviewer); the plan diff is reviewed. For a single-maintainer portfolio setup, the second approval is a recorded self-review after a 24-hour cooling-off period, documented as an exception. |

</CICDPipelineIntegration>

---

<TestingInfrastructureCode>
## 9. Testing Infrastructure Code

Infrastructure tests follow [TESTS.md](./TESTS.md) principles:

| Test Type | Scope | Tools |
| --- | --- | --- |
| **Static analysis** | Syntax, formatting, anti-patterns, misconfiguration. | `terraform validate`, `tflint`, `trivy config`, `checkov`, `kubeconform` |
| **Unit tests** | Module logic and variable validation in isolation. | `terraform test` with `mock_provider` |
| **Integration tests** | Provision real resources in an isolated environment. | `terraform test` (apply mode), Terratest (Go) |
| **Policy/compliance tests** | Provisioned infrastructure meets policies. | Conftest, Kyverno CLI, Checkov |
| **Smoke tests** | Post-apply connectivity, DNS, endpoints, health. | The application's `/health/ready`, `curl`, synthetic monitors |

| Requirement | Mandatory Behavior |
| --- | --- |
| **Tests in CI** | Static analysis and unit tests on every pull request; integration tests before a production deploy. |
| **Isolated environments** | Integration tests provision and destroy their own resources. |
| **Cleanup** | Teardown runs even on failure. |
| **No shared state** | Tests share neither state nor inventory with other tests or environments. |

</TestingInfrastructureCode>

---

<DocumentationAndChangeManagement>
## 10. Documentation and Change Management

| Requirement | Mandatory Behavior |
| --- | --- |
| **Module README** | Every reusable module has purpose, inputs, outputs, examples, prerequisites. |
| **Architecture diagrams** | Up-to-date diagrams of resource topology and network layout in `docs/`. |
| **Change records** | Production infrastructure changes record what changed, why, who approved, and the rollback plan (the pull request is the record). |
| **Runbooks** | Step-by-step runbooks for scaling, failover, backup restore, secret rotation, and disaster recovery. |
| **ADRs for infrastructure** | Significant decisions (cloud provider, state backend, managed vs. self-hosted services) are ADRs per [ARCHITECTURE.md Section 11](./ARCHITECTURE.md). |
| **Tagging for traceability** | Every resource is tagged with the commit SHA or pipeline run ID that created it. |

</DocumentationAndChangeManagement>

---

<DefinitionOfDone>
## 11. IaC Definition of Done

A delivery that impacts infrastructure is complete only when all items below are true:

1. All infrastructure is defined declaratively in version-controlled code. No manual changes were made.
2. Modules follow single responsibility with explicit input/output interfaces.
3. Provider, module, and tool versions are pinned.
4. State is remote, locked, encrypted, and isolated per environment.
5. No secrets, credentials, or sensitive values are committed.
6. Static analysis and policy-as-code checks pass with no blocking findings.
7. Unit and integration tests pass for modified modules.
8. The plan was reviewed and approved before applying.
9. Security requirements from [SECURITY.md](./SECURITY.md) are addressed: least privilege, encryption, network isolation, logging.
10. All resources carry the mandatory tags.
11. Stateful resources have deletion protection, backups, and a tested restore procedure.
12. Drift detection is configured for the target environment.
13. Documentation (READMEs, diagrams, runbooks) is updated.
14. A rollback strategy is defined for the change.
15. Smoke tests validate provisioned resources after apply.

Any exception to these standards must be documented with owner, scope, risk, rationale, and expiration date.

</DefinitionOfDone>

---

_Last updated: September 30, 2026_
_These standards must be reviewed and updated at minimum once per year or after any significant IaC incident._
