# Kubernetes

Manifests to run the platform on Kubernetes, following `ai/CONTAINERS.md §6–§8` and `§12`: **Kustomize**, one base and one
overlay set per environment, no duplicated manifests, nothing applied by hand in production (GitOps). Compose
(`docker-compose-prod.yml`) remains the single-host path.

## What runs where

| Piece                            | Where                           | Notes                                                                                                |
| -------------------------------- | ------------------------------- | ---------------------------------------------------------------------------------------------------- |
| API (`ecommerce-api`)            | `base/app`                      | HTTP 8080, HPA on CPU, PDB, Ingress with explicit routes                                             |
| Worker                           | `base/app`                      | Same image; `Outbox__Relay__Enabled` and `RabbitMq__ConsumersEnabled`; scaled by KEDA on queue depth |
| RabbitMQ                         | `base/rabbitmq`                 | `RabbitmqCluster` (RabbitMQ Cluster Operator), 3 nodes, quorum queues, PVC                           |
| OpenTelemetry Collector          | `base/otel-collector`           | One replica (tail sampling); pipeline = `observability/otel-collector/config.yaml`, the Compose file |
| Database release                 | `migrations`                    | One Job: provision roles, migrate Identity/Catalog/Inventory, grant. Runs before the workloads       |
| PostgreSQL, Valkey               | **outside** (`infrastructure/`) | Managed services; only their connection strings are secrets here                                     |
| Prometheus, Loki, Tempo, Grafana | **outside** (platform)          | Collector exports to the `observability` namespace (`PROMETHEUS_/LOKI_/TEMPO_OTLP_ENDPOINT`)         |
| PriorityClass                    | `cluster`                       | Cluster-scoped, applied once per cluster                                                             |

```
kubernetes/
├── cluster/                    cluster-scoped: PriorityClass
├── base/
│   ├── app/                    API, worker, Service, Ingress, HPA, KEDA, PDBs, ExternalSecrets, NetworkPolicies, config
│   ├── rabbitmq/               RabbitmqCluster, its default-user ExternalSecret, NetworkPolicies
│   └── otel-collector/         Deployment, Service, NetworkPolicies, config from ../../observability
├── migrations/                 the database release Job (+ ../database provisioning script)
└── overlays/<env>/             development | staging | production
    ├── foundation/             1. Namespace (Pod Security: restricted), ResourceQuota, LimitRange, SecretStore
    ├── migrations/             2. the release Job for this environment
    └── workloads/              3. everything else
```

An overlay sets the namespace, the image digests, the public host, scale, the storage class and the VPC CIDR of the
network policies. Everything else is shared, so staging mirrors production (`ai/CONTAINERS.md §12`).

## Prerequisites (platform add-ons, installed once per cluster)

Not part of this repository's manifests; the objects here assume them.

| Add-on                                                                         | Used for                                                                       |
| ------------------------------------------------------------------------------ | ------------------------------------------------------------------------------ |
| Kubernetes ≥ 1.30                                                              | native `preStop` `sleep`; `restricted` Pod Security Admission                  |
| ingress-nginx (namespace `ingress-nginx`)                                      | the Ingress and the policy that admits it                                      |
| cert-manager + ClusterIssuers `letsencrypt-staging` / `letsencrypt-production` | TLS certificate of the Ingress                                                 |
| External Secrets Operator (v1 API)                                             | every `ExternalSecret`; its cloud identity is provisioned by `infrastructure/` |
| RabbitMQ Cluster Operator (namespace `rabbitmq-system`)                        | `RabbitmqCluster`                                                              |
| KEDA (namespace `keda`)                                                        | `ScaledObject` / `TriggerAuthentication` of the worker                         |
| Metrics server                                                                 | the HPA                                                                        |
| Prometheus/Loki/Tempo in `observability`                                       | where the Collector exports                                                    |
| A policy engine (Kyverno or Gatekeeper)                                        | image signature verification at admission (`ai/CONTAINERS.md §2.6`)            |

## Secrets

No Secret value is in Git. The `ExternalSecret`s read these containers from the environment's secret store
(`pf-ecommerce-<environment>/<name>` on AWS; created by `infrastructure/*/secrets.tf`, values written out of band):

| Secret container                                                                        | Content                                                | Mounted as                                               |
| --------------------------------------------------------------------------------------- | ------------------------------------------------------ | -------------------------------------------------------- |
| `jwt-signing-key`                                                                       | ES384 private key (PEM)                                | `/run/secrets/jwt_es384_private_key` (API, worker)       |
| `identity-token-hash-key`                                                               | token HMAC key                                         | `Identity__TokenHashKey`                                 |
| `postgres-runtime-connection`                                                           | `app_runtime` connection string, TLS required          | `ConnectionStrings__Postgres`                            |
| `valkey-connection`                                                                     | Valkey connection string with password and TLS         | `ConnectionStrings__Valkey`                              |
| `rabbitmq-credentials`                                                                  | JSON `{"username","password"}`                         | `ConnectionStrings__RabbitMQ`, broker default user, KEDA |
| `identity-bootstrap-admin`                                                              | JSON `{"email","password"}` of the first administrator | API only                                                 |
| `postgres-admin-credentials`                                                            | JSON with the instance admin `password`                | release Job (provisioning)                               |
| `postgres-migrator-password`, `postgres-runtime-password`, `postgres-readonly-password` | role passwords                                         | release Job (provisioning)                               |
| `postgres-migrator-connection`                                                          | `app_migrator` (DDL) connection string                 | release Job (migration bundles)                          |

The runtime and migrator passwords exist twice (as a role password and inside a connection string): keep them equal when
rotating, then re-run the release Job (it resets the role passwords) and `kubectl rollout restart` the Deployments. Use
`SSL Mode=VerifyFull` (and `ssl=true` for Valkey) in the connection strings: the manifests carry no database or cache TLS
setting, the secret does.

## Releasing

Images are built, scanned and signed by CI, then pinned **by digest** in the overlay (`images:`, plus the
`app.kubernetes.io/version` label). The pipeline, in order (`ai/CONTAINERS.md §10.2`):

```bash
env=production
digest_api=sha256:...        # from the build
digest_migrations=sha256:...

# 0. set the release in the overlays (committed to the GitOps repository)
( cd kubernetes/overlays/$env/workloads  && kustomize edit set image registry.example.com/ecommerce-api@$digest_api )
( cd kubernetes/overlays/$env/migrations && kustomize edit set image registry.example.com/ecommerce-migrations@$digest_migrations )

# 1. once per environment (and when it changes): namespace, quotas, secret store
kubectl apply -k kubernetes/overlays/$env/foundation

# 2. the database release. A Job spec is immutable: remove the previous run first.
kubectl -n ecommerce-$env delete job ecommerce-db-release --ignore-not-found
kubectl apply -k kubernetes/overlays/$env/migrations
kubectl -n ecommerce-$env wait --for=condition=complete job/ecommerce-db-release --timeout=30m

# 3. the workloads: rolling update, maxUnavailable 0
kubectl apply -k kubernetes/overlays/$env/workloads
kubectl -n ecommerce-$env rollout status deploy/ecommerce-api deploy/ecommerce-worker --timeout=10m
```

In production these `kubectl apply` steps are performed by the GitOps controller, never by hand (`ai/IAC.md`):

- **Argo CD:** three Applications in the order foundation → migrations → workloads (sync waves). The Job carries
  `argocd.argoproj.io/hook: PreSync` and `BeforeHookCreation`, so it is recreated at every sync.
- **Flux:** three `Kustomization`s chained with `dependsOn`, the workloads one with `wait: true`. The Job carries
  `kustomize.toolkit.fluxcd.io/force: enabled`, so Flux recreates it when its spec changes.

**Rollback.** `kubectl rollout undo deploy/ecommerce-api deploy/ecommerce-worker`, or redeploy the previous digest. It is safe
because migrations are expand/contract (`ai/DATABASE.md §6`): the previous version works against the migrated schema. The
release Job is not rolled back; a destructive migration waits for the contract phase of a later release.

## Operating notes

- **Connection budget.** Each Pod may hold `Database:MaxPoolSize` (20) connections. Production: API ≤ 8 Pods + workers ≤ 6 =
  14 × 20 = **280** connections, plus the release Job. The managed instance's `max_connections` must exceed that with room
  for operators; lower the pool or the `maxReplicas` before raising either (`ai/CONTAINERS.md §9.3`).
- **Worker scale.** KEDA scales on the depth of each consumer queue (`worker-scaledobject.yaml`); add a trigger when a
  consumer is added (`docs/messaging.md`). Dead-letter queues (`*.dead`) are not consumed: alert on their depth.
- **Telemetry.** Pods export OTLP to `otel-collector:4317`. The Collector runs one replica because tail sampling needs a whole
  trace in one instance.
- **Probes.** `/health/live` (startup and liveness, no I/O) and `/health/ready` (readiness). Valkey and RabbitMQ degrade
  readiness to _Degraded_, which keeps the Pod serving. Probes come from the kubelet and bypass the NetworkPolicies.
- **Not routed publicly.** `/health/*`, `/api/v1/docs`, `/api/v1/openapi` and `/api/v1/diagnostics` are absent from the
  Ingress paths on purpose; the application also serves the first two in `Development` only.
- **External providers.** Egress to payment, carrier and e-mail/SMS endpoints is closed. When an integration lands, add an
  allowlisted CIDR rule to `app-egress` in the overlay.
- **Exceptions.** `docs/container-exceptions.md` EX-005 (RabbitMQ root filesystem) and EX-006 (no TLS to RabbitMQ and the
  Collector inside the cluster) document the two deviations in this directory.

## Validating changes

```bash
for env in development staging production; do
  for part in foundation migrations workloads; do
    kubectl kustomize kubernetes/overlays/$env/$part | kubeconform -strict -kubernetes-version 1.32.0 \
      -schema-location default \
      -schema-location 'https://raw.githubusercontent.com/datreeio/CRDs-catalog/main/{{.Group}}/{{.ResourceKind}}_{{.ResourceAPIVersion}}.json' -
  done
done
kubectl kustomize kubernetes/cluster
```

CI runs this on every pull request (`.github/workflows/ci.yml`, job `kubernetes`); `tests/Portfolio.ArchitectureTests`
(`KubernetesConventionTests`) also fails on a plain `Secret`, a `hostPath`/`hostNetwork`/`hostPort`, a floating image tag, a
container without a security context or resources, or an overlay that does not build to the same shape. Policy checks
(Kyverno/Conftest) and `kubectl --dry-run=server` against a test cluster run in the deployment pipeline.

## Known limits

- The operator-specific details (`RabbitmqCluster` overrides, the external default-user Secret keys) follow the operator's
  documentation and must be confirmed once in a real staging cluster; nothing in this directory has been applied to one yet.
- The HPA scales on CPU only. A request-rate or latency metric (`ai/CONTAINERS.md §6.5`) needs a custom-metrics adapter and is
  to be added in the overlays when one exists.
- The `SecretStore` is AWS Secrets Manager. For Azure, Google Cloud or OCI replace the provider block in
  `overlays/<env>/foundation/secret-store.yaml` (and the storage class and cloud annotations in the workloads overlay).
