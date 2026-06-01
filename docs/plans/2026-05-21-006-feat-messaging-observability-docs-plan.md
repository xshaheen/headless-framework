---
date: 2026-05-21
type: feat
status: completed
depth: deep
origin: docs/plans/2026-05-21-001-feat-messaging-bus-queue-split-plan.md
part_of: messaging-bus-queue-split
sequence: 5
depends_on:
  - docs/plans/2026-05-21-002-feat-messaging-foundation-contracts-plan.md
  - docs/plans/2026-05-21-003-feat-messaging-intent-persistence-drainer-plan.md
  - docs/plans/2026-05-21-004-feat-messaging-inmemory-testing-vertical-plan.md
  - docs/plans/2026-05-21-005-feat-messaging-provider-migration-plan.md
---

# feat: Messaging Observability, Dashboard, Demos, and Docs

## Summary

Finish the messaging split by exposing intent through observability and operator-facing projections, then rewrite the messaging docs and demos around the final bus/queue model. This slice intentionally runs last so documentation describes the shipped shape, not intermediate migration states.

Parent design map: [2026-05-21-001-feat-messaging-bus-queue-split-plan.md](2026-05-21-001-feat-messaging-bus-queue-split-plan.md).

## Scope

In scope:

- Parent unit: U10.
- Remaining demo/docs portions of U11.
- Requirements: R11, R12, R18, R19.
- Acceptance example: AE5.

Out of scope:

- Dashboard UI rendering for intent filters/badges; that remains #221.
- Provider runtime migrations already owned by the provider plan.
- Testing-package migration already owned by the InMemory vertical slice.
- NATS JetStream follow-up documentation beyond explicitly naming it as future work under #233.

## Key Decisions

- Emit both `headless.messaging.intent` and `messaging.destination.kind` together.
- Use one `SuppressIntentTags` flag for both tags.
- Diagnostic listener event names remain unchanged; event payloads only add `IntentType`.
- Dashboard endpoint/projection exposes `IntentType`; visual UI rendering is deferred.
- `docs/llms/messaging.md` becomes the canonical docs surface for the final model.
- Provider READMEs cross-link to the canonical capability matrix instead of each inventing a different explanation.

## Files

- Modify: `src/Headless.Messaging.OpenTelemetry/MessagingTags.cs`
- Modify: `src/Headless.Messaging.OpenTelemetry/MessagingInstrumentationOptions.cs`
- Create: `src/Headless.Messaging.OpenTelemetry/Internal/IntentTagEnricher.cs`
- Modify: `src/Headless.Messaging.OpenTelemetry/DiagnosticListener.cs`
- Modify: `src/Headless.Messaging.Core/Diagnostics/EventData.Message.P.cs`
- Modify: `src/Headless.Messaging.Core/Diagnostics/EventData.Message.S.cs`
- Modify: `src/Headless.Messaging.Dashboard/Endpoints/MessagingDashboardEndpoints.cs`
- Modify: `tests/Headless.Messaging.OpenTelemetry.Tests.Unit/`
- Modify: `tests/Headless.Messaging.Dashboard.Tests.Unit/`
- Modify: `demo/Headless.Messaging.*.Demo/Program.cs`
- Rename/update: `demo/Headless.Messaging.Aws.InMemory.Demo/`
- Rewrite: `docs/llms/messaging.md`
- Rewrite: `src/Headless.Messaging.Abstractions/README.md`
- Create/update: `src/Headless.Messaging.Bus.Abstractions/README.md`
- Create/update: `src/Headless.Messaging.Queue.Abstractions/README.md`
- Update: `src/Headless.Messaging.*/README.md`
- Audit/update: `.github/workflows/*.yml`
- Audit/update: `Makefile`
- Audit/update: `Directory.Packages.props`
- Conditional update: `docs/plans/2026-05-20-003-feat-messaging-publisher-intent-split-plan.md` if restored.

## Approach

1. Add intent and destination-kind tag constants.
2. Add `SuppressIntentTags` and an intent tag enricher following the existing OTel enricher pattern.
3. Thread intent into publish/consume activities without changing existing event names.
4. Add `IntentType` additively to diagnostic event data.
5. Expose `IntentType` through dashboard query/projection response shapes.
6. Update demos to show `IBus` and/or `IQueue` based on provider capability.
7. Read `docs/authoring/AUTHORING.md` before editing docs.
8. Rewrite `docs/llms/messaging.md` around type-level intent, durability axis, scheduling, provider capability, consumer intent, OTel tags, and migration from the old publisher trio.
9. Update abstraction/provider READMEs and keep their capability statements consistent with the parent matrix.
10. Audit workflows, Makefile targets, and package metadata for renamed AWS/InMemory paths.

## Test Suite Design

- OTel unit tests capture activities through `ActivityListener`.
- Dashboard unit tests verify `MessageQuery` and `MessageView` projection/filter behavior.
- Demo smoke tests build/run according to existing repo pattern.
- Docs/README checks are manual against `docs/authoring/AUTHORING.md`, plus text scans for stale API/package names.

## Test Scenarios

- `OutboxBus.PublishAsync` emits `headless.messaging.intent = "bus"` and `messaging.destination.kind = "topic"`.
- `OutboxQueue.EnqueueAsync` emits `headless.messaging.intent = "queue"` and `messaging.destination.kind = "queue"`.
- `SuppressIntentTags = true` suppresses both tags.
- Bus receive span emits intent tag with value `bus`; queue receive span emits `queue`.
- Diagnostic listener payloads preserve existing fields and add `IntentType`.
- `MessageView.IntentType` round-trips through dashboard projection.
- `MessageQuery.IntentType` filters dashboard result count/page queries.
- Demo projects resolve and use only interfaces supported by their selected provider.
- Docs mention every plan-local requirement R1-R19 by behavior or structural section.
- RedisPubSub volatile-delivery warning appears near the top of its README and in `docs/llms/messaging.md`.
- Docs migration notes cover old `IDirectPublisher` / `IOutboxPublisher` / `IScheduledPublisher` to new bus/queue interfaces; they do not document the unshipped method-level plan shape.

## Verification

- `tests/Headless.Messaging.OpenTelemetry.Tests.Unit/` passes.
- `tests/Headless.Messaging.Dashboard.Tests.Unit/` passes.
- Demo smoke/build checks pass.
- `make format-check` is clean.
- `make build` is green.
- `rg "IDirectPublisher|IOutboxPublisher|IScheduledPublisher|IMessagePublisher|DirectPublisher|OutboxPublisher" docs src tests demo` returns zero hits except intentional migration-history text.
- `rg "AwsSqs|InMemoryQueue" docs src tests demo` returns zero hits except intentional rename-history text and `InMemoryQueueTransport`.
- `docs/llms/messaging.md` and provider READMEs pass an authoring checklist review.

## Handoff Criteria

This plan is complete when users and agents can understand, observe, demo, and operate the final bus/queue model from the docs and telemetry without relying on stale publisher or package names.
