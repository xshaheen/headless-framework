---
title: feat: Unify messaging contract and execution model
type: feat
date: 2026-03-10
---

> **Verification gate:** Before claiming any task or story complete — run the plan's `verification_command` and confirm PASS. Do not mark complete based on reading code alone.

# feat: Unify messaging contract and execution model

## Overview

Refine the `Headless.Messaging.*` packages into one consistent developer experience for direct publish, outbox publish, class subscriptions, and runtime function subscriptions.

This plan assumes a deliberate break-change release: the new public contract replaces the current public contract rather than running beside it behind long-lived compatibility shims.

The redesign should keep the core direction intact:

- minimal DI-correct APIs
- strict deterministic defaults
- fail-fast validation by default
- runtime-safe mutation for ephemeral subscriptions
- shared execution semantics across class handlers and function handlers
- diagnostics and correlation parity so dashboards and telemetry do not regress

## Problem Statement

The current surface is split across multiple partially overlapping models:

- publishing is divided between `IDirectPublisher` and `IOutboxPublisher`, with duplicated topic/header/correlation logic and different developer ergonomics
- broker handler registration is built around a frozen `ConsumerRegistry`, which blocks safe runtime mutation
- handler execution is split between class-based dispatch and separate subscribe/runtime flows
- lifecycle and execution semantics are inconsistent in places, including consumer lifecycle expectations versus actual dispatcher behavior

This makes the framework harder to reason about, harder to extend safely, and more likely to drift in telemetry, retry, and correlation semantics.

## Proposed Solution

Design one execution model with two explicit public entry points:

1. `Publish`
2. `Subscribe`

Both should share the same execution core and the same defaulting strategy.

Key design decisions:

- Keep class handlers centered on `IConsume<T>` rather than introducing a second primary class handler contract.
- Replace the current broker-specific `ConsumeContext<T>` role with a shared execution-context model specialized for broker messages while preserving one class-handler mental model.
- Make direct publish and outbox publish API-identical at the application boundary; delivery mode changes reliability behavior, not method naming.
- Add runtime function subscriptions explicitly through a dedicated runtime subscriber service, with deterministic handler identity and atomic registry mutation.
- Preserve or alias current diagnostic listener names and correlation behavior so monitoring integrations do not silently break.
- Treat the redesign as a hard public-contract replacement: remove superseded public entry points instead of preserving side-by-side legacy APIs, with compatibility limited to telemetry aliases and temporary internal adaptation during implementation.

## Technical Approach

### Architecture

#### 1. Public contract

Unify the app-facing registration shape around one root DI entrypoint:

```csharp
builder.Services.AddMessaging(m =>
{
    m.UseConventions(c =>
    {
        c.UseKebabCaseTopics();
        c.UseApplicationId("billing-api");
        c.UseVersion("v1");
    });

    m.UseRabbitMq(...);
    m.UsePostgreSqlOutbox(...);

    m.SubscribeFromAssemblyContaining<Program>();
});
```

Auto-registration rules:

- scan only when explicitly requested
- discover concrete closed `IConsume<T>` handlers
- register each closed interface once
- compute deterministic defaults for topic, group, and handler identity
- fail startup on duplicate topic/group/handler identity collisions
- do not keep the legacy registration DSL as a long-lived parallel public surface

#### 2. Shared execution core

Create a common execution pipeline used by:

- class message handlers
- runtime function handlers

Shared responsibilities:

- per-execution scoped DI
- correlation propagation
- diagnostics and activity creation
- filters/middleware
- retry and failure classification
- consistent exception-to-state transitions

#### 3. Publishing model

Publishers should converge on one options-based experience:

- same primary publish method shape for direct and outbox
- same topic resolution path
- same header and correlation behavior
- same validation rules

Outbox coordination should move behind scoped infrastructure rather than mutable publisher state.
The old publisher surface should not survive as a parallel app-facing API once the replacement is shipped.

#### 4. Runtime subscription model

Add a dedicated runtime subscriber surface for ephemeral broker-attached handlers.

Required guarantees:

- deterministic handler identity by default
- explicit, auditable opt-out for non-deterministic identity
- atomic subscribe/unsubscribe updates
- defined in-flight unsubscribe semantics
- duplicate registration rejection by default

### Alternative Approaches Considered

#### Split handler interfaces across multiple primary contracts

Rejected as the primary approach because it fragments the developer model and weakens the goal of one consistent class-handler shape.

#### Keep the current `IConsume<T>` and `ConsumeContext<T>` unchanged

Rejected because the current context is broker-shaped and would preserve contract ambiguity instead of tightening the messaging execution model.

#### Keep the frozen registry and bolt on a second runtime registry

Rejected because it would preserve two parallel execution paths and make diagnostics/failure semantics harder to keep aligned.

#### Keep both the old and new public contracts during a long compatibility window

Rejected because this redesign is explicitly allowed to break compatibility, and a dual public surface would preserve the ambiguity that this work is meant to remove.

## Stories

> Full story details in companion PRD: [`2026-03-10-feat-unify-messaging-contract-and-execution-model-plan.prd.json`](./2026-03-10-feat-unify-messaging-contract-and-execution-model-plan.prd.json)

| ID | Title | Size |
|----|-------|------|
| US-001 | [M] Define unified abstractions and DI registration surface | M |
| US-002 | [M] Align direct and outbox publish execution | M |
| US-003 | [L] Add runtime function subscriptions on the shared execution core | L |
| US-005 | [M] Preserve telemetry, docs, and regression coverage | M |
| US-006 | [S] Align consumer lifecycle contract with scoped dispatch semantics | S |

## Final Acceptance Criteria

### Functional Requirements

- [x] Developers can configure messaging through one coherent DI surface with explicit scanning and deterministic auto-registration rules.
- [x] Direct publish and outbox publish expose one aligned primary experience with strict defaults and explicit non-default behavior.
- [x] Runtime function subscriptions support safe concurrent subscribe/unsubscribe with deterministic identities and documented in-flight semantics.
- [x] The release removes the superseded public contract surface instead of carrying a long-lived side-by-side compatibility API.

### Non-Functional Requirements

- [x] Diagnostics and correlation parity are preserved across direct publish, outbox publish, and runtime subscriptions.
- [x] Registry mutation is atomic and race-safe under concurrent registration changes.
- [x] Validation remains fail-fast by default, with all opt-outs explicit and auditable.

### Quality Gates

- [x] Unit coverage exists for contract validation, duplicate registration policy, runtime mutation safety, lifecycle semantics, and telemetry parity.
- [x] Integration coverage exists for publish/subscribe and outbox/runtime cross-path scenarios.
- [x] Public XML docs and package READMEs are updated for the new contract.

## System-Wide Impact

### Interaction Graph

Publishing a broker message should flow through:

`app service -> unified publisher contract -> publish envelope/default resolver -> direct transport send OR outbox persistence -> diagnostics/correlation instrumentation -> subscriber execution core -> handler filters -> handler`

The critical rule is that the instrumentation, correlation, retry, and failure layers are not reimplemented separately for each path.

### Error & Failure Propagation

- publish validation failures should throw before persistence or transport send
- direct publish transport failures should fail the caller immediately
- outbox publish persistence failures should fail the caller immediately; later dispatch failures should follow outbox retry/failure semantics
- runtime handler failures should traverse the same retry/failure classification path unless explicitly overridden
- cleanup/lifecycle failures must never mask the original business failure

### State Lifecycle Risks

- outbox persistence and dispatch transitions must not create double-send or silent-drop paths
- runtime unsubscribe must not leave partially detached registrations visible to the selector
- telemetry compatibility work must not break dashboard expectations around listener/event names

### API Surface Parity

Public surfaces that must stay aligned:

- `IDirectPublisher`
- `IOutboxPublisher`
- DI registration extensions/builders
- class handler contracts
- runtime function subscription APIs
- OpenTelemetry and dashboard instrumentation

Superseded public surfaces should be removed or folded into the new contract rather than left as aliases, except for observability identifiers that need compatibility continuity.

### Integration Test Scenarios

- Register a scanned class handler and a runtime function handler for the same message type, then verify deterministic conflict handling and telemetry parity.
- Publish the same message through direct and outbox paths and verify correlation/header consistency.
- Unsubscribe a runtime handler during active message execution and verify future deliveries stop while in-flight work follows the documented rule.
- Preserve current dashboard/OpenTelemetry behavior by verifying existing listener names still emit expected events.

## Success Metrics

- framework consumers have one obvious DI registration path for messaging features
- no public API path requires duplicate topic/header/correlation configuration logic
- no long-lived dual public contract remains after the redesign lands
- runtime subscription safety is covered by explicit concurrency tests
- existing telemetry consumers and dashboards continue to function without silent regressions
- package docs reflect the new contract clearly enough that app teams can onboard without transport-specific tribal knowledge

## Dependencies & Prerequisites

- `Headless.Messaging.Abstractions`
- `Headless.Messaging.Core`
- `Headless.Messaging.OpenTelemetry`
- transport/storage providers that depend on current message and subscription metadata behavior

## Risk Analysis & Mitigation

- **Risk:** breaking public API too broadly across transport/storage providers
  - **Mitigation:** take the break explicitly, adapt providers behind the new abstractions, and avoid preserving dual public APIs that would multiply maintenance cost
- **Risk:** runtime subscription support regresses selector performance
  - **Mitigation:** use immutable snapshot registries and keep hot-path lookups allocation-light
- **Risk:** telemetry/dashboard regressions
  - **Mitigation:** preserve or alias current diagnostic listener names and add regression tests in `Headless.Messaging.OpenTelemetry.Tests.Unit`

## Future Considerations

- request/reply or callback patterns can be redesigned later on top of the unified execution model rather than preserved as legacy overload shape
- once the execution core is unified, transport-specific filters or middleware can be added without multiplying public abstractions

## Documentation Plan

Update all affected public docs and package guidance:

- `src/Headless.Messaging.Abstractions/README.md`
- `src/Headless.Messaging.Core/README.md`
- `docs/llms/messaging.txt`
- XML docs for all changed public abstractions and options

## Sources & References

### Internal References

- frozen registration behavior: `src/Headless.Messaging.Core/ConsumerRegistry.cs:17`
- current group/topic selection path: `src/Headless.Messaging.Core/Internal/IConsumerServiceSelector.cs:95`
- duplicated publish logic: `src/Headless.Messaging.Core/Internal/DirectPublisher.cs:58`, `src/Headless.Messaging.Core/Internal/OutboxPublisher.cs:166`
- current `IConsume<T>` contract and broker-shaped context: `src/Headless.Messaging.Abstractions/IConsume.cs:41`, `src/Headless.Messaging.Abstractions/ConsumeContext.cs:17`
- diagnostic event names to preserve: `src/Headless.Messaging.Core/Diagnostics/MessageDiagnosticListenerNames.cs:8`
- package guidance snapshot: `docs/llms/messaging.txt`

### External References

- MassTransit consumers/configuration: <https://masstransit.io/documentation/configuration/consumers>
- MassTransit scoped middleware/filters: <https://masstransit.io/documentation/configuration/middleware/scoped>
- MassTransit transactional outbox: <https://masstransit.io/documentation/patterns/transactional-outbox>
- MassTransit mediator runtime connect/disconnect: <https://masstransit.io/documentation/concepts/mediator>
- Foundatio.Mediator README: <https://github.com/FoundatioFx/Foundatio.Mediator/blob/main/README.md>
