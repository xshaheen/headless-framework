---
date: 2026-05-16
topic: agent-ready-headless-template
origin: docs/ideation/2026-05-16-agent-ready-headless-template.md
---

# Agent-Ready Headless Template Requirements

## Summary

Build an agent-ready `headless-shop` template that demonstrates Headless Framework through a runnable modular shop, agent-readable guardrails, generated validation, and one business flow that proves DDD, CQRS, messaging, tenancy, OpenAPI, permissions, and tests work together.

---

## Problem Frame

Generic Clean Architecture templates are crowded and easy to imitate at the folder-name level. The useful Headless differentiator is not another skeleton; it is a generated .NET application that humans and AI agents can safely extend without breaking module boundaries, tenant posture, messaging contracts, or test expectations.

AI agents need executable feedback more than extra scaffolding. Without clear guardrails, repeatable recipes, architecture tests, and a single validation path, an agent can copy a pattern from the wrong layer, bypass shared contracts, or wire framework features through raw infrastructure instead of Headless abstractions.

The first requirements slice should therefore define a capability tour that is narrow enough to ship, but strong enough that a downstream implementation plan can treat docs, architecture tests, and generated-output validation as product requirements rather than optional polish.

---

## Actors

- A1. Application developer: Generates the template, studies the reference app, and extends it with new business behavior.
- A2. AI coding agent: Uses generated docs, recipes, and validation feedback to modify the app without drifting across architectural boundaries.
- A3. Framework maintainer: Ships and validates the template as part of the Headless Framework product surface.
- A4. Template-generated app runtime: Executes the sample flow across API, domain, mediator, persistence, messaging, tenancy, and tests.

---

## Key Flows

- F1. Generate and validate the shop template
  - **Trigger:** A developer or agent creates a new app from the `headless-shop` template.
  - **Actors:** A1, A2, A3
  - **Steps:** Generate the app, inspect the generated guardrails, run the documented validation command, and confirm the generated app builds and tests cleanly.
  - **Outcome:** The generated checkout is runnable, self-describing, and has a repeatable proof that template output is valid.
  - **Covered by:** R1, R2, R3, R10

- F2. Extend behavior through an agent-readable recipe
  - **Trigger:** A developer or agent wants to add a new command-style behavior to the generated app.
  - **Actors:** A1, A2
  - **Steps:** Read `AGENTS.md`, follow the command recipe, add behavior in the correct layer, and run validation to catch boundary violations.
  - **Outcome:** The change lands in the intended module/layer and validation failures explain how to fix drift.
  - **Covered by:** R2, R4, R5, R6, R10

- F3. Exercise the product-to-order smoke path
  - **Trigger:** The generated app runs its integration smoke path.
  - **Actors:** A4
  - **Steps:** Establish tenant context, create a product, publish the product-created signal through Headless messaging, update the ordering projection, place an order, and verify tenant isolation.
  - **Outcome:** The generated app proves the core Headless capability tour through an end-to-end business flow.
  - **Covered by:** R7, R8, R9, R10

---

## Requirements

**Template identity and positioning**
- R1. The first template must be positioned as `headless-shop`, an agent-ready Headless capability tour, not as `headless-clean-architecture` or another generic Clean Architecture starter.
- R2. The generated app must make safe human and AI extension a first-class product promise through guardrails, recipes, validation, and actionable architecture-test feedback.
- R3. The initial release must start with a single opinionated `tour` shape rather than a broad configuration matrix.

**Generated guardrails**
- R4. The generated app must include agent-readable instructions that explain the canonical module shape, where business rules live, how commands and queries are added, how modules communicate, which validation command to run, and which actions are forbidden.
- R5. The generated docs must include a concise architecture overview and at least one repeatable recipe for adding command-style behavior.
- R6. Architecture validation must fail with messages that tell humans and agents the fix direction, especially for direct module references, endpoint business logic, raw broker usage, and tenant-aware writes.

**Capability tour**
- R7. The first business flow must prove product creation through a Catalog-style aggregate and order placement through an Ordering-style projection.
- R8. The flow must demonstrate Headless Framework surfaces working together: domain primitives, mediator-based commands/queries, thin API endpoints, EF-backed persistence, Headless messaging, tenant posture, OpenAPI documentation, permissions around at least one command, and tests.
- R9. Cross-module communication must happen through shared contracts and Headless messaging abstractions, not direct references between module internals or raw broker clients.

**Validation contract**
- R10. Template validation must be treated as product validation: generated output must be packed, installed into an isolated template hive, generated into a clean directory, built, tested, architecture-checked, and smoke-tested.
- R11. The smoke validation must cover tenant setup, product creation, OpenAPI exposure, product visibility under the active tenant, integration-event publishing, ordering projection update, order placement, and tenant isolation failure behavior.
- R12. Generated docs and recipes must be validated against the generated checkout so stale package names, stale APIs, or non-runnable instructions are caught before release.

---

## Acceptance Examples

- AE1. **Covers R1, R2, R4.** Given a developer generates the template, when they open the generated checkout, they find agent-readable instructions that describe the template's extension contract and validation path without needing to infer the architecture from source alone.
- AE2. **Covers R6, R9.** Given an agent introduces a direct dependency from one module's internals to another, when architecture validation runs, it fails with an actionable message that points the shared-contract path rather than a vague dependency violation.
- AE3. **Covers R7, R8, R11.** Given the generated app is running under a tenant context, when the smoke flow creates a product and places an order, the ordering side can use projected product data and the smoke result proves the cross-layer Headless capability tour.
- AE4. **Covers R10, R12.** Given a recipe or generated doc references a stale package or API name, when generated-template validation runs, the release gate fails before the template is shipped.
- AE5. **Covers R3.** Given the first version ships, when a developer creates a new app without extra profile choices, they receive the opinionated tour shape rather than a menu of incomplete variants.

---

## Success Criteria

- A developer can generate the shop template and understand where to add behavior without reading framework internals first.
- An AI coding agent can use generated instructions, recipes, and validation output to add or adjust behavior while preserving module boundaries.
- The generated app demonstrates Headless capabilities through a meaningful end-to-end flow, not isolated package snippets.
- The template release process catches stale generated docs, invalid instructions, architecture drift, and broken smoke behavior before release.
- `dev-plan` can proceed without inventing product scope, success criteria, or v1/v2 boundaries.

---

## Scope Boundaries

- The first slice includes `headless-shop`, guardrails, at least one recipe, generated-template validation, and one product-to-order smoke path.
- `headless-module` is deferred until the flagship shop shape and validation contract are proven.
- `minimal` and `production` profiles are deferred until the default tour shape is stable.
- Broad provider switches are not a v1 feature; provider configurability should not become the template's main product promise.
- A ten-module stress app is out of scope for the default template.
- Raw broker-client sample code is out of scope; sample messaging must go through Headless messaging abstractions.
- CRUD-with-folders modeling is out of scope; the sample flow must show domain behavior and module communication.

---

## Key Decisions

- Capability tour over generic starter: This gives Headless a stronger position than folder naming and proves framework surfaces working together.
- Guardrails as product surface: Generated docs, recipes, architecture tests, and validation are part of the user value because they make agent extension safer.
- Opinionated `tour` first: Starting narrow reduces configuration sprawl and makes the initial validation contract enforceable.
- Validation as release gate: Template output is the product, so generated-output build, tests, architecture checks, smoke checks, and runnable docs must be treated as required release evidence.

---

## Dependencies / Assumptions

- The template will build on Headless Framework's existing .NET 10, Headless MSBuild SDK, xUnit v3, Microsoft Testing Platform, and package-convention baseline.
- The implementation plan should verify the current package APIs for multi-tenancy, messaging, OpenAPI, mediator, testing, permissions, and EF before choosing exact wiring.
- Existing LLM docs under `docs/llms/` are expected to inform generated agent instructions and recipes, but generated docs must still be validated against the generated checkout.
- The moved ideation source at `docs/ideation/2026-05-16-agent-ready-headless-template.md` is the origin artifact for this requirements document.

---

## Outstanding Questions

### Deferred to Planning

- [Affects R4, R5][Technical] Which exact generated documentation set is the smallest useful v1: full guardrail set from the ideation doc, or `AGENTS.md`, architecture overview, and one command recipe only?
- [Affects R6, R10][Technical] Which architecture-test framework and rule style best matches this repo's current test conventions and generated-template validation flow?
- [Affects R8, R11][Needs research] Which in-memory or local provider choices make the smoke path reliable without turning provider configuration into the main feature?
- [Affects R10, R12][Technical] Where should the generated-template validation live so it fits the repo's build and package workflow without becoming brittle?
