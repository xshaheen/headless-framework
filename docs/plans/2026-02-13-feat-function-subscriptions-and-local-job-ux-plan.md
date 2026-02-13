## Enhancement Summary

**Deepened on:** 2026-02-13
**Sections enhanced:** 11
**Research agents used:** `repo-research-analyst`, `learnings-researcher`, `best-practices-researcher`, `framework-docs-researcher`, `spec-flow-analyzer`, `architecture-strategist`, `strict-dotnet-reviewer`, `pragmatic-dotnet-reviewer`, `performance-oracle`, `security-sentinel`, `code-simplicity-reviewer`, `pattern-recognition-specialist`, `git-history-analyzer`.

### Section Manifest

- Section 1: `Overview` - tighten DX objective and define measurable reliability boundaries.
- Section 2: `Problem Statement / Motivation` - validate constraints and non-goals against current architecture.
- Section 3: `Proposed Solution` - strengthen decisions around shared execution-core and lifecycle semantics.
- Section 4: `Technical Considerations` - deepen DI scope, runtime mutation safety, and validation strategy.
- Section 5: `Research Summary` - ground plan with external standards and implementation references.
- Section 6: `SpecFlow Analysis` - expand race, lifecycle, and failure permutations.
- Section 7: `Stories` - add implementation-level quality bars per story.
- Section 8: `Success Metrics` - add objective thresholds for quality and performance.
- Section 9: `Dependencies & Risks` - attach concrete mitigation controls.
- Section 10: `References & Research` - include canonical documentation links.
- Section 11: `Related Work` - keep branch and migration context explicit.

### Key Improvements

1. Added explicit shared execution-core requirements: scoped DI, diagnostics parity, and correlation parity across class/function paths.
2. Added fail-fast guardrail policy details using startup validation patterns and explicit opt-out governance.
3. Expanded concurrency/race coverage for runtime subscribe/unsubscribe behavior with deterministic handler identity and lifecycle rules.

### New Considerations Discovered

- Runtime function registration requires a deterministic identity key model and atomic registry updates to avoid race conditions.
- Local job execution should keep durable storage as source of truth and avoid direct storage bypass patterns.
- Guardrails must distinguish hard failures (invalid config) from advisory controls (operational warnings) while still defaulting strict.

## Overview

Add a minimal, DI-correct messaging API that improves developer experience while preserving reliability guarantees. The feature introduces runtime function subscriptions for regular broker-attached messages, keeps scheduled jobs class-based with a simpler wrapper abstraction, and unifies execution behavior through a shared core path.

This plan uses the brainstorm dated `2026-02-13` (`messaging-dx-function-subscription-and-job-ux`) as the source of truth for WHAT to build.

**Branching Context**
- Base branch: `xshaheen/unified-messaging-scheduling-rebased`
- Feature branch: `codex/feat-messaging-dx-function-subscription-jobs-unified`
- Worktree: `.worktrees/feat-messaging-dx-function-subscription-jobs-unified`

### Research Insights

**Best Practices:**
- Keep the external API minimal and deterministic, but route all execution through one internal invocation path.
- Preserve existing diagnostics/correlation contracts so dashboards and telemetry are not broken by new function handlers.

**Performance Considerations:**
- Runtime handler lookup should be O(1) by deterministic key and avoid reflection on hot paths.
- Shared invocation should continue using compiled delegates where possible to avoid dispatch overhead.

**Implementation Details:**
```csharp
// Planning target: one runtime registration contract with deterministic identity.
public readonly record struct RuntimeSubscriptionKey(Type MessageType, string Topic, string Group, string HandlerId);
```

**Edge Cases:**
- Process receives messages while handler is being unsubscribed.
- Two subscriptions register identical semantics with different delegates.

## Problem Statement / Motivation

Current consumer and scheduling ergonomics require boilerplate that increases cognitive load for app developers. The team wants a minimal API for subscribe/publish/schedule operations, with strict defaults and deterministic behavior.

Constraints from the approved brainstorm:
- Runtime function subscriptions are broker-attached, DI-friendly, and ephemeral.
- Scheduled jobs stay class-based, execute locally, and require durable storage.
- Topic and group should be optional with deterministic convention defaults.
- Guardrails should be strict by default (fail fast), with explicit opt-out.
- Breaking changes are allowed to achieve meaningful DX improvement.

### Research Insights

**Best Practices:**
- Message/job handlers should be idempotent and retry-safe; scheduled and queued execution can repeat work under failure conditions.
- Job arguments should remain small and stable to reduce serialization fragility and operational overhead.

**Performance Considerations:**
- Convention defaults reduce setup cost, but implicit behavior must be deterministic and visible in logs/diagnostics.

**Edge Cases:**
- Omitted topic/group defaults drift if conventions are mutated after runtime subscriptions are added.
- Strict validation blocks startup when invalid cron/time zone exists; migration path must be explicit.

## Proposed Solution

Deliver a hybrid model:
- Add minimal runtime APIs for message subscription and publishing.
- Keep scheduled jobs class-based, but add a wrapper abstraction to reduce boilerplate.
- Build a shared execution core so class handlers and function handlers share correlation, filter pipeline, diagnostics, and scoped lifetime semantics.
- Apply strict guardrails in runtime validation and configuration checks.

### Research Insights

**Best Practices:**
- Keep scheduler execution local for jobs while retaining broker transport semantics for regular message subscriptions.
- Maintain explicit contract that runtime function subscriptions are ephemeral and non-persistent.

**Performance Considerations:**
- Function subscription updates should use snapshot or copy-on-write semantics to keep reads lock-light under load.
- Avoid duplicate broker subscription churn by batching topic/group rebinds when possible.

**Implementation Details:**
```csharp
// Planning target: shared invoker interface used by class and function handlers.
internal interface IMessageExecutionCore
{
    ValueTask ExecuteAsync(ExecutionEnvelope envelope, CancellationToken cancellationToken = default);
}
```

**Edge Cases:**
- Subscribe/unsubscribe during in-flight dispatch must define whether current invocation completes (recommended: complete in-flight, apply changes to next message).
- Function handler exceptions must preserve current retry/failure semantics used by class consumers.

## Technical Considerations

- API design consistency: new runtime APIs must align with existing `IMessagingBuilder` and `IConsumerBuilder` semantics.
- DI scope safety: each message function invocation and each scheduled job execution must run in an isolated scope.
- Runtime mutation safety: ephemeral subscribe/unsubscribe needs thread-safe registry updates and deterministic behavior under concurrent publish/subscribe.
- Scheduling boundary: jobs execute locally through scheduler infrastructure, not through transport subscribers.
- Compatibility: planned breaking changes need explicit migration guidance and deprecation path where practical.

### Research Insights

**Best Practices:**
- Use `IServiceScopeFactory` (or async scope equivalent) per invocation in background/worker flows.
- Validate options at startup to fail fast for invalid operational configuration.
- Do not bypass scheduler/storage contracts with ad-hoc direct table writes.

**Performance Considerations:**
- Prefer immutable snapshot lookup structures for runtime handlers to reduce lock contention during dispatch.
- Track dispatch latency and error rates separately for class vs function paths during migration.

**Implementation Details:**
```csharp
// Planning target: strict options validation at startup.
services
    .AddOptions<MessagingRuntimeOptions>()
    .Validate(o => o.MaxPayloadBytes > 0, "MaxPayloadBytes must be > 0")
    .ValidateOnStart();
```

**Edge Cases:**
- Scoped service disposal leaks if cancellation interrupts invocation cleanup.
- Dynamic runtime subscriptions can produce duplicate delivery if identity model is not canonicalized.

## Research Summary

### Internal Repository Findings

- Consumer registration and conventions are centered on `IMessagingBuilder` + `MessagingOptions` + `ConsumerBuilder`.
  - `src/Headless.Messaging.Abstractions/IMessagingBuilder.cs`
  - `src/Headless.Messaging.Core/Configuration/MessagingOptions.cs`
  - `src/Headless.Messaging.Core/ConsumerBuilder.cs`
- Runtime message execution currently flows via subscribe internals.
  - `src/Headless.Messaging.Core/Internal/IConsumerRegister.cs`
  - `src/Headless.Messaging.Core/Internal/ISubscribeInvoker.cs`
- Scheduling already has local execution and scoped dispatch behavior.
  - `src/Headless.Messaging.Core/Scheduling/ScheduledJobDispatcher.cs`
  - `src/Headless.Messaging.Core/Scheduling/SchedulerBackgroundService.cs`
  - `src/Headless.Messaging.Core/Scheduling/ScheduledJobManager.cs`
- ServiceCollection-based consumer registration currently has capability gaps for schedule-specific fluent APIs.
  - `src/Headless.Messaging.Abstractions/ServiceCollectionConsumerBuilder.cs`

### Institutional Learnings

- No `docs/solutions/` entries were found for this topic in this branch.

### External Research Findings

- .NET guidance for scoped services in background workers aligns with per-execution scope requirement.
- .NET options validation guidance supports strict startup validation (`ValidateOnStart`) for guardrails.
- Hangfire and Quartz guidance reinforce idempotency/reentrancy and strict scheduler contract usage.
- MassTransit and NServiceBus handler models support keeping class-based handler contracts while improving registration ergonomics.
- Cronos documentation emphasizes DST handling semantics for recurring scheduling correctness.

### References Collected

- [Use scoped services within a `BackgroundService`](https://learn.microsoft.com/en-us/dotnet/core/extensions/scoped-service)
- [Options pattern and validation (`ValidateOnStart`)](https://learn.microsoft.com/en-us/dotnet/core/extensions/options)
- [Dependency injection guidelines](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection-guidelines)
- [MassTransit consumers](https://masstransit.io/documentation/configuration/consumers)
- [NServiceBus handlers](https://docs.particular.net/nservicebus/handlers/)
- [Hangfire best practices](https://docs.hangfire.io/en/latest/best-practices.html)
- [Quartz scheduler best practices](https://www.quartz-scheduler.org/documentation/quartz-2.5.x/best-practices.html)
- [Cronos DST behavior](https://github.com/HangfireIO/Cronos)

## SpecFlow Analysis

### User Flow Overview

1. App developer registers runtime function subscription for a message type and optional topic/group.
2. Runtime message arrives from broker, handler executes with DI scope and existing filter/diagnostic behavior.
3. App developer unsubscribes runtime handler, and subsequent broker messages no longer route to it.
4. App developer defines class-based recurring job through wrapper abstraction and schedules one-time jobs through minimal API.
5. Scheduler acquires due jobs from durable storage, executes locally in scoped DI, and updates execution history.

### Key Permutations Covered

- First registration vs duplicate registration of same function key.
- Omitted topic/group vs explicit topic/group.
- Concurrent subscribe/unsubscribe during active message processing.
- Function handler success, handler exception, and cancellation.
- Job enable/disable/trigger with local execution.
- Retry/misfire behavior for class-based recurring jobs.

### Missing Elements To Resolve During Implementation

- Deterministic identity model for runtime function handlers.
- Explicit conflict semantics for duplicate runtime registrations.
- Guardrail thresholds and override mechanism shape.
- Migration contract for existing public APIs impacted by breaking changes.

### Research Insights

**Best Practices:**
- Define and document conflict policy: reject duplicate identity vs replace existing registration.
- Define unsubscribe consistency: in-flight invocations complete; new invocations use updated registry.

**Performance Considerations:**
- Reconciliation/rebind operations should be amortized and avoid per-message subscription checks.

**Edge Cases:**
- Handler removed while awaiting I/O.
- Runtime registration called before broker connection is fully healthy.
- Local job triggered manually while same recurring job run is active and `SkipIfRunning=true`.

## Stories

### US-001: Define minimal public DX API surface [M]

Define and document the new minimal API contract for runtime message functions and scheduling operations, including deterministic convention defaults and strict guardrail behavior.

**Files to Study:**
- src/Headless.Messaging.Abstractions/IMessagingBuilder.cs
- src/Headless.Messaging.Abstractions/IConsumerBuilder.cs
- src/Headless.Messaging.Abstractions/MessagingConventions.cs
- src/Headless.Messaging.Abstractions/Scheduling/IScheduledJobManager.cs

**Acceptance Criteria:**
- [ ] Public APIs include minimal operations for subscribe/unsubscribe/publish/schedule/trigger/enable/disable/list.
- [ ] Omitted topic/group behavior is deterministic and documented as convention-driven defaults.
- [ ] XML docs are updated to describe strict-by-default validation and opt-out model.

**Research Insights:**
- Keep naming symmetric across APIs: `Subscribe`/`Unsubscribe`, `EnableJob`/`DisableJob`, `ListJobs`/`ListExecutions`.
- Prefer additive overloads with optional parameters over many bespoke entry points.

### US-002: Implement runtime broker-attached function subscriptions [L]

Add runtime function subscription and unsubscription for regular messages, backed by broker routing and DI-friendly handler invocation.

**Files to Study:**
- src/Headless.Messaging.Core/Configuration/MessagingOptions.cs
- src/Headless.Messaging.Core/Internal/IConsumerRegister.cs
- src/Headless.Messaging.Core/Internal/ISubscribeInvoker.cs
- src/Headless.Messaging.Core/ConsumerRegistry.cs

**Acceptance Criteria:**
- [ ] Runtime function handlers can subscribe and unsubscribe without process restart.
- [ ] Subscriptions are broker-attached and use deterministic topic/group defaults when omitted.
- [ ] Runtime subscriptions are ephemeral and do not persist across application restart.
- [ ] Message function execution uses scoped DI and supports cancellation tokens.

**Research Insights:**
- Add deterministic `HandlerId` to support conflict checks and predictable unsubscribe.
- Use an atomic registry update strategy (snapshot replacement) to avoid dispatch races.
- Ensure broker re-subscription minimizes churn when only one handler changes.

### US-003: Unify class and function execution via shared core [L]

Refactor execution paths so class consumers and runtime function consumers share a common invocation core for consistent behavior.

**Files to Study:**
- src/Headless.Messaging.Core/Internal/ISubscribeInvoker.cs
- src/Headless.Messaging.Core/Internal/ISubscribeExecutor.cs
- src/Headless.Messaging.Core/Scheduling/ScheduledJobDispatcher.cs
- src/Headless.Messaging.Core/Diagnostics/MessageDiagnosticListenerNames.cs

**Acceptance Criteria:**
- [ ] Class and function handlers share correlation propagation and diagnostic events.
- [ ] Shared invocation path applies filters and error semantics consistently.
- [ ] Execution creates one scoped DI scope per invocation and disposes it correctly.

**Research Insights:**
- Keep telemetry event names stable and add dimension labels (`handler_kind=class|function`) instead of new event names.
- Preserve existing exception wrapping and retry semantics to avoid behavior regressions.

### US-004: Add class-based job wrapper abstraction for simpler scheduling [M]

Introduce a wrapper abstraction that keeps jobs class-based but reduces boilerplate for recurring and one-time job handlers.

**Files to Study:**
- src/Headless.Messaging.Abstractions/RecurringAttribute.cs
- src/Headless.Messaging.Abstractions/ScheduledTrigger.cs
- src/Headless.Messaging.Core/ConsumerBuilder.cs
- src/Headless.Messaging.Core/Scheduling/ScheduledJobDispatcher.cs

**Acceptance Criteria:**
- [ ] Wrapper abstraction simplifies class-based scheduled job authoring while preserving current capabilities.
- [ ] Scheduled jobs continue executing locally and are not routed as broker subscribers.
- [ ] Job execution uses scoped DI with parity to message execution behavior.

**Research Insights:**
- Keep wrapper API thin and avoid creating a second scheduling model.
- Enforce time-zone and cron validation at registration time, not first execution time.

### US-005: Enforce strict guardrails and deterministic defaults [M]

Implement fail-fast validation for high-risk configuration paths and make opt-out explicit and auditable.

**Files to Study:**
- src/Headless.Messaging.Core/Configuration/MessagingOptions.cs
- src/Headless.Messaging.Abstractions/MessagingConventions.cs
- src/Headless.Messaging.Core/Scheduling/SchedulerOptions.cs
- src/Headless.Messaging.Abstractions/RecurringAttribute.cs

**Acceptance Criteria:**
- [ ] Invalid topic/group, invalid cron/timezone, and invalid schedule semantics fail fast with clear error messages.
- [ ] Guardrail opt-out requires explicit configuration and is documented with warnings.
- [ ] Validation behavior is covered by tests for both class and function paths.

**Research Insights:**
- Use startup validation and invariant checks for all defaulted conventions.
- Add safe bounds for payload and retry-related settings to reduce abuse/DoS risks.

### US-006: Provide migration guidance for planned breaking changes [S]

Create migration notes and upgrade guidance mapping old APIs and behavior to the new minimal surface.

**Files to Study:**
- src/Headless.Messaging.Abstractions/README.md
- src/Headless.Messaging.Core/README.md
- README.md

**Acceptance Criteria:**
- [ ] Migration guide documents all breaking API and behavior changes.
- [ ] Old-to-new mapping examples exist for subscribe, publish, and scheduling usage.
- [ ] Release notes include explicit risk notes and rollback guidance.

**Research Insights:**
- Include a compatibility matrix: old API, new API, runtime behavior delta, migration risk.
- Provide one end-to-end “before vs after” snippet for each primary scenario.

### US-007: Add comprehensive tests for runtime functions and local jobs [L]

Add unit and integration tests to protect runtime function subscriptions, job local execution, strict guardrails, and execution core parity.

**Files to Study:**
- tests/Headless.Messaging.Core.Tests.Unit/Scheduling/ScheduledJobDispatcherTests.cs
- tests/Headless.Messaging.Core.Tests.Unit/Scheduling/ScheduledJobManagerTests.cs
- tests/Headless.Messaging.Core.Tests.Unit/DispatcherTests.cs
- tests/Headless.Messaging.Abstractions.Tests.Unit/ServiceCollectionConsumerBuilderTests.cs

**Acceptance Criteria:**
- [ ] Runtime function subscribe/unsubscribe flows are covered for success, duplicate, and removal cases.
- [ ] Local scheduled job execution is covered for success, failure, retry, and misfire branches.
- [ ] Shared execution core parity is verified across class and function handlers.
- [ ] Guardrail fail-fast and explicit opt-out paths are both validated.

**Research Insights:**
- Add concurrency-focused tests around rapid subscribe/unsubscribe while dispatch is active.
- Add regression tests to verify diagnostics and correlation IDs are unchanged for existing class consumers.

## Success Metrics

- Reduced setup steps for common publish/subscribe/schedule workflows.
- Fewer configuration errors reaching runtime due to strict guardrails.
- No regression in scheduling reliability and execution telemetry.
- Stable test coverage across new execution paths.

### Research Insights

**Target Metrics:**
- At least 30% reduction in lines required for first working subscription scenario in docs samples.
- Zero increase in scheduled-job failure rate after migration under baseline test load.
- No regression in existing diagnostic counters and event emission.
- Unit and integration coverage for all new public API branches and failure branches.

## Dependencies & Risks

- Dependency on storage and scheduler internals for local job behavior.
- Risk of behavioral drift during shared-core refactor if parity tests are incomplete.
- Migration risk due to approved breaking changes.
- Runtime subscription lifecycle race conditions if handler identity semantics are underspecified.

### Research Insights

**Mitigations:**
- Gate runtime function path behind an internal feature toggle during rollout on main branch.
- Add explicit invariants for registration lifecycle and reject ambiguous duplicates.
- Publish migration checklist and provide one release overlap window if feasible.
- Add load tests for registry mutation and dispatch contention before release cut.

## References & Research

### Internal References

- docs/brainstorms/2026-02-13-messaging-dx-function-subscription-and-job-ux-brainstorm.md
- src/Headless.Messaging.Abstractions/IMessagingBuilder.cs
- src/Headless.Messaging.Abstractions/IConsumerBuilder.cs
- src/Headless.Messaging.Core/Configuration/MessagingOptions.cs
- src/Headless.Messaging.Core/Internal/IConsumerRegister.cs
- src/Headless.Messaging.Core/Internal/ISubscribeInvoker.cs
- src/Headless.Messaging.Core/Scheduling/ScheduledJobDispatcher.cs
- src/Headless.Messaging.Core/Scheduling/SchedulerBackgroundService.cs

### External References

- [Use scoped services within a `BackgroundService`](https://learn.microsoft.com/en-us/dotnet/core/extensions/scoped-service)
- [Dependency injection guidelines](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection-guidelines)
- [Options pattern and validation](https://learn.microsoft.com/en-us/dotnet/core/extensions/options)
- [MassTransit consumer configuration](https://masstransit.io/documentation/configuration/consumers)
- [MassTransit dynamic endpoint connection](https://masstransit.io/usage/configuration.html)
- [NServiceBus message handlers](https://docs.particular.net/nservicebus/handlers/)
- [Hangfire best practices](https://docs.hangfire.io/en/latest/best-practices.html)
- [Quartz scheduler best practices](https://www.quartz-scheduler.org/documentation/quartz-2.5.x/best-practices.html)
- [Cronos documentation](https://github.com/HangfireIO/Cronos)

### Related Work

- Current feature branch baseline: `xshaheen/unified-messaging-scheduling-rebased`
