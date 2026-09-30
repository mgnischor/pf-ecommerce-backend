# Claude Agent Instructions

@../AGENTS.md

`AGENTS.md` (repository root) is the **canonical** source: project overview, the standards suite in `ai/`, the cross-cutting rules, the task-based index, and the done-checklist. This file adds only the Claude-specific working protocol. Do not copy rules from `AGENTS.md` into this file — update `AGENTS.md` and, if a rule is among the few summarized in `.github/copilot-instructions.md`, that file too.

The standards in `ai/` are non-negotiable. Do not generate code, architecture, or guidance that contradicts them.

---

## Project facts

- C# / .NET 10 modular monolith, single project `Portfolio.csproj` (root namespace `Portfolio`), source under `src/<BoundedContext>/{Domain,Application,Infrastructure,API}`.
- Configuration lives in `configuration/`, launch settings in `properties/`, Kubernetes manifests in `kubernetes/`, Compose files at the repository root.
- Build and run: `dotnet restore`, `dotnet build`, `dotnet run --project Portfolio.csproj`. Quality gates (analyzers, CSharpier, tests, architecture tests, coverage) are defined in `ai/CODE.md §2` and `ai/TESTS.md`.
- Tests are under `tests/` (`Portfolio.UnitTests`, `Portfolio.ArchitectureTests`, `Portfolio.IntegrationTests`) and run with `dotnet test --project tests/<name>` (xUnit v3 on Microsoft.Testing.Platform, enabled in `global.json`). `dotnet build -warnaserror` and `csharpier check .` must be clean; there is deliberately no solution file next to `Portfolio.csproj` (it would make bare `dotnet build` ambiguous).
- In development the API reference is at `/api/v1/docs` (Scalar) and `/api/v1/openapi/v1.json`; both are development-only.
- There is no front end in this repository.

---

## How to apply the standards

1. **Identify the domains** involved in the request using the task-based index in `AGENTS.md §3`.
2. **Read the relevant `ai/` document(s) before producing output.** When several domains apply (for example, an endpoint that writes to the database and calls a payment provider), apply all of them together.
3. **Comply.** If a request conflicts with a standard, follow the standard, explain the conflict, and ask for explicit justification before proceeding.

| Situation                         | Required action                                                                                                                                                          |
| --------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Request conflicts with a standard | Follow the standard; explain the conflict to the developer                                                                                                               |
| Request requires an exception     | Require all five fields every Definition of Done demands — owner, scope, risk, rationale, expiration date — before proceeding; a generic justification is not sufficient |
| Standard is ambiguous             | Apply the most conservative, secure interpretation                                                                                                                       |
| Multiple standards apply          | Apply all of them; flag any contradictions                                                                                                                               |

Prefer small, reversible, incremental changes over large rewrites.

---

## Task tracking protocol

`ai/TASKS.md` is the **single source of truth for task progress**. Read it at the start of every session.

| Event             | Required action                                    |
| ----------------- | -------------------------------------------------- |
| Starting a task   | Change `- [ ]` to `- [~]`                          |
| Completing a task | Change `- [~]` to `- [x]`                          |
| Blocked on a task | Leave unchecked; append `<!-- BLOCKED: reason -->` |

- Never skip a checkbox update; record every transition immediately.
- Do not alter task descriptions — only the checkbox marker.
- Only one task is `[~]` at a time unless the work is genuinely parallel.

---

## Prohibited actions

- Introducing security vulnerabilities, or disabling or bypassing security controls, validation, or authorization checks.
- Placing business logic outside the Domain layer, or domain code that depends on EF Core, ASP.NET Core, RabbitMQ, Valkey, or OpenTelemetry.
- Referencing another bounded context's internal types, or querying or joining across context schemas.
- Building SQL or shell commands from strings (including `FromSqlRaw` with concatenation).
- Hardcoding credentials, secrets, or environment-specific configuration, or committing them (including `appsettings*.json`, `.env`, `.tfvars`, and state files).
- Using `float`/`double` for money, reading `DateTime.UtcNow`/`DateTimeOffset.UtcNow` directly instead of `TimeProvider`, or exposing entities or persistence details in API contracts.
- Publishing to RabbitMQ inside the database transaction (use the outbox), or writing a retriable handler, consumer, or job that is not idempotent.
- Adding a dependency that is commercially licensed (MediatR 13+, AutoMapper 15+, MassTransit 9+, FluentAssertions 8+, Duende IdentityServer) or not drawn from `ai/CODE.md §5`, without approval.
- Writing non-deterministic or order-dependent tests, using the EF Core in-memory or SQLite providers for PostgreSQL behavior, or skipping traceability fields on new entities.
- Making manual infrastructure changes outside IaC, or using wildcard IAM permissions or unversioned module/image references.
