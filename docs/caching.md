# Caching (Valkey)

How the platform uses **Valkey** through StackExchange.Redis and `HybridCache`, following `ai/DATABASE.md §4`. Valkey is
a cache and a store of short-lived hints, never of durable business state: PostgreSQL stays the source of truth.

## Layout

| Concern                                                                  | Where                                                            |
| ------------------------------------------------------------------------ | ---------------------------------------------------------------- |
| Options, connection, health check, registration                          | `src/SharedKernel/Infrastructure/Caching/Valkey*.cs`             |
| Cache-aside boundary (`IResilientCache`) and the declaration of an entry | `ResilientCache.cs`, `CacheEntry.cs`                             |
| Account-state cache and its event-driven invalidation                    | `src/Identity/Infrastructure/Caching/`                           |
| Access-token (`jti`) blocklist                                           | `src/Identity/Infrastructure/Caching/ValkeyRevokedTokenStore.cs` |

## Rules the code enforces

- **Every entry is declared as a `CacheEntry`** (name, context, entity, version, TTL up to 1 h). Keys are
  `{prefix}:{context}:{entity}:{id}:v{n}`; changing the shape of a value means bumping `Version`.
  `TelemetryAndCacheConventionTests` fails the build for an entry without TTL or version.
- **Only the cache boundary touches the client.** Nothing outside `ResilientCache`, `ValkeyConnection`, the health check,
  the registration, and the blocklist may use StackExchange.Redis or `HybridCache` (architecture test).
- **A cache failure never reaches a request.** Short timeouts (`Valkey:ConnectTimeoutMilliseconds` 500,
  `Valkey:OperationTimeoutMilliseconds` 250), `AbortOnConnectFail=false`, single-flight on a miss, and a bypass to the
  source of truth while Valkey is unavailable (`HybridCache` would otherwise swallow the error silently). Failures are
  logged at most once per 30 s and counted in `app.cache.errors`.
- **Invalidation is event-driven.** `IDomainEventSubscriber` handlers run after the transaction commits, so a rolled-back
  change never evicts and a committed one evicts immediately (`AccountCacheInvalidator`).
- **Payloads are bounded** (`Valkey:MaximumPayloadBytes`, 64 KiB) and contain no secrets.

## Entries

| Name               | Key                                      | TTL  | Invalidated by                                                     |
| ------------------ | ---------------------------------------- | ---- | ------------------------------------------------------------------ |
| `identity.account` | `ecommerce:identity:account:{userId}:v1` | 30 s | account deactivation, access-level change, token revocation events |

The TTL bounds staleness if an invalidation is ever lost; the events make the normal case immediate.

## Access-token blocklist

Signing out blocks the `jti` of the current access token until it would have expired (TTL = remaining token life).
Token validation reads it on every request.

- **Fail closed by default.** If Valkey cannot be read, the token is refused (`Valkey:RevocationCheckFailureMode` =
  `Deny`). `Allow` exists for deployments that prefer availability over immediate revocation; choosing it is an
  explicit, documented risk decision.
- The blocklist shares the instance (and the `allkeys-lru` policy) with the caches. Under memory pressure an entry could
  be evicted early. Move it to its own instance (`noeviction`) before the memory budget gets tight.

## Configuration

| Setting                                | Meaning                                            | Default             |
| -------------------------------------- | -------------------------------------------------- | ------------------- |
| `ConnectionStrings__Valkey`            | Connection string (**secret**; never logged)       | none (**required**) |
| `Valkey__KeyPrefix`                    | First segment of every key                         | `ecommerce`         |
| `Valkey__ConnectTimeoutMilliseconds`   | First connection / reconnection attempt            | 500                 |
| `Valkey__OperationTimeoutMilliseconds` | Per-command budget                                 | 250                 |
| `Valkey__MaximumPayloadBytes`          | Largest cached value                               | 65536               |
| `Valkey__RevocationCheckFailureMode`   | `Deny` or `Allow` when the blocklist is unreadable | `Deny`              |

Readiness (`/health/ready`) reports Valkey as **Degraded**, not Unhealthy: the API still serves requests from
PostgreSQL.

## Tests

`tests/Portfolio.IntegrationTests/Caching/` runs against a real Valkey (Testcontainers, same pinned image as Compose):
hit/miss, TTL, single-flight, oversized payloads, outage and recovery after a container stop/start, blocklist
fail-closed/open, and invalidation. `Http/ValkeyApiTests.cs` proves the HTTP-level effects: a deactivated account is
refused immediately, a replayed refresh token revokes the family, and the API keeps serving while Valkey is down.
