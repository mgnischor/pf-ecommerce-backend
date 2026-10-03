# Identity — Business Rules

Rules catalog for the **Identity** bounded context (`BR-IDN-*`), as required by `ai/BUSINESS.md §3`. Every rule is
enforced in the Domain or Application layer, has stable error codes, and is covered by unit tests tagged
`[Trait("Rule", "BR-IDN-xxx")]`; the HTTP behavior is covered by integration tests.

> **Ownership and sources.** Owner and requirement source are placeholders to be assigned by the maintainers
> (same convention as `catalog.md`). Effective date for every rule: 2026-09-30.

## Access levels

Access is **hierarchical**: a higher level holds everything a lower level may do. The five levels, lowest first:

| Level             | Wire name       | Who                                          | Typical permissions                                                                                                                       |
| ----------------- | --------------- | -------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------- |
| 1 — Public        | `public`        | Anonymous visitors and registered customers  | Read the catalog, sign in, register; customers reach **their own** carts, orders, payments, shipments (ownership is checked per resource) |
| 2 — Collaborator  | `collaborator`  | Day-to-day staff                             | Create and edit draft products, read stock levels                                                                                         |
| 3 — Manager       | `manager`       | Staff who approve business-impacting changes | Change prices, activate and discontinue products, adjust stock, refund payments                                                           |
| 4 — Administrator | `administrator` | Account and destructive-action owners        | Create and manage accounts, delete products                                                                                               |
| 5 — Developer     | `developer`     | Platform engineers                           | Everything above, plus technical diagnostics (`GET /api/v1/diagnostics/runtime`)                                                          |

Anonymous access is declared explicitly with `[AllowAnonymous]`; every other endpoint names a policy
(`Access.Authenticated`, `Access.Collaborator`, `Access.Manager`, `Access.Administrator`, `Access.Developer`).
A fallback policy denies anything that declares nothing, and an architecture test fails the build when an endpoint
does not declare its authorization.

### Endpoint matrix

| Minimum level                  | Endpoints                                                                                                                                                                                                                                                          |
| ------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| anonymous                      | `POST /auth/tokens`, `POST /auth/tokens/refresh`, `POST /auth/tokens/revocation`, `POST /auth/registrations`, `GET /.well-known/jwks.json`, `GET /products`, `GET /products/{id}`, `POST /payments/webhooks/{provider}` (authenticity from the provider signature) |
| public (any signed-in account) | `GET /auth/me`, carts, checkout, orders, `GET /payments/{id}`, shipments, `GET /customers/me`, `PATCH /customers/me`                                                                                                                                               |
| collaborator                   | `POST /products`, `PATCH /products/{id}`, `GET /inventory/items/{sku}`                                                                                                                                                                                             |
| manager                        | `PUT /products/{id}/price`, `POST /products/{id}/activation`, `POST /products/{id}/discontinuation`, `POST /payments/{id}/refunds`, `POST /inventory/items/{sku}/adjustments`                                                                                      |
| administrator                  | `DELETE /products/{id}`, `POST /users`, `PUT /users/{id}/access-level`, `POST /users/{id}/deactivation`                                                                                                                                                            |
| developer                      | `GET /diagnostics/runtime`                                                                                                                                                                                                                                         |

The matrix above is enforced by `AuthorizationMatrixTests`: every protected endpoint is called as anonymous and as
each of the five levels, and the response must follow the policy exactly (`401`, `403`, or past authorization).

## Summary

| ID         | Name                                       | Classification   | Criticality | Enforced in                                             |
| ---------- | ------------------------------------------ | ---------------- | ----------- | ------------------------------------------------------- |
| BR-IDN-001 | Account E-mail Constraint                  | Constraint       | Compliance  | `EmailAddress`, `IUserRepository`                       |
| BR-IDN-002 | Password Policy                            | Constraint       | Compliance  | `PasswordPolicy`, `Argon2idPasswordHasher`              |
| BR-IDN-003 | Sign-in Throttling and Uniform Failure     | Constraint       | Compliance  | `User`, `SignInHandler`                                 |
| BR-IDN-004 | Access Level Assignment                    | Authorization    | Compliance  | `AccessManagementRules`, policies                       |
| BR-IDN-005 | Refresh Token Rotation and Reuse Detection | State Transition | Compliance  | `RefreshToken`, `RefreshTokenHandler`                   |
| BR-IDN-006 | Account Deactivation and Token Revocation  | State Transition | Compliance  | `User`, `DeactivateUserHandler`, `AccessTokenValidator` |

## BR-IDN-001 — Account E-mail Constraint

| Attribute       | Value                                                                                                                                                                                                                                                                                                                                                   |
| --------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Description     | An e-mail is trimmed and lowercased, is at most 254 characters (local part at most 64), has exactly one `@`, a domain with at least two dot-separated labels, and only printable ASCII without specials. Non-ASCII characters are rejected on purpose: look-alike addresses must not create distinct accounts. At most one account uses a given e-mail. |
| Error behavior  | `EMAIL_INVALID` (validation, field `email`); `EMAIL_ALREADY_REGISTERED` (conflict).                                                                                                                                                                                                                                                                     |
| Known trade-off | Registration answers `409` for an existing e-mail, which allows account enumeration through that one endpoint. It is rate limited; the uniform `202` alternative needs the e-mail verification flow, which does not exist yet.                                                                                                                          |

> **Registration profile.** `POST /auth/registrations` also accepts an optional `fullName`, `phone`, `locale` and
> `timeZone` (size-bounded only). Identity neither stores nor judges them: a self-registration raises
> `CustomerRegistered` next to `UserRegistered`, carrying the e-mail and these values to the Customers context, which owns
> their rules and creates the profile (BR-CUS-006). `UserRegistered` stays free of personal data; accounts an administrator
> creates raise only `UserRegistered` and get no profile.

## BR-IDN-002 — Password Policy

| Attribute      | Value                                                                                                                                                                                                                                                                                                                                                                                                                                                |
| -------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Description    | NIST SP 800-63B: 12 to 128 characters, no composition rules, no forced rotation; rejected when it contains the account's e-mail name (4+ characters) or appears in the breached-password corpus. Stored only as an Argon2id PHC string: 64 MiB memory, 3 iterations, parallelism 1, 16-byte salt, 32-byte hash. Weaker parameters in configuration stop the host. Verifiers made with weaker parameters are upgraded at the next successful sign-in. |
| Error behavior | `PASSWORD_LENGTH` (params `min`, `max`), `PASSWORD_CONTAINS_EMAIL`, `PASSWORD_COMPROMISED`; the rejected password is never echoed.                                                                                                                                                                                                                                                                                                                   |
| Known gap      | The corpus is a short built-in list (`CommonPasswordScreen`); an offline Have I Been Pwned range copy replaces it through `IBreachedPasswordScreen`.                                                                                                                                                                                                                                                                                                 |

## BR-IDN-003 — Sign-in Throttling and Uniform Failure

| Attribute      | Value                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| -------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Description    | Five consecutive failed sign-ins lock the account for 15 minutes; a success clears the counter. Every failure — malformed e-mail, unknown account, wrong password, locked or deactivated account — returns the same `401 INVALID_CREDENTIALS` after the same amount of hashing work, so neither the body nor the timing reveals whether an account exists. Sign-in, refresh, and registration are also rate limited per client (`429` with `Retry-After`). |
| Error behavior | `INVALID_CREDENTIALS` (unauthorized), `RATE_LIMITED` (429).                                                                                                                                                                                                                                                                                                                                                                                                |

## BR-IDN-004 — Access Level Assignment

| Attribute      | Value                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                |
| -------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Description    | Privileges only flow downward. Only administrators and developers manage accounts. A caller cannot grant a level above their own, cannot manage themselves, and cannot manage an account that is not strictly below them — except developers, who may manage any other account. Self-registration always creates a `public` account and the request cannot name a level. Changing a level bumps the account's token version, so tokens carrying the old level stop working immediately. Every change is audited with actor, target, and both levels. |
| Error behavior | `ACCESS_LEVEL_INSUFFICIENT`, `ACCESS_LEVEL_ESCALATION`, `USER_NOT_MANAGEABLE` (forbidden); `ACCESS_LEVEL_INVALID` (validation).                                                                                                                                                                                                                                                                                                                                                                                                                      |

## BR-IDN-005 — Refresh Token Rotation and Reuse Detection

| Attribute      | Value                                                                                                                                                                                                                                                                                                                                                                                                                                           |
| -------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Description    | Refresh tokens are opaque 256-bit values stored only as HMAC-SHA3-512. Each use consumes the token and issues its successor in the same session (family). A token lasts at most 7 days and a session at most 30, however often it is refreshed. Presenting an already-consumed token means it leaked: the whole family is revoked and every access token of the account is invalidated, and the client still only sees `INVALID_REFRESH_TOKEN`. |
| Error behavior | `INVALID_REFRESH_TOKEN` (unauthorized, uniform for unknown, expired, revoked, and reused tokens). Reuse is logged at `Critical` (`auth.refresh_token.reuse_detected`).                                                                                                                                                                                                                                                                          |

## BR-IDN-006 — Account Deactivation and Token Revocation

| Attribute   | Value                                                                                                                                                                                                                                                                                                                                                                                      |
| ----------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Description | Deactivating an account stops it from authenticating, invalidates every access token it holds (token-version bump), and revokes every refresh-token session. The operation is idempotent. Signing out revokes the session behind the refresh token and blocks the current access token's `jti` until it would have expired; it always answers `204`, so it cannot be used to probe tokens. |

## Token specification

| Property        | Value                                                                                                                                                                                                             |
| --------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Algorithm       | ES384 (ECDSA P-384), selected through an explicit allowlist; `alg: none` and HMAC algorithms are rejected                                                                                                         |
| Header `typ`    | `at+jwt` (validated)                                                                                                                                                                                              |
| Header `kid`    | Required; the verification key is looked up by `kid`, never by trying keys                                                                                                                                        |
| Claims          | `iss`, `aud`, `sub`, `jti`, `iat`, `nbf`, `exp`, `access_level`, `ver` — no e-mail or other personal data                                                                                                         |
| Lifetime        | Access: 10 minutes by default, 15 at most. Refresh: 7 days. Session: 30 days                                                                                                                                      |
| Validation      | Exact issuer and audience, signature, expiry (30-second skew), `iat` not in the future; then the account must exist, be active, and match the token's `ver` and `access_level`, and the `jti` must not be revoked |
| Transport       | `Authorization: Bearer` only; tokens in the query string are ignored                                                                                                                                              |
| Key publication | `GET /.well-known/jwks.json` (public keys only). Rotation keeps retired public keys for the maximum access-token lifetime                                                                                         |

## Operating the secrets

Nothing sensitive is committed (`.gitignore` excludes `.env`, `*.pem`, `*.key`, `secrets/`).

| Setting                                                                                 | Purpose                                                                                           |
| --------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------- |
| `Jwt:ActiveKeyId`, `Jwt:Keys:N:Id`, `Jwt:Keys:N:PrivateKeyPem` or `…:PrivateKeyPemFile` | ES384 signing keys. Retired keys: `…:PublicKeyPem` only                                           |
| `Identity:TokenHashKey`                                                                 | Base64 key of at least 32 random bytes for the refresh-token HMAC                                 |
| `Identity:Bootstrap:Accounts:N:Email`, `…:Password`, `…:AccessLevel`                    | Accounts created at startup when missing. This is how the first administrator and developer exist |

Outside `Development` the host **refuses to start** without a signing key or a token-hash key, with token settings
above the limits, with Argon2id parameters below the minimums, or with a bootstrap account whose password violates the
policy. `Development` generates throw-away keys and logs a warning.

Generate a P-384 key (keep the file outside the repository):

```bash
openssl ecparam -name secp384r1 -genkey -noout | openssl pkcs8 -topk8 -nocrypt -out jwt-es384.pem
```

Local development with user secrets:

```bash
dotnet user-secrets set "Identity:Bootstrap:Accounts:0:Email" "admin@example.com" --project Portfolio.csproj
dotnet user-secrets set "Identity:Bootstrap:Accounts:0:Password" "<a long random passphrase>" --project Portfolio.csproj
dotnet user-secrets set "Identity:Bootstrap:Accounts:0:AccessLevel" "developer" --project Portfolio.csproj
```

## Known limitations

1. **Accounts and refresh tokens live in PostgreSQL** (schema `identity`, `docs/database.md`). The `version` column
   arbitrates concurrent refresh-token rotation across instances: of two simultaneous refreshes of one token, one
   commits and the other is refused (`409`, `CONCURRENT_UPDATE`); two sessions never come out of one token. Each
   authenticated request reads the account to validate the token version, which makes revocation immediate. The account
   state is cached in Valkey for 30 s and evicted by domain events after commit (`docs/caching.md`), so the common case
   costs no query and deactivation still takes effect at once.
2. **The `jti` blocklist lives in Valkey** with a TTL equal to the remaining token life. It fails closed by default: if
   Valkey cannot be read, access tokens are refused (`Valkey:RevocationCheckFailureMode`, `Allow` is an explicit
   availability trade-off). It shares an instance with the caches; move it to a `noeviction` instance before memory gets tight.
3. **Client IP for rate limiting** is the socket address; behind a proxy, forwarded headers must be configured with
   trusted proxies before the limits are meaningful.
4. **No MFA, e-mail verification, or password reset yet.** `ai/SECURITY.md §2.1` (A07) requires MFA for staff accounts;
   until it exists, staff accounts rely on the password policy, lockout, and rate limiting alone.
5. **No per-account rate limit** (only per client address); the lockout covers the per-account case.
