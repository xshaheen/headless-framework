---
date: 2026-05-16
topic: agent-ready-headless-template
focus: Clean Architecture, DDD, and AI-agent-friendly framework demonstration template
mode: repo-grounded
status: draft
---

# Ideation: Agent-Ready Headless Template

## Lean

Build the template as an agent-ready capability tour, not as another Clean Architecture skeleton.

The differentiator should be:

> A Headless Framework template that humans and AI agents can safely extend without destroying the architecture.

That means the template needs three connected surfaces:

1. `headless-shop` as the runnable reference app.
2. `headless-module` as the growth path.
3. Guardrails and generated validation as the contract that keeps both usable by agents.

## Why This Direction

Generic Clean Architecture templates are crowded. Headless should not compete on folder names. It should compete on a stronger promise: a generated .NET application where DDD, CQRS, multi-tenancy, messaging, OpenAPI, permissions, observability, and tests work together in a way that an AI agent can understand, modify, and verify.

For agents, the highest-value feature is not extra scaffolding. It is executable feedback:

- clear architecture rules
- small repeatable recipes
- one validation command
- architecture tests with actionable failures
- generated docs that map concepts to files

Without those, an agent will eventually copy a pattern from the wrong layer, bypass a module boundary, or wire messaging/tenancy incorrectly.

## Ranked Product Surfaces

### 1. Agent-Readable Guardrails

This is the first-class requirement.

Generated app should include:

- `AGENTS.md`
- `docs/architecture.md`
- `docs/recipes/add-module.md`
- `docs/recipes/add-command.md`
- `docs/recipes/add-query.md`
- `docs/recipes/add-integration-event.md`
- `docs/recipes/add-permission.md`
- `docs/recipes/add-tenant-aware-flow.md`
- `make verify` or an equivalent scriptable validation command

`AGENTS.md` should tell agents:

- the canonical module shape
- where business rules live
- how commands and queries are added
- how modules communicate
- which validation command to run
- which files are generated or template-owned
- what not to do, such as direct module references or raw broker clients

Architecture tests should fail with messages written for humans and agents:

- "Ordering must not reference Catalog internals. Move shared contracts to Shop.Contracts."
- "Endpoints should delegate to Mediator commands or queries. Move business rules to Application."
- "Messaging must go through Headless.Messaging abstractions. Do not use raw broker clients."
- "Tenant-aware writes must run under tenant context."

### 2. `headless-shop` Capability Tour

The flagship template should be a small modular shop, not a generic starter.

Suggested shape:

```text
Shop.Api
Shop.Contracts
Shop.Modules
Shop.Catalog.Domain
Shop.Catalog.Application
Shop.Catalog.Infrastructure
Shop.Catalog.Api
Shop.Catalog.Module
Shop.Ordering.Domain
Shop.Ordering.Application
Shop.Ordering.Infrastructure
Shop.Ordering.Api
Shop.Ordering.Module
Shop.Tests.Architecture
Shop.Tests.Integration
```

The first business flow should prove:

```text
CreateProduct
  -> Catalog aggregate enforces invariant
  -> domain event is raised
  -> integration event is published through Headless.Messaging
  -> Ordering projection is updated
  -> PlaceOrder succeeds against projected product data
```

Capabilities demonstrated by that flow:

- `Headless.Domain` aggregate roots, value objects, and events
- `Mediator.SourceGenerator` command/query handlers
- thin Minimal API endpoints
- `Headless.Orm.EntityFramework` persistence conventions
- cross-layer tenant posture through HTTP, Mediator, EF, and Messaging
- Headless Messaging outbox/consumer path
- OpenAPI surface through Scalar or NSwag
- permissions around one command
- testing through architecture and integration tests

### 3. `headless-module` Growth Template

Add this after the flagship app shape is proven.

The companion template should generate one bounded module into an existing generated app:

```bash
dotnet new headless-module -n Inventory
```

It should produce:

- `Inventory.Domain`
- `Inventory.Application`
- `Inventory.Infrastructure`
- `Inventory.Api`
- `Inventory.Module`
- optional `Inventory.Contracts`
- sample command/query
- module registration
- architecture test registration
- recipe entry or generated README section

This is agent-relevant because it prevents file-layout drift. Agents should not need to infer where a new module belongs from existing code every time. They should use a generator that writes the correct shape and then fill in domain behavior.

## Validation Contract

Template validation should be treated as product validation.

Minimum gate:

```text
pack template
install template into custom hive
generate app into temp directory
build generated app
run unit tests
run architecture tests
run integration smoke
```

Smoke path:

```text
create tenant
create product
verify OpenAPI is exposed
verify product is visible under tenant
verify Catalog publishes integration event
verify Ordering projection receives product
place order
verify tenant isolation blocks cross-tenant access
```

Agent-specific validation:

- every recipe command should be runnable from a clean generated checkout
- every architecture-test failure should explain the fix direction
- `AGENTS.md` should name the validation command
- generated docs should avoid stale package/API names

## Profile Strategy

Do not make the first version too configurable. Start opinionated.

Recommended sequence:

1. `tour` profile only.
2. Add `minimal` after the default shape stabilizes.
3. Add `production` after validation is strong enough.

Possible final shape:

```bash
dotnet new headless-shop -n TrailStore --profile tour
dotnet new headless-shop -n TrailStore --profile minimal
dotnet new headless-shop -n TrailStore --profile production
```

Profile intent:

- `minimal`: module boundaries, DDD primitives, CQRS, in-memory infrastructure
- `tour`: tenancy, messaging, permissions, OpenAPI, jobs, caching, architecture tests
- `production`: Testcontainers, external providers, observability, deployment-ready defaults

## What To Avoid

- Do not lead with `headless-clean-architecture` naming. It sounds generic.
- Do not make provider switches the main feature in version one.
- Do not create a ten-module stress app as the default.
- Do not ship docs without generated-output validation.
- Do not expose raw broker clients in sample code.
- Do not let modules reference each other directly.
- Do not model DDD as CRUD with folders.

## First Implementation Slice

The smallest credible first slice:

1. Create `templates/HeadlessShop` package.
2. Generate `Shop.Api`, `Shop.Contracts`, Catalog, Ordering, and architecture tests.
3. Implement `CreateProduct` and `PlaceOrder` through `Mediator.SourceGenerator`.
4. Publish `ProductCreated` through Headless Messaging.
5. Project product data into Ordering.
6. Add `AGENTS.md` and one recipe: `docs/recipes/add-command.md`.
7. Add generated-template validation.
8. Add one smoke test for product-to-order.

This keeps the first version narrow while proving the real differentiator: agent-safe extension of a production-shaped Headless app.

