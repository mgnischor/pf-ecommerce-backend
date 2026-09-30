# Security Standards

> **Scope:** These standards apply to the `pf-ecommerce-backend` repository: an internet-facing e-commerce HTTP API written in **C# / .NET 10** (ASP.NET Core, Kestrel), backed by **PostgreSQL**, **Valkey**, and **RabbitMQ**, handling customer accounts, personal data, and payment flows. Every item listed here is a hard requirement unless explicitly noted as conditional. Security is a non-negotiable quality attribute — it is addressed from the first line of code and maintained throughout the software lifecycle.

---

## Table of Contents

1. [General Principles](#1-general-principles)
2. [OWASP Compliance](#2-owasp-compliance)
    - 2.1 [OWASP Top 10:2025](#21-owasp-top-102025-web-applications)
    - 2.2 [OWASP API Security Top 10](#22-owasp-api-security-top-10)
    - 2.3 [E-Commerce Abuse Cases](#23-e-commerce-abuse-cases)
3. [MITRE Compliance](#3-mitre-compliance)
4. [Cryptography Standards](#4-cryptography-standards)
    - 4.1 [Symmetric Encryption — ChaCha20-Poly1305](#41-symmetric-encryption--chacha20-poly1305)
    - 4.2 [Password Hashing — Argon2id / PBKDF2 / bcrypt](#42-password-hashing--argon2id--pbkdf2--bcrypt)
    - 4.3 [General Hashing — SHA3-512](#43-general-hashing--sha3-512)
    - 4.4 [Approved Cryptographic Libraries](#44-approved-cryptographic-libraries)
5. [Secrets and Sensitive Data Management](#5-secrets-and-sensitive-data-management)
6. [HTTP and Edge Hardening](#6-http-and-edge-hardening)
    - 6.1 [Universal Requirements](#61-universal-requirements)
    - 6.2 [Kestrel and ASP.NET Core](#62-kestrel-and-aspnet-core)
    - 6.3 [Reverse Proxy and Ingress](#63-reverse-proxy-and-ingress)
    - 6.4 [Secure and Efficient CORS Policy](#64-secure-and-efficient-cors-policy)
    - 6.5 [Secure File Upload Policy](#65-secure-file-upload-policy)
    - 6.6 [API Documentation Exposure](#66-api-documentation-exposure)
7. [JWT Authentication Best Practices](#7-jwt-authentication-best-practices)
    - 7.1 [Algorithm Selection](#71-algorithm-selection)
    - 7.2 [Token Lifetime](#72-token-lifetime)
    - 7.3 [Claims Validation](#73-claims-validation)
    - 7.4 [Storage and Transmission](#74-storage-and-transmission)
    - 7.5 [Payload Confidentiality](#75-payload-confidentiality)
    - 7.6 [Revocation and Key Rotation](#76-revocation-and-key-rotation)
    - 7.7 [Cookie-Based Authentication Security](#77-cookie-based-authentication-security)
8. [OAuth2 Authentication Best Practices](#8-oauth2-authentication-best-practices)
9. [Dependency and Supply Chain Security](#9-dependency-and-supply-chain-security)
10. [Container Security](#10-container-security)
    - 10.1 [Docker](#101-docker)
    - 10.2 [Kubernetes](#102-kubernetes)
11. [Logging and Monitoring](#11-logging-and-monitoring)
    - 11.1 [What to Log](#111-what-to-log)
    - 11.2 [What Never to Log](#112-what-never-to-log)
    - 11.3 [Log Format and Integrity](#113-log-format-and-integrity)
    - 11.4 [Alerting](#114-alerting)
    - 11.5 [Logging Subsystem and Output Channels](#115-logging-subsystem-and-output-channels)
12. [Incident Response](#12-incident-response)
13. [Cloud Provider Security (Conditional)](#13-cloud-provider-security-conditional)
14. [Recommended Security Libraries and Tooling](#14-recommended-security-libraries-and-tooling)
    - 14.1 [Authentication, Authorization, and Web Protections](#141-authentication-authorization-and-web-protections)
    - 14.2 [Security Scanning in CI](#142-security-scanning-in-ci)
15. [Security Definition of Done](#15-security-definition-of-done)

---

<GeneralPrinciples>
## 1. General Principles

Security decisions follow a layered, defense-in-depth approach:

| Principle | Description |
| --- | --- |
| **Least Privilege** | Every component, service, and user account operates with the minimum permissions needed. |
| **Fail Securely** | On error, deny access by default and never expose internal state. |
| **Defense in Depth** | Multiple independent controls so that the failure of one layer does not compromise the system. |
| **Zero Trust** | No caller — internal or external — is implicitly trusted. Every request is authenticated and authorized. |
| **Secure by Default** | Features that introduce risk must be explicitly enabled. Insecure configurations are never the default. |
| **Immutable Audit Trail** | Security-relevant events are logged in a tamper-resistant, append-only manner. |

</GeneralPrinciples>

---

<OWASPCompliance>
## 2. OWASP Compliance

The project addresses the **OWASP Top 10 (2025 edition)** and the **OWASP API Security Top 10 (2023 edition)**. When OWASP publishes a new edition, this section is re-mapped within one review cycle.

### 2.1 OWASP Top 10:2025 (Web Applications)

| # | Risk | Mandatory Mitigation |
| --- | --- | --- |
| A01 | Broken Access Control (includes SSRF) | Enforce server-side authorization on every request with ASP.NET Core authorization policies; deny by default (`FallbackPolicy` requires an authenticated user; anonymous endpoints are explicit `[AllowAnonymous]`). Never rely on client-supplied roles, prices, or identifiers. For SSRF: validate and allowlist every URL fetched by the server (webhooks, image imports) and block internal ranges (RFC 1918, loopback, link-local, cloud metadata endpoints). |
| A02 | Security Misconfiguration | Disable unused features, ports, headers, and services. Apply [Section 6](#6-http-and-edge-hardening). Disable DTD processing in any XML parser (XXE). Run configuration scanning in CI. Development-only features (Scalar UI, OpenAPI, developer exception page, sensitive EF logging) are off outside `Development`. |
| A03 | Software Supply Chain Failures | Scan dependencies on every build, commit lock files, pin versions, generate SBOMs, verify provenance and signatures ([Section 9](#9-dependency-and-supply-chain-security)). Block deployments with known `HIGH`/`CRITICAL` CVEs. Harden the CI/CD pipeline (least-privilege tokens, SHA-pinned actions, protected branches). |
| A04 | Cryptographic Failures | Only the algorithms and libraries in [Section 4](#4-cryptography-standards). Never store sensitive data unencrypted. TLS 1.2+ for all transport (TLS 1.3 preferred), including connections to PostgreSQL, Valkey, and RabbitMQ. |
| A05 | Injection | Parameterized queries only (EF Core LINQ, `FromSql` interpolation, Npgsql parameters). Validate external input. Never build SQL, shell commands, or templates from untrusted input. Apply context-aware output encoding wherever untrusted text may be rendered. |
| A06 | Insecure Design | Threat-model (STRIDE) each major feature — especially checkout, payment, refunds, coupons, and account recovery. Document trust boundaries and abuse cases ([Section 2.3](#23-e-commerce-abuse-cases)) before implementation. |
| A07 | Authentication Failures | Apply [Sections 7](#7-jwt-authentication-best-practices) and [8](#8-oauth2-authentication-best-practices) and the password policy in [Section 4.2](#42-password-hashing--argon2id--pbkdf2--bcrypt). Enforce MFA for all staff/administrative accounts; offer MFA (prefer passkeys/WebAuthn) to customers. |
| A08 | Software or Data Integrity Failures | Verify signatures/checksums of third-party artifacts. Never use `BinaryFormatter` or other native/binary serializers on untrusted data; use `System.Text.Json` with explicit types. Verify payment-provider webhook signatures before processing. Authenticate and integrity-protect RabbitMQ messages that cross a trust boundary. |
| A09 | Security Logging and Alerting Failures | Log authentication events, authorization failures, payment anomalies, and anomalous requests; forward to a SIEM/telemetry backend; alert per [Section 11.4](#114-alerting). |
| A10 | Mishandling of Exceptional Conditions | Fail closed: any error in an authentication, authorization, validation, or payment path denies. Handle every error path explicitly; never swallow exceptions. Release resources and roll back transactions on failure. Return RFC 9457 Problem Details without stack traces, internal identifiers, or query text; log details server-side. |

### 2.2 OWASP API Security Top 10

| # | Risk | Mandatory Mitigation |
| --- | --- | --- |
| API1 | Broken Object Level Authorization | Verify object ownership on every endpoint that accepts an ID (orders, carts, addresses, payment methods, reviews) — in the Application layer, not just the route. Automated tests assert that customer A cannot read or modify customer B's resources. |
| API2 | Broken Authentication | Never accept tokens or API keys in query strings. Rotate credentials regularly. Rate-limit and lock out authentication endpoints. |
| API3 | Broken Object Property Level Authorization | Explicit response contracts (`API/Contracts`) — never serialize entities. Mass-assignment protection: request contracts contain only writable fields (no `role`, `isAdmin`, `price`, `status`). |
| API4 | Unrestricted Resource Consumption | Rate limiting (`Microsoft.AspNetCore.RateLimiting`), pagination limits, maximum payload sizes, query complexity limits, and timeouts on all endpoints. |
| API5 | Broken Function Level Authorization | Separate customer and back-office (admin/staff) routes and policies; authorize per verb. |
| API6 | Unrestricted Access to Sensitive Business Flows | Protect registration, login, checkout, coupon redemption, gift-card/voucher lookup, password reset, and stock-reserving actions with rate limits, velocity checks, bot mitigation (CAPTCHA/step-up where warranted), and idempotency keys ([Section 2.3](#23-e-commerce-abuse-cases)). |
| API7 | Server-Side Request Forgery | See A01. |
| API8 | Security Misconfiguration | No debug endpoints outside `Development`. Suppress detailed errors in production. |
| API9 | Improper Inventory Management | Maintain the API inventory (the versioned OpenAPI document). Retire deprecated versions promptly. |
| API10 | Unsafe Consumption of APIs | Validate data from payment, carrier, e-mail, and other third-party APIs before processing or storing it; set timeouts; treat webhooks as untrusted input. |

### 2.3 E-Commerce Abuse Cases

Every change touching these flows is reviewed against the abuse cases below; each has an automated test where feasible.

| Abuse case | Mandatory control |
| --- | --- |
| **Price or total tampering** | Totals, prices, discounts, taxes, and shipping are always computed server-side from authoritative data; client-supplied amounts are ignored or cross-checked. |
| **Inventory hoarding / denial of stock** | Cart and checkout reservations expire; reservations per customer/IP are rate-limited; quantity limits enforced in the domain. |
| **Coupon and promotion abuse** | Redemption limits enforced atomically in the database ([DATABASE.md Section 3.3](./DATABASE.md)); per-account and per-IP velocity checks; coupon codes are high-entropy and lookups are rate-limited. |
| **Double charge / replay** | Checkout and payment commands require an `Idempotency-Key`; payment webhooks are deduplicated by provider event ID. |
| **Payment-provider webhook forgery** | Verify the provider's signature (constant-time comparison) and timestamp tolerance before processing; allowlist source IPs where the provider publishes them. |
| **Account takeover and credential stuffing** | Breached-password screening, throttling, anomaly alerts, MFA for staff, secure recovery flow with short-lived single-use tokens, uniform responses that prevent account enumeration. |
| **Order/customer enumeration (IDOR)** | Non-guessable IDs (UUIDs), ownership checks (API1), uniform `404` for unauthorized access to another customer's resource. |
| **Review fraud and stored XSS** | Only customers with a delivered order may review (business rule); review text is length-limited, stored as plain text, and output-encoded by every consumer; HTML is rejected or sanitized ([Section 14.1](#141-authentication-authorization-and-web-protections)). |
| **Refund and staff abuse** | Separation of duties for high-value refunds ([BUSINESS.md Section 8.1](./BUSINESS.md)); audit log for every refund and price change. |
| **Card testing** | Rate limit and monitor payment attempts per account, IP, and device; rely on the provider's fraud tooling; alert on abnormal failure ratios. |

</OWASPCompliance>

---

<MITRECompliance>
## 3. MITRE Compliance

The project is evaluated against the **MITRE ATT&CK** framework (Enterprise, Containers/Cloud matrices as relevant) and the **MITRE CWE** catalog.

### ATT&CK — Priority Tactics to Defend Against

| Tactic | Key Mitigations |
| --- | --- |
| **Initial Access** (TA0001) | Harden authentication (MFA, breached-password screening). Minimize exposed surface. WAF/rate limits at the edge. |
| **Execution** (TA0002) | Shell-less, read-only containers; no dynamic code evaluation; allowlisted processes. |
| **Persistence** (TA0003) | Monitor unexpected changes to configurations, scheduled jobs, and service registrations; immutable images. |
| **Privilege Escalation** (TA0004) | Least privilege; audit and alert on privilege or role changes. |
| **Defense Evasion** (TA0005) | Immutable logs; monitor for log tampering or service disruption. |
| **Credential Access** (TA0006) | No plaintext credentials; rate limiting and lockout on authentication endpoints; secrets only from a secrets manager. |
| **Discovery** (TA0007) | No internal infrastructure details in errors, headers, or API responses. |
| **Lateral Movement** (TA0008) | Kubernetes network policies default-deny; per-service credentials for PostgreSQL, Valkey, and RabbitMQ. |
| **Collection** (TA0009) | Encrypt sensitive data at rest per [Section 4](#4-cryptography-standards). |
| **Exfiltration** (TA0010) | Egress filtering; alert on anomalous outbound volume and on bulk reads of customer data. |
| **Command and Control** (TA0011) | Allowlisted egress destinations; monitor beaconing and DNS tunneling. |
| **Impact** (TA0040) | Tested, immutable backups with offline copies; integrity monitoring on financial data. |

### CWE — Mandatory Checks

The following CWEs are explicitly addressed in code review and static analysis:

- **CWE-20** Improper Input Validation
- **CWE-79** Cross-site Scripting (XSS)
- **CWE-89** SQL Injection
- **CWE-200** Exposure of Sensitive Information
- **CWE-209** Error Message Containing Sensitive Information
- **CWE-256** Plaintext Storage of a Password
- **CWE-284** Improper Access Control
- **CWE-295** Improper Certificate Validation
- **CWE-306** Missing Authentication for Critical Function
- **CWE-326** Inadequate Encryption Strength
- **CWE-327** Broken or Risky Cryptographic Algorithm
- **CWE-330** Insufficiently Random Values
- **CWE-352** Cross-Site Request Forgery (CSRF)
- **CWE-434** Unrestricted Upload of File with Dangerous Type
- **CWE-502** Deserialization of Untrusted Data
- **CWE-611** XML External Entity (XXE)
- **CWE-639** Authorization Bypass Through User-Controlled Key (IDOR)
- **CWE-755** Improper Handling of Exceptional Conditions
- **CWE-798** Hard-coded Credentials
- **CWE-862** Missing Authorization
- **CWE-863** Incorrect Authorization
- **CWE-915** Improperly Controlled Modification of Dynamically-Determined Object Attributes (mass assignment)
- **CWE-918** Server-Side Request Forgery (SSRF)
- **CWE-1395** Dependency on Vulnerable Third-Party Component

Static analysis in CI reports findings mapped to these CWEs; any `HIGH` or `CRITICAL` finding blocks the pipeline.

</MITRECompliance>

---

<CryptographyStandards>
## 4. Cryptography Standards

> **Hard rule:** Never invent cryptographic schemes. Never use algorithms not listed in this section without written approval. Never implement cryptographic primitives from scratch. Any value used for a security decision — nonce, salt, token, session ID, password-reset code, coupon code, idempotency secret — is generated with a cryptographically secure random number generator (`RandomNumberGenerator`). `System.Random` and `Guid.NewGuid()` are forbidden for these purposes.

### 4.1 Symmetric Encryption — ChaCha20-Poly1305

**ChaCha20-Poly1305** is the mandatory algorithm for all symmetric encryption (data at rest and data in transit where the transport layer does not already provide it). It is an AEAD (confidentiality plus integrity in one operation), resistant to timing attacks without hardware acceleration, standardized in RFC 8439, and used in TLS 1.3.

**Implementation rules:**

- Never reuse a nonce for the same key. Generate nonces with a CSPRNG. Nonce length: **96 bits (12 bytes)**. Key length: **256 bits (32 bytes)**.
- Always verify the authentication tag before processing decrypted output; reject messages that fail verification.
- Store the nonce alongside the ciphertext; never store or transmit the key in plaintext.
- Derive encryption keys from a master secret with a KDF (e.g., HKDF-SHA3-512), never from raw passwords.
- Use envelope encryption for data at rest: a data encryption key (DEK) encrypts the data; a key encryption key (KEK) held in the secrets manager/KMS encrypts the DEK. Rotate KEKs at least annually and DEKs at least every 90 days (or per the classification schedule in [Section 5.4](#54-data-classification-and-encryption-at-rest)). Re-encrypting all data under a compromised key is mandatory and completes within the incident timelines in [Section 12](#12-incident-response).
- Use a keyed hash (HMAC-SHA3-512 "blind index") for exact-match lookup of encrypted fields (e.g., tax ID, e-mail); never a plain hash.

**Prohibited algorithms:**

| Algorithm | Reason |
| --- | --- |
| DES / 3DES | Deprecated, vulnerable to SWEET32 |
| RC4 | Broken |
| AES-CBC without MAC | Padding oracle attacks |
| AES-ECB | Deterministic, leaks patterns |
| Blowfish | Short block size |

### 4.2 Password Hashing — Argon2id / PBKDF2 / bcrypt

**Argon2id** is the **primary** algorithm for storing password verifiers. When Argon2id is unavailable on the platform, **PBKDF2** (RFC 8018) is the required secondary. **bcrypt** is permitted as a tertiary fallback only when neither is available.

> **ASP.NET Core Identity note:** The default `PasswordHasher<TUser>` uses PBKDF2 with a lower iteration count than this standard requires. Do not use it as-is: implement a custom `IPasswordHasher<TUser>` using Argon2id with the parameters below (and PHC-string storage), or configure the PBKDF2 fallback to the iteration minimum in this section.

#### Argon2id (Primary)

| Parameter | Required Value |
| --- | --- |
| Variant | **Argon2id** only. |
| Memory cost | Minimum **64 MiB** (65,536 KiB); increase as hardware allows |
| Iterations (t) | Minimum **3** |
| Parallelism (p) | **1** (or the number of available cores, up to 4) |
| Salt length | Minimum **128 bits (16 bytes)**, CSPRNG |
| Output length | Minimum **256 bits (32 bytes)** |
| Salt storage | Alongside the hash; never reused across users |

#### PBKDF2 (Secondary fallback)

| Parameter | Required Value |
| --- | --- |
| PRF | HMAC-SHA3-512. If the platform's PBKDF2 does not accept it, HMAC-SHA-512 is permitted under the interoperability exception in [Section 4.3](#43-general-hashing--sha3-512) with the same iteration minimum |
| Salt length | Minimum **128 bits (16 bytes)**, CSPRNG |
| Iteration count | Minimum **600,000** (OWASP 2023+ guidance, aligned with NIST SP 800-132); increase as hardware allows |
| Output length | **512 bits (64 bytes)** for key derivation; **256 bits (32 bytes)** minimum for verification |

#### bcrypt (Tertiary fallback)

| Parameter | Required Value |
| --- | --- |
| Cost factor | Minimum **12**; recalibrate upward regularly |
| Salt | Generated by the library; never supplied manually |
| Password length | bcrypt truncates at **72 bytes**; for longer passwords pre-hash with SHA3-512 and base64-encode before bcrypt |

**Additional rules (all algorithms):**

- Hash server-side only. Never compute or verify password hashes on the client.
- Store the algorithm identifier, version, and parameters alongside the hash to allow transparent upgrades on next login; upgrade PBKDF2/bcrypt hashes to Argon2id at the next successful authentication.
- Never use unsalted or fast hashes (MD5, SHA1, SHA2) for password storage.
- Hash computation is CPU- and memory-intensive: bound concurrent hashing operations (`SemaphoreSlim`) and rate-limit login endpoints so they cannot be used for denial of service.

**User-facing password policy (NIST SP 800-63B):**

- Minimum length **12 characters**; maximum no lower than **64**.
- No composition rules (forced symbol/digit mixes).
- Screen new and changed passwords against a corpus of known-breached passwords (e.g., an offline Have I Been Pwned range copy) and reject matches.
- No periodic forced rotation absent evidence of compromise.
- Throttle authentication with exponential backoff or temporary lockout after repeated failures, aligned with [Section 11.4](#114-alerting).

### 4.3 General Hashing — SHA3-512

**SHA3-512** is the mandatory algorithm for general-purpose hashing (integrity verification, fingerprinting, HMAC, etc.).

- Use SHA3-512 for checksums and any value requiring integrity guarantees; use HMAC-SHA3-512 for keyed hashing (signing tokens, verifying message integrity with a shared secret).
- Never use SHA1 or MD5 in new code; migrate legacy uses.
- For digital signatures prefer **Ed25519**. Where ECDSA is required, use P-384 minimum with SHA3-512 when supported, otherwise SHA-384. RSA signatures use RSA-PSS with at least 3072-bit keys.
- Use a constant-time comparison (`CryptographicOperations.FixedTimeEquals`) for any comparison of MACs, tokens, or hashes — never `==` or `SequenceEqual`.

**Prohibited hash algorithms:**

| Algorithm | Reason |
| --- | --- |
| MD5 | Broken |
| SHA1 | Collision attacks (SHAttered) |
| SHA-2 family | Permitted only for interoperability with external systems or standards that mandate it (TLS cipher suites, JOSE algorithms such as `ES256`, payment-provider webhook signatures such as HMAC-SHA256, Git/OCI digests, cloud KMS APIs); not for new internal designs. This is the **interoperability exception** referenced throughout this document. |

### 4.4 Approved Cryptographic Libraries

Cryptographic primitives come from the libraries below. Any other library requires written approval. Platform implementations are preferred; where a platform implementation reports itself unsupported at runtime (`.IsSupported == false`), fall back to the listed third-party library rather than a weaker algorithm.

| Requirement | .NET 10 |
| --- | --- |
| **ChaCha20-Poly1305 (AEAD)** | `System.Security.Cryptography.ChaCha20Poly1305` (check `IsSupported`); fallback `NSec.Cryptography` (libsodium) |
| **Argon2id** | `Konscious.Security.Cryptography.Argon2` or `NSec.Cryptography` |
| **SHA3-512 / HMAC-SHA3-512** | `SHA3_512`, `HMACSHA3_512` (check `IsSupported`; chiseled Ubuntu images with OpenSSL 3 support them); fallback `BouncyCastle.Cryptography` |
| **HKDF** | `System.Security.Cryptography.HKDF` |
| **Signatures (Ed25519 / ECDSA)** | `NSec.Cryptography` (Ed25519); `ECDsa` (P-384) |
| **CSPRNG** | `RandomNumberGenerator` |
| **Constant-time compare** | `CryptographicOperations.FixedTimeEquals` |
| **Secret memory hygiene** | `CryptographicOperations.ZeroMemory`; use `byte[]`/`Span<byte>` rather than `string` for key material |
| **Key management / Data Protection** | ASP.NET Core Data Protection keys are persisted to a shared store and protected by a KEK in the secrets manager (never to the container filesystem); application-level envelope encryption via an `IFieldEncryptor` abstraction in Infrastructure |

- Use the high-level password-hashing APIs (PHC string format) where the library offers them.
- Never wrap these libraries in a home-grown "crypto helper" that changes nonce, salt, or tag handling. A thin adapter exposing only approved operations is permitted and recommended.

</CryptographyStandards>

---

<SecretsAndSensitiveDataManagement>
## 5. Secrets and Sensitive Data Management

### 5.1 What Counts as Sensitive Data

- Connection strings and credentials for PostgreSQL, Valkey, and RabbitMQ
- API keys, webhook signing secrets, and tokens (payment, carrier, e-mail/SMS providers)
- Encryption keys, JWT signing keys, and Data Protection keys
- Passwords and password hashes
- Private keys (TLS, SSH, signing)
- PII (names, e-mail addresses, phone numbers, postal addresses, tax IDs such as CPF/CNPJ, IP addresses)
- Payment data (provider tokens and references; card numbers must never be stored — [DATABASE.md Section 9](./DATABASE.md))

### 5.2 Prohibited Practices

The following are **strictly forbidden**:

- Committing secrets to any Git repository, including private repositories.
- Storing secrets in `.env` files tracked by Git, or in `appsettings*.json` (including `configuration/appsettings.json`).
- Logging secrets at any level.
- Including secrets in error messages, stack traces, or API responses.
- Hardcoding secrets in source code, configuration files, Dockerfiles, Compose files, or build scripts.
- Transmitting secrets in URLs or in HTTP headers that may appear in logs.

### 5.3 Required Practices

- **Use a secrets manager** (HashiCorp Vault or the chosen cloud provider's secret service) for all secrets in staging and production. In local development use `dotnet user-secrets` or an untracked `.env` file.
- **Inject at runtime** as mounted files (preferred, read through `AddKeyPerFile`/`*_FILE` conventions) or environment variables at process start — never baked into images or artifacts. Environment variables follow the `ConnectionStrings__Postgres` convention documented in the README.
- **`.gitignore`:** `.env`, `secrets/`, `*.pem`, `*.key`, `*.pfx`, `*.p12`, `appsettings.*.local.json`, and `*.user` are ignored.
- **Pre-commit hooks:** `gitleaks` (or equivalent) runs as a pre-commit hook and in CI; enable the hosting platform's secret scanning with push protection.
- **Secret rotation:** a rotation schedule for all long-lived credentials; the application supports hot rotation without downtime (reload on change for file-mounted secrets; dual-key acceptance windows for JWT signing keys and webhook secrets).
- **Separate credentials per service and environment:** the API, worker, and migration job each have their own PostgreSQL role, RabbitMQ user, and Valkey ACL user.
- **Minimum exposure window:** short-lived tokens with the shortest practical lifetime ([Section 7](#7-jwt-authentication-best-practices)).

### 5.4 Data Classification and Encryption at Rest

| Classification | Examples | Requirement |
| --- | --- | --- |
| **Critical** | Encryption keys, private keys, password hashes, webhook secrets | Stored only in a secrets manager/KMS/HSM. Never in application tables (password hashes excepted, stored only in the Identity schema under restricted roles). |
| **Confidential** | PII (tax ID, phone, address, e-mail), payment references, API keys | Encrypted at rest with ChaCha20-Poly1305 at the application level for the highest-risk fields (e.g., tax ID), plus storage-level encryption for the whole database. Access logged and audited. |
| **Internal** | Orders, inventory, internal configuration | Storage-level encryption. Access controlled by role. |
| **Public** | Published catalog content | No encryption requirement; integrity verifiable. |

</SecretsAndSensitiveDataManagement>

---

<HTTPAndEdgeHardening>
## 6. HTTP and Edge Hardening

The application is a JSON API. Kestrel always runs behind a reverse proxy or ingress controller that terminates TLS ([CONTAINERS.md Section 3.4](./CONTAINERS.md)).

### 6.1 Universal Requirements

| Requirement | Details |
| --- | --- |
| **TLS only** | TLS 1.2 or 1.3 for all external traffic; HTTP redirects to HTTPS with a `301`. Connections to PostgreSQL, Valkey, and RabbitMQ also use TLS outside local development. |
| **HSTS** | `Strict-Transport-Security: max-age=31536000; includeSubDomains; preload` on all HTTPS responses outside `Development` (`app.UseHsts()` and/or the proxy). |
| **API response headers** | `X-Content-Type-Options: nosniff`; `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'`; `Referrer-Policy: no-referrer`; `Cross-Origin-Resource-Policy: same-origin`; `Cache-Control: no-store` on authenticated and personal-data responses; `Permissions-Policy` denying all features. |
| **No sensitive headers** | Remove `Server`, `X-Powered-By`, and equivalent headers before the response reaches the client (`AddServerHeader = false`). |
| **CORS** | Explicit allowlist of trusted origins — see [Section 6.4](#64-secure-and-efficient-cors-policy). |
| **Cookie flags** | Any session or authentication cookie uses `HttpOnly`, `Secure`, and `SameSite=Strict` (or `Lax` with documented approval) and the `__Host-` prefix ([Section 7.7](#77-cookie-based-authentication-security)). |
| **HTTP methods** | Only the methods a route declares are allowed; `TRACE` and unknown methods are rejected. |
| **Request limits** | Maximum body size, header size, and request time set globally and per endpoint (larger only for upload endpoints). |
| **Rate limiting** | `Microsoft.AspNetCore.RateLimiting` per client (IP + identity) with stricter policies for authentication, registration, password reset, checkout, payment, and coupon endpoints; `429` with `Retry-After`. Distributed counters live in Valkey when more than one replica runs; behind a proxy, the real client IP is taken only from trusted `X-Forwarded-For` hops. |

### 6.2 Kestrel and ASP.NET Core

```csharp
// Program.cs — hardened host configuration
builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.Limits.MaxRequestBodySize = 1 * 1024 * 1024;    // default 1 MB; raise per upload endpoint
    options.Limits.MaxRequestHeadersTotalSize = 32 * 1024;
    options.Limits.MaxRequestLineSize = 8 * 1024;
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(15);
    options.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(60);
    options.Limits.MinRequestBodyDataRate = new MinDataRate(240, TimeSpan.FromSeconds(5)); // slowloris defense
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Populate KnownProxies/KnownNetworks from configuration; never clear them in production.
});

builder.Services.AddProblemDetails();          // RFC 9457 error responses
builder.Services.AddRateLimiter(/* named policies per endpoint group */);

var app = builder.Build();

app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler();
    app.UseHsts();
}
app.UseHttpsRedirection();   // only where the app terminates/observes HTTPS; otherwise the proxy redirects
app.UseSecurityHeaders();    // NetEscapades.AspNetCore.SecurityHeaders — the header set in Section 6.1
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
```

**Additional requirements:**

- `FallbackPolicy` requires authenticated users; anonymous routes are explicit.
- Configure `JsonSerializerOptions`/model binding to reject unknown members on security-sensitive requests (mass-assignment defense), and cap collection sizes and nesting depth.
- Use `[RequestSizeLimit]`/`[RequestFormLimits]` only on endpoints that need more than the default.
- Never enable `DeveloperExceptionPage`, detailed errors, or `EnableSensitiveDataLogging` outside `Development`.
- Validate `Host` (`AllowedHosts` set to the real hostnames, not `*`, outside `Development`; note that `configuration/appsettings.json` currently ships `*`).
- Health endpoints (`/health/*`) and metrics are not routable from the public internet.

### 6.3 Reverse Proxy and Ingress

| Requirement | Details |
| --- | --- |
| **TLS** | TLS 1.2/1.3 only, modern ciphers, automated certificates (cert-manager or the provider). |
| **Headers** | The proxy adds or passes the header set from [Section 6.1](#61-universal-requirements) and overwrites (never appends) `X-Forwarded-*` from untrusted clients. |
| **Limits** | Body-size, header-size, timeout, and connection limits configured explicitly; WAF/rate limiting for public routes. |
| **Routing** | Only `/api/v1/*` (and explicitly approved routes) are exposed; `/health/*`, `/metrics`, the OpenAPI document, and Scalar UI are not routed in staging/production. |
| **Logging** | Access logs are forwarded to the central logging backend (client IP, route, status, latency; no bodies, tokens, or query-string secrets). |

### 6.4 Secure and Efficient CORS Policy

The API is called from browsers (storefront, back-office). CORS is configured with named ASP.NET Core policies:

- Explicit allowlist of trusted origins from configuration. Never `*` for authenticated endpoints; never reflect the incoming `Origin` without allowlist validation; reject `null` and malformed origins.
- Allow only required methods and headers; deny by default. Set `Vary: Origin` when behavior depends on the origin.
- `Access-Control-Allow-Credentials: true` only with a single validated origin and only with cookie authentication plus CSRF protection.
- Preflight (`OPTIONS`) only for routes that need CORS; `Access-Control-Max-Age` of 300–600 seconds; no sensitive data in preflight responses.

```http
Access-Control-Allow-Origin: https://shop.example.com
Access-Control-Allow-Methods: GET, POST, PUT, PATCH, DELETE
Access-Control-Allow-Headers: Authorization, Content-Type, Idempotency-Key, X-Request-ID
Access-Control-Expose-Headers: X-Request-ID
Access-Control-Max-Age: 600
Vary: Origin
```

### 6.5 Secure File Upload Policy

Applies to endpoints that accept files (product images, review photos, documents).

- **Validation:** allowlist by MIME type and extension; verify file signatures (magic bytes); enforce per-file and per-request size limits; generate server-side random file names (never trust client names/paths); reject archives unless explicitly required.
- **Storage:** store in private object storage (or a non-web-root, non-executable location), never on the container filesystem; serve through authorization checks or short-lived signed URLs; encrypt sensitive uploads at rest.
- **Scanning:** scan with antimalware before marking files available; quarantine suspicious files and alert.
- **Processing:** process images asynchronously in isolated workers with strict timeouts and memory limits; re-encode images to strip metadata and neutralize polyglots.
- **Download:** `Content-Disposition: attachment` (or strict `Content-Type` for images) and `X-Content-Type-Options: nosniff`; never render untrusted HTML/SVG inline.

### 6.6 API Documentation Exposure

- The OpenAPI document (`/api/v1/openapi/v1.json`) and Scalar UI (`/api/v1/docs`) are mapped only when `app.Environment.IsDevelopment()` (as in `src/Program.cs`). Exposing them in staging/production requires an explicit decision, authentication, and an exception record.
- The published OpenAPI contract never contains secrets, internal hostnames, or example real data.

</HTTPAndEdgeHardening>

---

<JWTAuthenticationBestPractices>
## 7. JWT Authentication Best Practices

> **Applicability:** The API authenticates callers with JWT bearer access tokens. If the Identity context issues tokens itself, or delegates to an external identity provider, that choice is recorded in an ADR; the rules below apply to issuing and validating either way.
>
> **Normative reference:** RFC 8725 — JSON Web Token Best Current Practices.

### 7.1 Algorithm Selection

- Sign with **EdDSA** (Ed25519, preferred), **ES384**, or **ES256**. **PS256/PS384** with RSA keys of at least **3072 bits** is permitted for interoperability. **RS256** is permitted only to _verify_ tokens from an external identity provider that cannot issue anything else; never issue new tokens with it.
- JOSE algorithm identifiers are fixed by RFC 7518/8037; their use of SHA-2 falls under the interoperability exception in [Section 4.3](#43-general-hashing--sha3-512).
- **HMAC algorithms (HS256/384/512) are forbidden** wherever more than one party verifies tokens; they are permitted only for a single issuer that is also the single verifier.
- `alg: none` is **forbidden**; reject empty or missing `alg`.
- **Validate `alg` against an explicit server-side allowlist** (`TokenValidationParameters.ValidAlgorithms`), never trusting the token header.
- **Set and validate `typ`** (e.g., `at+JWT` for access tokens) to prevent cross-JWT confusion (`ValidTypes`).
- **Do not follow `x5u`, `jku`, or `jwk` headers** to fetch keys at runtime unless URLs are pre-configured and allowlisted.
- Use `kid` when multiple keys are in rotation; resolve the key by `kid` lookup, never by trying every key.
- Minimum key sizes: RSA ≥ 3072 bits; ECDSA ≥ P-256; EdDSA Ed25519 or Ed448.

```csharp
options.TokenValidationParameters = new TokenValidationParameters
{
    ValidateIssuer = true,         ValidIssuer = issuer,
    ValidateAudience = true,       ValidAudience = audience,
    ValidateLifetime = true,       RequireExpirationTime = true,
    RequireSignedTokens = true,    ValidateIssuerSigningKey = true,
    ValidAlgorithms = ["EdDSA"],   // explicit allowlist
    ValidTypes = ["at+jwt"],
    ClockSkew = TimeSpan.FromSeconds(30),
};
options.MapInboundClaims = false;  // keep raw claim names
```

### 7.2 Token Lifetime

| Token Type | Maximum Lifetime |
| --- | --- |
| Access Token | 15 minutes |
| Refresh Token | 7 days (absolute expiry of 30 days) |
| ID Token | 1 hour |
| One-Time Token (password reset, e-mail verification) | 15 minutes, single use |

- Access tokens are short-lived; refresh tokens are **rotated** on every use.
- Implement **refresh token families**: if a previously used refresh token is presented, revoke the entire family immediately.
- Refresh tokens and one-time tokens are opaque, high-entropy random values stored only as hashes (HMAC-SHA3-512) in PostgreSQL.

### 7.3 Claims Validation

The API validates **all** of the following on every request (RFC 8725 §3.4, §3.5):

- `typ` matches the expected type.
- `iss` matches the expected issuer exactly (string comparison; no prefix/regex).
- `aud` includes this API's identifier; reject otherwise.
- `exp` is in the future; `nbf` (when present) is in the past; `iat` is not in the future beyond a **30-second** skew.
- `sub` corresponds to an existing, active principal; `iss` + `sub` uniquely identifies the principal.
- `jti` (when present) is checked against a short-term cache (Valkey) to prevent replay within the validity window.
- Roles and permissions are taken from the validated token and re-checked against server-side state for sensitive operations (refunds, price changes, role changes).

### 7.4 Storage and Transmission

- Browser applications prefer the **Backend-for-Frontend (BFF)** pattern: tokens stay server-side and the browser receives only an `HttpOnly`, `Secure`, `SameSite` session cookie ([Section 7.7](#77-cookie-based-authentication-security)).
- When a BFF is not feasible, clients keep access tokens in memory only (not `localStorage`/`sessionStorage`) and refresh tokens in `HttpOnly` cookies.
- **Never transmit JWTs in URLs.** Use the `Authorization: Bearer <token>` header.
- Cookie-borne tokens use the `__Host-` prefix.

### 7.5 Payload Confidentiality

- Do not put PII, credentials, or internal identifiers in JWT payloads — they are encoded, not encrypted.
- If confidentiality is required, use nested JWE (sign, then encrypt).

### 7.6 Revocation and Key Rotation

- Support immediate revocation: a blocklist keyed by `jti` in Valkey (TTL equal to the remaining lifetime) or per-user token-version checks.
- On password change, suspected compromise, or logout, invalidate all tokens of the affected `sub`.
- Rotate signing keys on a schedule (recommended every 90 days). Publish public keys via a JWKS endpoint over HTTPS and keep retired public keys for the maximum access-token lifetime after rotation.

### 7.7 Cookie-Based Authentication Security

> **Applicability:** Mandatory whenever authentication or session state is stored in browser cookies (for example a BFF).

- **Attributes:** `HttpOnly`, `Secure`, `SameSite=Strict` (`Lax` only with documented approval, e.g., federated callbacks); `__Host-` prefix (no `Domain`, `Path=/`); explicit short `Max-Age`/`Expires`.
- **Session lifecycle:** regenerate identifiers after login and privilege change; invalidate on logout, password change, or compromise; enforce idle and absolute timeouts; limit concurrent sessions per risk profile.
- **CSRF:** `SameSite` alone is insufficient for high-risk operations. Require anti-forgery tokens (ASP.NET Core Antiforgery) on state-changing cookie-authenticated requests and validate `Origin`/`Sec-Fetch-Site`.
- **Transport and exposure:** never over HTTP; no tokens or session IDs in URLs or `localStorage`; minimal `Path` scope; store opaque session identifiers, not user data.

</JWTAuthenticationBestPractices>

---

<OAuth2AuthenticationBestPractices>
## 8. OAuth2 Authentication Best Practices

> **Applicability:** Applies when the project uses OAuth2/OpenID Connect (for example, for storefront sign-in, social login, or staff SSO). Normative reference: **RFC 9700 — OAuth 2.0 Security Best Current Practice**. Build versus buy (OpenIddict, Keycloak, or a managed IdP) is decided by ADR; building an authorization server from scratch is forbidden.

| Topic | Mandatory Behavior |
| --- | --- |
| **Flow selection** | SPA, mobile, and server-rendered clients: **Authorization Code + PKCE**. Machine-to-machine: **Client Credentials**. The **Implicit** flow is forbidden. **ROPC** is forbidden unless no alternative exists and an exception is approved. |
| **PKCE** | Mandatory for **all** Authorization Code flows, including confidential clients. `code_challenge_method=S256` only; `plain` is forbidden. `code_verifier`: 43–128 characters from a CSPRNG. |
| **State and nonce** | `state` is required on every authorization request and compared in constant time; `nonce` is required for OpenID Connect and validated in the ID token. Keep them in a server-side session or signed `HttpOnly` cookie. |
| **Redirect URIs** | Register exact redirect URIs; no wildcards; exact string matching. No open redirectors anywhere in the flow. |
| **Authorization codes** | Expire within 60 seconds, single use; replay revokes issued tokens. |
| **Mix-up defense** | When more than one authorization server is configured, compare the `iss` response parameter with the expected issuer. Use PAR (RFC 9126) and, for high-assurance flows, JAR (RFC 9101) where supported. |
| **Tokens** | All rules in [Section 7](#7-jwt-authentication-best-practices) apply to OAuth2-issued tokens. Access tokens are audience-restricted; resource servers reject tokens not addressed to them. Use DPoP (RFC 9449) for sender-constrained tokens in high-security contexts. Public-client refresh tokens are sender-constrained or one-time use with rotation. |
| **Client secrets** | Confidential-client secrets live in the secrets manager ([Section 5](#5-secrets-and-sensitive-data-management)); prefer `private_key_jwt` over shared secrets; rotate on schedule or after exposure. |
| **Scopes** | Fine-grained, resource-specific scopes; request only what an operation needs; review and prune unused scopes regularly; meaningful consent screens. |
| **Authorization server** | TLS 1.2+ (1.3 preferred) on every endpoint; discovery metadata over HTTPS with `issuer`; rate limiting and brute-force protection on token, introspection, and revocation endpoints; token revocation (RFC 7009) effective immediately; all events logged to the SIEM. |

</OAuth2AuthenticationBestPractices>

---

<DependencyAndSupplyChainSecurity>
## 9. Dependency and Supply Chain Security

- **Dependency scanning** runs on every CI execution: NuGet Audit (`NuGetAudit` enabled with `NuGetAuditMode=all`, `NuGetAuditLevel=low`, warnings as errors for `HIGH`/`CRITICAL`), `dotnet list package --vulnerable --include-transitive`, plus a cross-ecosystem scanner (OSV-Scanner or Trivy).
- Any dependency with **CVSS ≥ 7.0** (`HIGH`/`CRITICAL`) is resolved (upgrade, replace, or documented risk acceptance) before the pipeline passes. This is the single threshold referenced by [CODE.md](./CODE.md), [TESTS.md](./TESTS.md), and [CONTAINERS.md](./CONTAINERS.md).
- **Lock files** are committed: `packages.lock.json` (`RestorePackagesWithLockFile=true`), with CI restoring in locked mode (`--locked-mode` / `RestoreLockedMode=true`). Package versions are centralized in `Directory.Packages.props`.
- **Dependency-confusion defense:** `nuget.config` uses `clear` plus explicit sources and **package source mapping**; only `nuget.org` (or the private feed) is allowed, and internal package prefixes map to the private feed only.
- New dependencies need a justification in the pull request (maintenance status, license, transitive footprint). Unmaintained packages (no release or security response in 18 months) are not introduced.
- License changes on upgrade are supply-chain events and require approval (see the commercially licensed packages in [CODE.md Section 4.3](./CODE.md)).
- **CI/CD hardening:** pin GitHub Actions and reusable workflows to full commit SHAs; use least-privilege `GITHUB_TOKEN` permissions (`permissions:` set per workflow/job); protect the default branch; require reviews and passing checks; no long-lived cloud credentials in CI (OIDC federation).
- **Pin Docker base images by digest**; verify downloaded artifacts (checksums/signatures) in CI.
- Subscribe to security advisories (GitHub Dependabot alerts) for all major dependencies; Renovate/Dependabot keep packages and images current.
- Generate an **SBOM** (CycloneDX or SPDX) for every release build and store it with the release artifacts; produce **SLSA** build provenance attestations and sign images ([CONTAINERS.md Section 2.6](./CONTAINERS.md)).

</DependencyAndSupplyChainSecurity>

---

<ContainerSecurity>
## 10. Container Security

Workloads running in Docker or Kubernetes comply with this section and, for detailed configuration, [CONTAINERS.md](./CONTAINERS.md).

### 10.1 Docker

- **Minimal base image:** the chiseled `aspnet` image, pinned to a digest; no shell, package manager, or build tools in the final image. Multi-stage builds only.
- **No secrets in images** — no `ENV`, `ARG`, `COPY`, or `ADD` of credentials; use runtime injection and BuildKit secret mounts.
- **Scan and sign:** scan every image in CI (Trivy, Grype, Docker Scout, or Snyk) and block on `HIGH`/`CRITICAL`; sign with Cosign or Notation; Docker Content Trust is retired.
- **Runtime hardening (Docker and Compose):** non-root user (numeric UID), read-only root filesystem with `tmpfs` for `/tmp`, `cap_drop: ALL`, `no-new-privileges`, default (or stricter) seccomp profile, CPU/memory/PID limits.
- **Never expose the Docker socket** to a container.
- **Private, authenticated registry** for production images; public images mirrored and scanned first.
- **`HEALTHCHECK`** only where Docker/Compose run the image (Kubernetes uses probes), in exec form with a binary shipped in the image.
- **Strict `.dockerignore`**; no `.git`, `.env`, `bin/`, `obj/`, or secrets in the build context.

### 10.2 Kubernetes

Applies when the application is deployed to Kubernetes (`kubernetes/`). Managed Kubernetes control planes are preferred; control-plane items are the provider's responsibility where managed.

#### Cluster Hardening

- Keep cluster components patched: `CRITICAL` CVEs within 72 hours, `MEDIUM`+ within 30 days.
- Enable API-server audit logging, forward it to the SIEM, and retain it for at least 12 months.
- The API server is not publicly accessible; authenticate with OIDC, authorize with RBAC; anonymous authentication is disabled.
- Encrypt Kubernetes Secrets at rest with a KMS provider.

#### Workload Security

- Enforce **Pod Security Admission** at the `restricted` level on every application namespace.
- Every Pod: `runAsNonRoot: true`, `seccompProfile: RuntimeDefault`, `allowPrivilegeEscalation: false`, `readOnlyRootFilesystem: true`, `capabilities.drop: ["ALL"]`, resource requests and limits ([CONTAINERS.md Section 6.1](./CONTAINERS.md)).
- No `hostPID`, `hostIPC`, `hostNetwork`, or `hostPath` in production.

#### RBAC and Least Privilege

- Least-privilege RBAC; never bind `cluster-admin` or wildcard permissions to workloads.
- A dedicated ServiceAccount per workload with `automountServiceAccountToken: false` (the application never calls the Kubernetes API).
- Audit RBAC at least quarterly.

#### Network Policies

- **Default deny** ingress and egress in every production namespace; explicitly allow only required flows ([CONTAINERS.md Section 7.3](./CONTAINERS.md)).
- Block egress to the cloud metadata endpoint (`169.254.169.254`) from application workloads to prevent SSRF-based credential theft.
- A service mesh with mTLS is optional for this topology; if not adopted, use TLS to backing services and network policies for segmentation.

#### Secrets Management

- Never commit Kubernetes Secrets as base64 YAML. Use External Secrets Operator, Sealed Secrets, or Vault Agent Injector.
- Mount secrets as files on `tmpfs` rather than environment variables where possible.
- Rotate Secrets on the schedule in [Section 5](#5-secrets-and-sensitive-data-management).

#### Supply Chain and Runtime Detection

- Enforce image signature verification and registry allowlisting at admission (Kyverno, OPA/Gatekeeper, or Sigstore Policy Controller).
- Run a continuous in-cluster vulnerability scanner (Trivy Operator) and, for production, a runtime threat-detection agent (Falco) with alerts forwarded to the SIEM.

</ContainerSecurity>

---

<LoggingAndMonitoring>
## 11. Logging and Monitoring

### 11.1 What to Log

| Category | Events |
| --- | --- |
| Authentication | Successful and failed logins, MFA events, password changes and resets, account lockouts, token refresh reuse detection |
| Authorization | Access denied events, ownership-check failures (possible IDOR probing), privilege changes |
| Payments and orders | Payment authorization/capture/refund outcomes, webhook signature failures, idempotency-key conflicts, refund approvals |
| Data Access | Access to confidential data classifications; bulk reads of customer data by staff |
| Configuration | Changes to security configuration, role assignments, feature flags |
| Anomalies | Unusual request rates, velocity-check hits (coupons, card testing), unexpected parameter values |
| System | Service starts/stops, crashes, unhandled exceptions, dead-lettered messages |

### 11.2 What Never to Log

- Passwords (plaintext or hashed)
- Encryption keys, secrets, webhook signing secrets
- Full JWTs, refresh tokens, session tokens, or `Authorization` headers
- Card numbers, CVV, bank account numbers
- Full national identification numbers (CPF/CNPJ, SSN) and other PII beyond what is strictly necessary for audit
- Request or response bodies of authentication, payment, or profile endpoints
- Connection strings and SQL parameter values (outside local development)

### 11.3 Log Format and Integrity

- Structured logging (JSON) to enable reliable parsing and querying.
- Every entry includes: timestamp (ISO 8601 UTC), severity, `trace_id`/`span_id` (correlation), service name, event type, and actor identity (user ID or service identity — never a name or e-mail in production).
- Logs are forwarded in real time to a centralized, tamper-resistant backend and stored append-only; modification or deletion is restricted and alerted on.
- Security and audit logs are retained for at least **12 months** (or as required by regulation); telemetry design is in [OBSERVABILITY.md](./OBSERVABILITY.md).

### 11.4 Alerting

- Alert thresholds for: repeated authentication failures (≥ 5 in 5 minutes per account), card-testing patterns, refresh-token reuse, webhook signature failures, mass data access, configuration changes outside maintenance windows, and any critical-severity entry.
- Alert response SLAs are defined and exercised via tabletop or simulation at least once per quarter.

### 11.5 Logging Subsystem and Output Channels

#### 11.5.1 Architecture Requirements

- Application code logs through `ILogger<T>` with source-generated `LoggerMessage` templates (never string interpolation, never direct `Console.WriteLine`).
- Security-relevant and business-audit events go through a dedicated abstraction (`ISecurityAuditLog` / `IAuditLog`, implemented in Infrastructure) with typed event records (`LoginFailed`, `RefundApproved`, …) that enforce required fields; audit events are also persisted in an append-only PostgreSQL table where regulation or the business requires it.
- Redaction is built in: `Microsoft.Extensions.Compliance.Redaction` with data-classification attributes on logged properties, and a denylist of field names (`password`, `token`, `authorization`, `cardNumber`, …) applied before emission.

#### 11.5.2 Mandatory Output Channels

- **Telemetry (mandatory outside local development):** logs exported through OpenTelemetry (OTLP) to the Collector. This is the single shipping path to the operational and security backends ([OBSERVABILITY.md Section 4.3](./OBSERVABILITY.md#43-log-shipping-path)).
- **Console/stdout (mandatory):** JSON logs to stdout for runtime diagnostics, `kubectl logs`/`docker logs`, and local runs. No log agent tails the application's stdout, so records are not ingested twice.
- **File output (conditional):** only for non-containerized hosts that require it (path, rotation, retention, and size from configuration). For containerized workloads the file channel is **disabled**: the root filesystem is read-only ([CONTAINERS.md §3.5](./CONTAINERS.md#35-logging-and-observability)). Events that must never be lost (security and audit) are also written to the append-only PostgreSQL audit table in the same transaction as the action.

#### 11.5.3 OpenTelemetry Integration Rules

- Log records include `trace_id` and `span_id` when a span is active; severity mapping follows OpenTelemetry semantic conventions.
- Export is non-blocking and resilient (retry with backoff, bounded queue, failure-safe fallback).
- If the telemetry backend is unavailable, console output continues without interruption.

</LoggingAndMonitoring>

---

<IncidentResponse>
## 12. Incident Response

### 12.1 Classification

| Severity | Description | Response Time |
| --- | --- | --- |
| **P1 — Critical** | Active exploitation, confirmed data breach, payment-flow compromise, system-wide compromise | Immediate (< 15 min) |
| **P2 — High** | Suspected exploitation, significant vulnerability with active exposure | < 1 hour |
| **P3 — Medium** | Vulnerability with limited exploitation risk, no confirmed breach | < 24 hours |
| **P4 — Low** | Minor findings, informational | < 1 week |

### 12.2 Response Steps

1. **Detect** — automated alerts or manual discovery; record detection time and method.
2. **Contain** — isolate affected systems or accounts; revoke compromised credentials (rotate JWT signing keys, database and broker credentials, webhook secrets) immediately.
3. **Eradicate** — remove malicious artifacts, patch vulnerabilities, rotate affected secrets.
4. **Recover** — restore services from clean backups; verify integrity before returning to production.
5. **Post-mortem** — document timeline, root cause, impact, and corrective actions within 5 business days; share lessons learned.

### 12.3 Communication

- Do not disclose details of an active incident publicly or to unaffected parties until containment is confirmed.
- Notify affected users and, where required by law, the relevant data protection authority within the mandated timeframe — for example 72 hours under GDPR (Art. 33) and 3 business days under LGPD (ANPD Resolution CD/ANPD nº 15/2024). The privacy owner determines which regimes apply. Notify the payment provider/acquirer per its contract for incidents involving payment data.
- Maintain a confidential incident log accessible only to authorized responders.

</IncidentResponse>

---

<CloudProviderSecurity>
## 13. Cloud Provider Security (Conditional)

> **Applicability:** Mandatory when any cloud provider hosts the application or its backing services. Terraform templates exist for AWS, Azure, Google Cloud, and OCI ([IAC.md Section 5.1](./IAC.md#51-provider-templates)); the provider is chosen by ADR, and that ADR adds the provider-specific controls (organization guardrails such as SCPs/Azure Policy/Organization Policies, audit-log services, posture management, key management) to this baseline.

- Dedicated accounts/subscriptions/projects per environment (`development`, `staging`, `production`).
- SSO and MFA for all human users; no long-lived static credentials; short-lived workload identity (managed identities / service accounts / OIDC federation) instead of embedded keys.
- Public access denied by default for storage, databases, caches, brokers, and administrative endpoints.
- Encryption at rest (customer-managed keys for sensitive data) and in transit for all managed services.
- Centralized, retained audit logs (control plane and, where supported, data plane) for at least 12 months.
- No wildcard IAM permissions (`Action: *`, `Resource: *`) without an approved exception.
- Infrastructure-as-code policy checks in CI/CD block non-compliant deployments; continuous posture assessment alerts on drift; manual console changes in production are exceptional and audited.
- Break-glass access is time-bound, fully logged, and reviewed after use.

</CloudProviderSecurity>

---

<SecurityLibraries>
## 14. Recommended Security Libraries and Tooling

Cryptographic primitives are governed by [Section 4.4](#44-approved-cryptographic-libraries) and are not repeated here. A different library is allowed only when it is actively maintained, has a published security-response process, and the choice is recorded in an ADR.

### 14.1 Authentication, Authorization, and Web Protections

| Requirement | Default |
| --- | --- |
| **JWT validation (§7)** | `Microsoft.AspNetCore.Authentication.JwtBearer` + `Microsoft.IdentityModel.JsonWebTokens` |
| **OAuth2 / OIDC client (§8)** | `Microsoft.AspNetCore.Authentication.OpenIdConnect`; `Duende.AccessTokenManagement` (commercial license — approval required) |
| **Authorization server (§8)** | OpenIddict or Keycloak (prefer a dedicated IdP); Duende IdentityServer is commercial — approval required |
| **Policy-based authorization** | ASP.NET Core authorization policies, requirements, and resource-based handlers |
| **Security headers (§6.1)** | `NetEscapades.AspNetCore.SecurityHeaders` |
| **CORS (§6.4)** | ASP.NET Core CORS middleware with named policies |
| **CSRF (§7.7)** | ASP.NET Core Antiforgery |
| **Rate limiting (API4/API6)** | `Microsoft.AspNetCore.RateLimiting` (built-in); Valkey-backed counters for multi-replica limits |
| **Input validation** | FluentValidation; `System.ComponentModel.DataAnnotations` for trivial models |
| **HTML sanitization (XSS)** | `HtmlSanitizer` (Ganss.Xss) when HTML is accepted; default stance is plain-text only |
| **Safe XML parsing (XXE)** | `XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }` |
| **Safe deserialization (A08)** | `System.Text.Json` with explicit types (never `BinaryFormatter`) |
| **Secrets manager client (§5.3)** | The chosen provider's SDK (`Azure.Security.KeyVault.Secrets`, `AWSSDK.SecretsManager`, `Google.Cloud.SecretManager.V1`) or `VaultSharp`; or mounted files via `AddKeyPerFile` |
| **Log redaction (§11.5)** | `Microsoft.Extensions.Compliance.Redaction` |
| **Webhook signature verification** | `CryptographicOperations.FixedTimeEquals` over the provider's HMAC, with timestamp tolerance |

### 14.2 Security Scanning in CI

| Scan | Default |
| --- | --- |
| **SAST (language)** | .NET analyzers (`AnalysisLevel=latest-recommended`), SonarAnalyzer.CSharp, Security Code Scan rules |
| **SAST (cross-language)** | CodeQL (C#) or Semgrep |
| **Dependency audit** | NuGet Audit (`NuGetAuditMode=all`), `dotnet list package --vulnerable --include-transitive`, OSV-Scanner |
| **License / source policy** | `nuget-license` or ORT |
| **Secret scanning** | `gitleaks` (pre-commit and CI) plus the hosting platform's secret scanning with push protection |
| **Container and IaC scanning** | Trivy (image, filesystem, `trivy config`), hadolint, Checkov |
| **DAST** | OWASP ZAP (API scan against the OpenAPI document in staging) |
| **SBOM (§9)** | CycloneDX .NET tool or `syft` |

</SecurityLibraries>

---

<DefinitionOfDone>
## 15. Security Definition of Done

A delivery is complete from a security perspective only when all items below are true:

1. Every OWASP Top 10:2025 and API Security Top 10 risk relevant to the change has a mitigation from [Section 2](#2-owasp-compliance); the change was threat-modeled if it introduces a new trust boundary, and e-commerce abuse cases ([Section 2.3](#23-e-commerce-abuse-cases)) were considered.
2. All cryptography uses the algorithms in [Section 4](#4-cryptography-standards) through the libraries in [Section 4.4](#44-approved-cryptographic-libraries); every security-relevant random value comes from a CSPRNG.
3. No secret, credential, key, or token exists in source code, configuration files, images, logs, or Git history; secrets are read from a secrets manager at runtime.
4. Authentication and session handling follow [Sections 7](#7-jwt-authentication-best-practices) and [8](#8-oauth2-authentication-best-practices); authorization (including object ownership) is enforced server-side and denies by default.
5. All input crossing a trust boundary is validated, and output rendered into SQL, shell, or other interpreters is parameterized or encoded.
6. Error paths fail closed and return generic Problem Details; full details are logged server-side without sensitive data.
7. SAST, dependency, secret, and container scans pass with no unresolved `HIGH`/`CRITICAL` findings; lock files are committed and CI restores in locked mode.
8. An SBOM and build provenance are produced for every release, and production images are signed and verified at admission.
9. Security-relevant events are logged per [Section 11](#11-logging-and-monitoring) with redaction applied, and alerts exist for the thresholds in [Section 11.4](#114-alerting).
10. Security controls introduced or changed are covered by automated tests per [TESTS.md Section 8](./TESTS.md).
11. Rate limits, idempotency keys, and webhook signature verification are in place for every new payment, checkout, coupon, or account flow.

Any exception to these standards must be documented with owner, scope, risk, rationale, and expiration date, and approved by the maintainers.

</DefinitionOfDone>

---

_Last updated: September 30, 2026_
_These standards must be reviewed and updated at minimum once per year or after any significant security incident._
