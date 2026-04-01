---
title: "refactor: close messaging test-suite gaps"
type: refactor
status: active
date: 2026-04-01
---

# refactor: close messaging test-suite gaps

## Overview

Design a complete, layered test suite for `Headless.Messaging.Abstractions`, `Headless.Messaging.Core`, `Headless.Messaging.PostgreSql`, and `Headless.Messaging.Nats`.

The repo already has good coverage islands:

- abstractions cover `ConsumeContext`, `MessageHeader`, and conventions
- core covers many runtime paths, retries, circuit breakers, bootstrap readiness, and in-memory integration
- PostgreSQL has broad real-database storage coverage
- NATS has solid unit coverage plus transport/consumer integration tests

The missing pieces are the seams between those islands: a few behavior-bearing abstraction helpers, several deterministic core helpers, provider DI/transaction wrappers, shared harness adoption for NATS consumers, and no real end-to-end `NATS + PostgreSql + Core` suite through `AddHeadlessMessaging(...)`.

## Problem Frame

This plan is derived directly from the user request. There is no recent matching `docs/brainstorms/*-requirements.md` for this scope, and the request is specific enough for direct planning.

Inspection confirms these gaps:

- `Headless.Messaging.Abstractions` has thin coverage beyond `ConsumeContext`, `MessageHeader`, and conventions. Only the behavior-bearing helpers are worth expanding; trivial DTO and constructor-only surfaces should not get dedicated tests by default.
- `Headless.Messaging.Core` has strong feature tests, but several deterministic helper seams still have no focused tests: `ConsumerPauseGate`, `OutboxTransactionExtensions`, and `ScheduledMediumMessageQueue`.
- `Headless.Messaging.PostgreSql` relies almost entirely on options tests plus storage integration tests; provider-owned DI registration and ADO/EF transaction adapters are not directly covered.
- `Headless.Messaging.Nats` covers its main runtime types, but it does not consume `ConsumerClientTestsBase`, and it still lacks a real full-stack suite with durable PostgreSQL storage.
- `tests/Headless.Messaging.Core.Tests.Harness/` already defines the right contract layers; the suite is incomplete because those layers are not used consistently across the chosen provider pair.

## Requirements Trace

- R1. Define a complete test matrix for abstractions, core, PostgreSQL storage, and NATS transport split into fast unit tests and Docker-backed integration tests.
- R2. Cover each non-trivial behavior seam in the chosen scope: options validation, DI registration, transaction wrappers, pause/resume behavior, retry/delay helpers, storage and transport contract behavior, and full publish-consume-store lifecycle.
- R3. Reuse and extend existing shared harnesses wherever a scenario is generic across transports or storages; do not duplicate generic provider tests by hand.
- R4. Keep the suite maintainable by keeping deterministic logic in package-local unit projects, moving only real broker/database behavior into Testcontainers-backed integration suites, and skipping trivial unit tests that do not protect meaningful behavior.
- R5. Add at least one real end-to-end suite proving `AddHeadlessMessaging(...)` works with PostgreSQL storage and NATS transport together.

## Scope Boundaries

- This plan does not expand coverage for every transport or storage provider in the repo.
- This plan does not redesign production APIs or runtime behavior unless tests expose an ambiguity during execution.
- This plan does not add new coverage tooling, mutation tooling, or CI orchestration unless implementation proves a current gap blocks the new suites.
- This plan does not require direct tests for marker interfaces with no behavior unless they participate in a forwarding or DI contract.

## Context & Research

### Relevant Code and Patterns

- The repo targets `.NET 10` with `Microsoft.Testing.Platform` via [global.json](/Users/xshaheen/Dev/framework/headless-framework/global.json).
- Test projects already follow the repo split described in [CLAUDE.md](/Users/xshaheen/Dev/framework/headless-framework/CLAUDE.md): `*.Tests.Unit`, `*.Tests.Integration`, and `*.Tests.Harness`.
- Shared provider test harnesses already exist in:
  - [tests/Headless.Messaging.Core.Tests.Harness/TransportTestsBase.cs](/Users/xshaheen/Dev/framework/headless-framework/tests/Headless.Messaging.Core.Tests.Harness/TransportTestsBase.cs)
  - [tests/Headless.Messaging.Core.Tests.Harness/ConsumerClientTestsBase.cs](/Users/xshaheen/Dev/framework/headless-framework/tests/Headless.Messaging.Core.Tests.Harness/ConsumerClientTestsBase.cs)
  - [tests/Headless.Messaging.Core.Tests.Harness/DataStorageTestsBase.cs](/Users/xshaheen/Dev/framework/headless-framework/tests/Headless.Messaging.Core.Tests.Harness/DataStorageTestsBase.cs)
  - [tests/Headless.Messaging.Core.Tests.Harness/MessagingIntegrationTestsBase.cs](/Users/xshaheen/Dev/framework/headless-framework/tests/Headless.Messaging.Core.Tests.Harness/MessagingIntegrationTestsBase.cs)
- Core already has strong in-memory integration coverage in:
  - [tests/Headless.Messaging.Core.Tests.Unit/IntegrationTests/IDirectPublisherIntegrationTests.cs](/Users/xshaheen/Dev/framework/headless-framework/tests/Headless.Messaging.Core.Tests.Unit/IntegrationTests/IDirectPublisherIntegrationTests.cs)
  - [tests/Headless.Messaging.Core.Tests.Unit/IntegrationTests/RuntimeSubscriberIntegrationTests.cs](/Users/xshaheen/Dev/framework/headless-framework/tests/Headless.Messaging.Core.Tests.Unit/IntegrationTests/RuntimeSubscriberIntegrationTests.cs)
  - [tests/Headless.Messaging.Core.Tests.Unit/BootstrapperTests.cs](/Users/xshaheen/Dev/framework/headless-framework/tests/Headless.Messaging.Core.Tests.Unit/BootstrapperTests.cs)
- PostgreSQL already uses the storage harness through [tests/Headless.Messaging.PostgreSql.Tests.Integration/PostgreSqlStorageTests.cs](/Users/xshaheen/Dev/framework/headless-framework/tests/Headless.Messaging.PostgreSql.Tests.Integration/PostgreSqlStorageTests.cs), but its provider-owned helpers under [src/Headless.Messaging.PostgreSql](/Users/xshaheen/Dev/framework/headless-framework/src/Headless.Messaging.PostgreSql) are mostly untested.
- NATS already uses the transport harness through [tests/Headless.Messaging.Nats.Tests.Integration/NatsTransportTests.cs](/Users/xshaheen/Dev/framework/headless-framework/tests/Headless.Messaging.Nats.Tests.Integration/NatsTransportTests.cs), but it does not yet use `ConsumerClientTestsBase`, and it has no full-stack suite with durable storage.

### Institutional Learnings

- [docs/solutions/guides/messaging-transport-provider-guide.md](/Users/xshaheen/Dev/framework/headless-framework/docs/solutions/guides/messaging-transport-provider-guide.md)
  - Providers should rely on core for retries/serialization and prove broker contracts through targeted transport and consumer-client tests.
- [docs/solutions/concurrency/circuit-breaker-transport-thread-safety-patterns.md](/Users/xshaheen/Dev/framework/headless-framework/docs/solutions/concurrency/circuit-breaker-transport-thread-safety-patterns.md)
  - Concurrency-sensitive transport tests need deterministic gates, not sleep-driven timing.
- [docs/solutions/concurrency/startup-pause-gating-and-half-open-recovery.md](/Users/xshaheen/Dev/framework/headless-framework/docs/solutions/concurrency/startup-pause-gating-and-half-open-recovery.md)
  - Transport pause/resume semantics must be validated at startup and recovery boundaries, not only steady state.
- [docs/solutions/messaging/transport-wrapper-drift-and-doc-sync.md](/Users/xshaheen/Dev/framework/headless-framework/docs/solutions/messaging/transport-wrapper-drift-and-doc-sync.md)
  - Wrapper code drifts easily; contract tests should pin adapter behavior and sanitized operator-facing values.

### External References

- None. The repo already has strong local patterns for test structure, provider harnessing, and messaging runtime behavior, so external research would add little value here.

## Key Technical Decisions

- Reuse the shared harnesses as the default contract layer for providers.
  - Rationale: `TransportTestsBase`, `ConsumerClientTestsBase`, `DataStorageTestsBase`, and `MessagingIntegrationTestsBase` already encode the framework contract. Missing coverage should first be solved by adopting these harnesses before adding provider-specific custom tests.

- Promote reusable scenarios into the harness before adding provider-local tests.
  - Rationale: if a scenario applies to more than one transport or storage, encoding it once in the shared harness is cheaper and less likely to drift than copying it into each provider project.

- Keep unit tests behavioral, not ceremonial.
  - Rationale: empty interface existence tests, DTO getter/setter tests, logger-constant tests, and constructor-only exception tests add little signal. The test value is in forwarding behavior, lifecycle state transitions, DI contracts, broker/database contracts, and end-to-end behavior.

- Put PostgreSQL transaction-wrapper coverage in the PostgreSQL test projects, not in core.
  - Rationale: raw ADO.NET and EF transaction bridging are provider-owned behavior implemented in [src/Headless.Messaging.PostgreSql/PostgreSqlOutboxTransaction.cs](/Users/xshaheen/Dev/framework/headless-framework/src/Headless.Messaging.PostgreSql/PostgreSqlOutboxTransaction.cs), [src/Headless.Messaging.PostgreSql/EntityFrameworkTransactionExtensions.cs](/Users/xshaheen/Dev/framework/headless-framework/src/Headless.Messaging.PostgreSql/EntityFrameworkTransactionExtensions.cs), and [src/Headless.Messaging.PostgreSql/PostgreSqlEntityFrameworkDbTransaction.cs](/Users/xshaheen/Dev/framework/headless-framework/src/Headless.Messaging.PostgreSql/PostgreSqlEntityFrameworkDbTransaction.cs).

- Add one new full-stack integration project for `NATS + PostgreSql`.
  - Rationale: the repo currently has no real suite proving the exact pair the user called out works through DI bootstrap, outbox persistence, transport delivery, consumer execution, and monitoring. That gap is larger than any single provider unit gap.

- Prefer deterministic fast tests for timing and concurrency helpers, but only when they prove behavior that integration tests would not isolate cleanly.
  - Rationale: `ConsumerPauseGate`, delayed queue ordering, and transaction-forwarding tests can be proven with fake time, latches, and stubs. Docker should be reserved for real broker/database behavior, and trivial code should not be unit tested just to increase file coverage.

## Open Questions

### Resolved During Planning

- Do we need a separate brainstorming/requirements pass first?
  - No. The request is technical and scoped tightly enough for direct planning.

- Do we need external docs or best-practice research?
  - No. Existing repo patterns and solution docs are already sufficient.

- Should the existing provider tests be replaced?
  - No. Extend them, activate the unused harness layers, and add one new stack-level suite.

- Should the plan include direct tests for abstraction interfaces with no code?
  - No. Only behavioral adapters, helper extensions, and option/exception contracts need explicit tests.

### Deferred to Implementation

- Exact fixture composition for the dual-container end-to-end suite.
  - This can be a single combined fixture or a collection-based composition of NATS and PostgreSQL fixtures. The better choice depends on how much setup reuse is practical during implementation.

- Final name of the combined end-to-end test project.
  - `Headless.Messaging.NatsPostgreSql.Tests.Integration` is the clearest default, but the implementer may choose a repo-preferred alternative if another naming pattern exists.

- Whether some DTO-only tests remain after mutation or coverage review.
  - If a DTO-only test adds no signal beyond construction and serialization round-trip, implementation can keep it minimal or merge it into a broader contract test.

## High-Level Technical Design

Use a four-layer test matrix:

| Layer | Purpose | Projects |
|---|---|---|
| Abstraction contracts | fast helper/default/forwarding checks for behavior-bearing helpers only | `Headless.Messaging.Abstractions.Tests.Unit` |
| Core runtime contracts | fast deterministic helper/runtime checks with no Docker | `Headless.Messaging.Core.Tests.Unit` |
| Provider contracts | real broker/database contract checks using harnesses | `Headless.Messaging.PostgreSql.Tests.Integration`, `Headless.Messaging.Nats.Tests.Integration` |
| Full stack | real `AddHeadlessMessaging(...)` bootstrap and message lifecycle through the chosen provider pair | new NATS + PostgreSQL integration project |

The suite should prove behavior at the lowest layer that can prove it:

- use unit tests for pure helpers, forwarding adapters, and internal deterministic state machines
- use provider integration tests for actual NATS and PostgreSQL contracts
- use one full-stack suite for DI bootstrap, outbox persistence, transport delivery, and monitoring together
- when a new scenario is generic across providers, move it into `tests/Headless.Messaging.Core.Tests.Harness/` instead of leaving it as a one-off provider test

## Implementation Units

- [ ] **Unit 1: Close abstraction contract gaps**

**Goal:** Add only the abstraction tests that protect real behavior and avoid trivial DTO-style coverage.

**Requirements:** R1, R2, R4

**Dependencies:** None

**Files:**
- Create: `tests/Headless.Messaging.Abstractions.Tests.Unit/MessagePublisherExtensionsTests.cs`
- Create: `tests/Headless.Messaging.Abstractions.Tests.Unit/MessagingConventionsExtensionsTests.cs`
- Modify: `tests/Headless.Messaging.Abstractions.Tests.Unit/MessagingConventionsTests.cs`

**Approach:**
- Add focused tests only for extension-method forwarding and convention-helper behavior.
- Leave `PublishOptions` and `PublisherSentFailedException` without dedicated tests unless implementation uncovers a real behavior or regression worth pinning.
- Keep interface-only and property-bag types out of scope unless a forwarding helper depends on them.

**Patterns to follow:**
- [tests/Headless.Messaging.Abstractions.Tests.Unit/ConsumeContextTests.cs](/Users/xshaheen/Dev/framework/headless-framework/tests/Headless.Messaging.Abstractions.Tests.Unit/ConsumeContextTests.cs)
- [tests/Headless.Messaging.Abstractions.Tests.Unit/MessageHeaderTests.cs](/Users/xshaheen/Dev/framework/headless-framework/tests/Headless.Messaging.Abstractions.Tests.Unit/MessageHeaderTests.cs)

**Test scenarios:**
- Happy path — `PublishAsync<T>(publisher, message, ct)` forwards the same payload and cancellation token with `options: null`.
- Error path — `PublishAsync<T>(null!, ...)` throws `ArgumentNullException`.
- Happy path — `PublishDelayAsync<T>(publisher, delay, message, ct)` forwards the same delay, payload, and cancellation token with `options: null`.
- Happy path — `UseKebabCaseTopics`, `WithTopicPrefix`, `WithTopicSuffix`, and `WithDefaultGroup` mutate only the intended convention property and return the same instance.
- Edge case — convention helpers can be chained in any order without resetting previously assigned properties.

**Verification:**
- The abstractions unit suite covers the behavior-bearing helpers without adding low-signal DTO or constructor tests.
- The abstractions unit project stays Docker-free and fast.

- [ ] **Unit 2: Harden core deterministic helper and registration coverage**

**Goal:** Cover the remaining untested `Headless.Messaging.Core` seams that can be proven without external infrastructure.

**Requirements:** R1, R2, R4

**Dependencies:** Unit 1

**Files:**
- Create: `tests/Headless.Messaging.Core.Tests.Unit/Transport/ConsumerPauseGateTests.cs`
- Create: `tests/Headless.Messaging.Core.Tests.Unit/Transactions/OutboxTransactionExtensionsTests.cs`
- Create: `tests/Headless.Messaging.Core.Tests.Unit/Internal/ScheduledMediumMessageQueueTests.cs`
- Modify: `tests/Headless.Messaging.Core.Tests.Unit/MessagingBuilderTests.cs`
- Modify: `tests/Headless.Messaging.Core.Tests.Unit/BootstrapperTests.cs`
- Modify: `tests/Headless.Messaging.Core.Tests.Unit/IntegrationTests/RuntimeSubscriberIntegrationTests.cs`
- Modify: `tests/Headless.Messaging.Core.Tests.Harness/MessagingIntegrationTestsBase.cs`

**Approach:**
- Add focused tests for helper state machines and connection/transaction forwarding.
- Extend existing registration and runtime tests rather than creating a second competing setup suite.
- When a stack-level scenario is generic, promote it into `MessagingIntegrationTestsBase` instead of leaving it provider-local.
- Use fake `DbConnection`, fake `TimeProvider`, and explicit latches instead of sleeps.

**Execution note:** Add characterization tests first for any helper whose current timing behavior is subtle, especially `ScheduledMediumMessageQueue` and bootstrap/runtime-subscription boundaries.

**Patterns to follow:**
- [tests/Headless.Messaging.Core.Tests.Unit/Transport/BrokerAddressDisplayTests.cs](/Users/xshaheen/Dev/framework/headless-framework/tests/Headless.Messaging.Core.Tests.Unit/Transport/BrokerAddressDisplayTests.cs)
- [tests/Headless.Messaging.Core.Tests.Unit/BootstrapperTests.cs](/Users/xshaheen/Dev/framework/headless-framework/tests/Headless.Messaging.Core.Tests.Unit/BootstrapperTests.cs)
- [tests/Headless.Messaging.Core.Tests.Unit/IntegrationTests/RuntimeSubscriberIntegrationTests.cs](/Users/xshaheen/Dev/framework/headless-framework/tests/Headless.Messaging.Core.Tests.Unit/IntegrationTests/RuntimeSubscriberIntegrationTests.cs)

**Test scenarios:**
- Happy path — `ConsumerPauseGate.WaitIfPausedAsync` completes immediately when not paused.
- Edge case — `ConsumerPauseGate.PauseAsync` blocks waiters until `ResumeAsync` is called.
- Edge case — repeated `PauseAsync`/`ResumeAsync` calls are idempotent and return `false` once no transition occurs.
- Error path — waiting on a paused gate with a canceled token throws `OperationCanceledException`.
- Happy path — `ConsumerPauseGate.Release()` unblocks current waiters and prevents future pauses from transitioning.
- Happy path — `BeginOutboxTransaction(...)` opens a closed connection, assigns the underlying transaction, and copies `autoCommit`.
- Happy path — `BeginOutboxTransactionAsync(...)` uses the async `DbConnection` path and honors the requested isolation level.
- Error path — async outbox transaction creation propagates open/begin failures without partially mutating the transaction wrapper.
- Happy path — `ScheduledMediumMessageQueue` yields due items in `(sendTime, storageId)` order.
- Edge case — future items remain queued until due and do not disappear when polled early.
- Edge case — `UnorderedItems` reflects queued messages without mutating queue order.
- Happy path — `AddHeadlessMessaging(...)` still registers the expected core services (`IOutboxPublisher`, `IScheduledPublisher`, `IRuntimeSubscriber`, `IBootstrapper`, circuit-breaker services).
- Integration — bootstrap can recover from a failed first attempt and succeed on a later retry if the underlying blocker is removed.
- Integration — runtime subscriptions attached around bootstrap readiness still follow the intended before-ready and after-ready paths.
- Integration — any newly discovered generic end-to-end scenario is added once in `MessagingIntegrationTestsBase` and inherited by provider-pair suites.

**Verification:**
- The remaining deterministic `Headless.Messaging.Core` helper files have focused tests.
- Existing fast core suites remain infrastructure-free and timing-stable.

- [ ] **Unit 3: Add PostgreSQL provider unit coverage for DI and transaction wrappers**

**Goal:** Cover the PostgreSQL provider seams that are behavior-rich enough to justify direct tests and leave trivial code alone.

**Requirements:** R1, R2, R4

**Dependencies:** Unit 2

**Files:**
- Create: `tests/Headless.Messaging.PostgreSql.Tests.Unit/SetupTests.cs`
- Create: `tests/Headless.Messaging.PostgreSql.Tests.Unit/DbConnectionExtensionsTests.cs`
- Create: `tests/Headless.Messaging.PostgreSql.Tests.Unit/PostgreSqlOutboxTransactionTests.cs`
- Create: `tests/Headless.Messaging.PostgreSql.Tests.Unit/PostgreSqlEntityFrameworkDbTransactionTests.cs`
- Create: `tests/Headless.Messaging.PostgreSql.Tests.Unit/EntityFrameworkTransactionExtensionsTests.cs`
- Modify: `tests/Headless.Messaging.PostgreSql.Tests.Unit/PostgreSqlOptionsValidatorTests.cs`

**Approach:**
- Test `UsePostgreSql(...)` and `UseEntityFramework<TContext>()` as provider-registration contracts.
- Test transaction wrappers with stubs/fakes for `IDbTransaction`, `DbTransaction`, and `IDbContextTransaction`.
- Keep storage-table behavior in integration tests; keep wrapper forwarding in unit tests.
- Do not add direct tests for logging source-generator methods or property-bag options that already have adequate validation coverage.

**Patterns to follow:**
- [tests/Headless.Messaging.Nats.Tests.Unit/SetupTests.cs](/Users/xshaheen/Dev/framework/headless-framework/tests/Headless.Messaging.Nats.Tests.Unit/SetupTests.cs)
- [tests/Headless.Messaging.Nats.Tests.Unit/NatsConnectionPoolTests.cs](/Users/xshaheen/Dev/framework/headless-framework/tests/Headless.Messaging.Nats.Tests.Unit/NatsConnectionPoolTests.cs)

**Test scenarios:**
- Happy path — `UsePostgreSql(string)` registers the PostgreSQL storage marker, options, storage initializer, data storage, and outbox transaction services.
- Happy path — `UsePostgreSql(Action<PostgreSqlOptions>)` copies `MessagingOptions.Version` into PostgreSQL options.
- Happy path — `UseEntityFramework<TContext>()` and `UseEntityFramework<TContext>(configure)` set `DbContextType`, preserve version, and keep the same service registrations.
- Error path — provider setup methods reject `null` configure delegates.
- Happy path — `DbConnectionExtensions.ExecuteNonQueryAsync` opens closed connections, attaches transactions, and forwards parameters.
- Happy path — `DbConnectionExtensions.ExecuteReaderAsync` returns the reader callback result and handles `readerFunc: null` without throwing.
- Happy path — `DbConnectionExtensions.ExecuteScalarAsync` converts numeric results using invariant culture.
- Happy path — `PostgreSqlOutboxTransaction.Commit` and `CommitAsync` commit the correct underlying transaction type and flush buffered messages.
- Edge case — rollback paths call the underlying rollback method and do not flush buffered messages.
- Happy path — `PostgreSqlEntityFrameworkDbTransaction` forwards commit, rollback, dispose, dispose-async, and underlying `DbTransaction` exposure.
- Happy path — `EntityFrameworkTransactionExtensions` set `AutoCommit`, assign the wrapped transaction, and return a `PostgreSqlEntityFrameworkDbTransaction`.

**Verification:**
- The PostgreSQL unit suite covers provider-owned behavior seams without spending time on trivial logging or option-property tests.
- PostgreSQL unit tests prove DI and wrapper contracts without requiring Docker.

- [ ] **Unit 4: Expand PostgreSQL integration coverage around EF, monitoring, and transaction boundaries**

**Goal:** Prove the PostgreSQL provider behaves correctly against a real database for the seams that unit tests cannot prove.

**Requirements:** R1, R2, R4, R5

**Dependencies:** Unit 3

**Files:**
- Create: `tests/Headless.Messaging.PostgreSql.Tests.Integration/TestMessagingDbContext.cs`
- Create: `tests/Headless.Messaging.PostgreSql.Tests.Integration/PostgreSqlEntityFrameworkOutboxTransactionTests.cs`
- Modify: `tests/Headless.Messaging.PostgreSql.Tests.Integration/PostgreSqlStorageTests.cs`
- Modify: `tests/Headless.Messaging.PostgreSql.Tests.Integration/PostgreSqlMonitoringTest.cs`
- Modify: `tests/Headless.Messaging.PostgreSql.Tests.Integration/Headless.Messaging.PostgreSql.Tests.Integration.csproj`

**Approach:**
- Keep the shared `DataStorageTestsBase` coverage as the generic storage contract.
- Add targeted integration tests only for real PostgreSQL concerns: EF transaction wrapping, schema overrides, and monitoring queries over realistic data mixes.
- If any new storage scenario proves reusable for other providers, move it into `DataStorageTestsBase` instead of keeping it PostgreSQL-only.

**Execution note:** Start with failing real-database tests for commit/rollback behavior before changing or adding any provider helpers.

**Patterns to follow:**
- [tests/Headless.Messaging.PostgreSql.Tests.Integration/PostgreSqlStorageTests.cs](/Users/xshaheen/Dev/framework/headless-framework/tests/Headless.Messaging.PostgreSql.Tests.Integration/PostgreSqlStorageTests.cs)
- [tests/Headless.Messaging.PostgreSql.Tests.Integration/PostgreSqlMonitoringTest.cs](/Users/xshaheen/Dev/framework/headless-framework/tests/Headless.Messaging.PostgreSql.Tests.Integration/PostgreSqlMonitoringTest.cs)

**Test scenarios:**
- Integration — starting an outbox transaction through raw `IDbConnection.BeginTransaction(...)` persists buffered messages only after commit.
- Error path — rolling back a raw outbox transaction leaves no published rows behind.
- Integration — starting an outbox transaction through EF `DatabaseFacade.BeginTransaction(...)` wraps the real EF transaction and persists only on commit.
- Integration — a custom schema name creates and queries provider tables under that schema, not only under `messaging`.
- Integration — monitoring pagination preserves `TotalItems` for mixed publish/receive data sets even when the requested page is empty.
- Integration — monitoring filters by group, message name, status, and content against real rows.
- Integration — hourly success/failure aggregations report both publish and receive message types correctly.
- Edge case — provider initialization is idempotent across repeated `InitializeAsync()` calls.

**Verification:**
- PostgreSQL integration coverage proves both the generic storage contract and provider-specific transaction/monitoring behavior on a real database.

- [ ] **Unit 5: Fill NATS unit gaps and adopt the consumer-client harness**

**Goal:** Bring `Headless.Messaging.Nats` to the same contract completeness as the better-covered providers.

**Requirements:** R1, R2, R3, R4

**Dependencies:** Unit 2

**Files:**
- Modify: `tests/Headless.Messaging.Nats.Tests.Unit/SetupTests.cs`
- Modify: `tests/Headless.Messaging.Nats.Tests.Unit/MessagingNatsOptionsTests.cs`
- Modify: `tests/Headless.Messaging.Nats.Tests.Unit/NatsConnectionPoolTests.cs`
- Modify: `tests/Headless.Messaging.Nats.Tests.Unit/NatsTransportTests.cs`
- Modify: `tests/Headless.Messaging.Nats.Tests.Unit/NatsConsumerClientFactoryTests.cs`
- Modify: `tests/Headless.Messaging.Nats.Tests.Unit/NatsConsumerClientTests.cs`
- Create: `tests/Headless.Messaging.Nats.Tests.Integration/NatsConsumerClientHarnessTests.cs`
- Modify: `tests/Headless.Messaging.Nats.Tests.Integration/NatsConsumerClientTests.cs`
- Modify: `tests/Headless.Messaging.Nats.Tests.Integration/Headless.Messaging.Nats.Tests.Integration.csproj`

**Approach:**
- Keep branch-heavy logic tests in the unit project.
- Use `ConsumerClientTestsBase` for generic consumer-client contract coverage and keep the existing custom integration tests for NATS-specific behavior.
- Prefer improving `ConsumerClientTestsBase` when a scenario is generic instead of stacking more one-off NATS-only tests.

**Patterns to follow:**
- [tests/Headless.Messaging.Core.Tests.Harness/ConsumerClientTestsBase.cs](/Users/xshaheen/Dev/framework/headless-framework/tests/Headless.Messaging.Core.Tests.Harness/ConsumerClientTestsBase.cs)
- [tests/Headless.Messaging.Nats.Tests.Integration/NatsTransportTests.cs](/Users/xshaheen/Dev/framework/headless-framework/tests/Headless.Messaging.Nats.Tests.Integration/NatsTransportTests.cs)

**Test scenarios:**
- Happy path — `UseNats(...)` registers the NATS marker, transport, consumer factory, and connection pool services.
- Happy path — `NatsConnectionPool.ConnectAllAsync` eagerly connects every pooled connection and honors cancellation between connections.
- Error path — `NatsConsumerClientFactory.CreateAsync` disposes the client on cancellation as well as on connection failure.
- Happy path — `NatsTransport.SendAsync` returns success for normal publish acknowledgements and preserves sanitized broker address output.
- Error path — `NatsTransport.SendAsync` returns failed `OperateResult` when JetStream reports an error ack or zero sequence.
- Happy path — `NatsConsumerClient` custom header builder can append or overwrite headers without losing `Headers.Group`.
- Edge case — `BuildStreamSubjects(...)` handles mixed bare subjects and hierarchical subjects without dropping non-prefix topics.
- Edge case — pause/resume remains idempotent while one or more receive waits are already in flight.
- Error path — failures while building a transport message reject the NATS message and surface a consume-error log callback.
- Integration — `ConsumerClientTestsBase` scenarios pass for subscribe, fetch topics, broker address, graceful shutdown, null sender handling, and log callback wiring.
- Integration — rejecting a message causes JetStream redelivery rather than silent loss.
- Integration — a paused consumer started before first delivery does not process until resumed.
- Integration — transient consumer API failures log and retry instead of permanently exiting the listen loop.

**Verification:**
- The NATS suite covers the behavior-bearing transport and consumer-client seams without duplicating trivial option-property checks.
- The NATS integration suite now uses both the transport harness and the consumer-client harness.

- [ ] **Unit 6: Add a real NATS + PostgreSQL end-to-end suite**

**Goal:** Prove the exact stack the user asked about works together through DI, bootstrap, outbox persistence, transport delivery, consumer execution, and monitoring.

**Requirements:** R1, R2, R3, R4, R5

**Dependencies:** Units 3, 4, 5

**Files:**
- Create: `tests/Headless.Messaging.NatsPostgreSql.Tests.Integration/Headless.Messaging.NatsPostgreSql.Tests.Integration.csproj`
- Create: `tests/Headless.Messaging.NatsPostgreSql.Tests.Integration/NatsPostgreSqlFixture.cs`
- Create: `tests/Headless.Messaging.NatsPostgreSql.Tests.Integration/NatsPostgreSqlMessagingIntegrationTests.cs`

**Approach:**
- Create the first real subclass of `MessagingIntegrationTestsBase` backed by the chosen provider pair.
- Use one fixture to start both NATS and PostgreSQL containers and to expose connection settings to `ConfigureTransport(...)` and `ConfigureStorage(...)`.
- Keep generic end-to-end assertions inherited from the harness and add only the NATS/PostgreSQL-specific assertions that the harness cannot express by itself.
- If an assertion proves generic for other transport+storage pairs, move it up into `MessagingIntegrationTestsBase` during implementation.

**Execution note:** Start with a failing end-to-end outbox publish test and keep the initial fixture small before adding secondary scenarios.

**Patterns to follow:**
- [tests/Headless.Messaging.Core.Tests.Harness/MessagingIntegrationTestsBase.cs](/Users/xshaheen/Dev/framework/headless-framework/tests/Headless.Messaging.Core.Tests.Harness/MessagingIntegrationTestsBase.cs)
- [tests/Headless.Messaging.Nats.Tests.Integration/NatsFixture.cs](/Users/xshaheen/Dev/framework/headless-framework/tests/Headless.Messaging.Nats.Tests.Integration/NatsFixture.cs)
- [tests/Headless.Messaging.PostgreSql.Tests.Integration/PostgreSqlTestFixture.cs](/Users/xshaheen/Dev/framework/headless-framework/tests/Headless.Messaging.PostgreSql.Tests.Integration/PostgreSqlTestFixture.cs)

**Test scenarios:**
- Integration — outbox publish stores a message in PostgreSQL and the message is then consumed through NATS by a registered subscriber.
- Integration — direct publish through `IDirectPublisher` bypasses durable storage but still reaches the NATS-backed subscriber.
- Integration — delayed publish persists first, remains undispatched immediately, and later reaches the subscriber after the delay window.
- Integration — failing consumers increment retry/failure state and surface those counts through the PostgreSQL monitoring API.
- Integration — custom publish headers survive publisher -> NATS transport -> consumer context.
- Integration — `IBootstrapper.IsStarted` is `true` only after the full NATS + PostgreSQL stack is ready for publish/consume activity.
- Integration — runtime subscribers attached after bootstrap receive real NATS messages without manual restart.
- Integration — monitoring can retrieve the persisted published and received records created by the full-stack flow.

**Verification:**
- One Docker-backed suite proves the real provider pair works end to end through the framework entry point.
- The harness-defined publish/consume lifecycle assertions pass against NATS + PostgreSQL, not only against in-memory infrastructure.

## System-Wide Impact

- **Interaction graph:** `AddHeadlessMessaging(...)` wires core runtime, chosen transport, chosen storage, processors, bootstrapper, publishers, and consumer registration. The new suite must validate that graph at unit seams and once end to end.
- **Error propagation:** publish failures, bootstrap failures, commit/rollback boundaries, broker ack/nak behavior, and monitoring visibility must remain observable through the existing exception and `OperateResult` contracts.
- **State lifecycle risks:** pause/resume, delayed scheduling, runtime subscription timing, and outbox commit/rollback boundaries are the highest-risk state transitions and should get the tightest tests.
- **API surface parity:** extension methods in abstractions/core/provider setup files should stay aligned with the runtime services they register.
- **Integration coverage:** generic provider contracts belong in the shared harness; only provider-specific differences and full-stack wiring need custom integration tests.
- **Unchanged invariants:** no production API changes are required by this plan; the goal is to characterize and harden the existing contracts, not redefine them.

## Success Metrics

- Every behavior-bearing seam in the chosen package set is mapped to the lowest-signal test layer that can prove it.
- `tests/Headless.Messaging.Core.Tests.Harness/ConsumerClientTestsBase.cs` is actively used by NATS.
- `tests/Headless.Messaging.Core.Tests.Harness/MessagingIntegrationTestsBase.cs` is actively used by a real provider pair.
- Shared harnesses grow where scenarios are reusable across transports or storages instead of duplicating the same assertions in provider projects.
- Fast unit tests remain infrastructure-free and deterministic.
- Docker-backed suites prove NATS and PostgreSQL independently and together.

## Risks & Dependencies

| Risk | Mitigation |
|------|------------|
| Docker-backed suites become slow or flaky | Keep deterministic logic in unit projects, keep fixtures reusable, and reserve Docker for true broker/database behavior |
| Concurrency tests become timing-sensitive | Use fake time, latches, and explicit gates instead of sleep-based assertions |
| Duplicate provider tests drift away from the shared contract | Push generic scenarios into the harness and keep provider-specific tests narrowly scoped |
| Combined NATS + PostgreSQL fixture becomes hard to maintain | Start from the existing single-provider fixtures and compose only the minimal shared setup |
| Trivial unit tests consume time without raising confidence | Keep the success bar on behavior-bearing seams and prefer integration or harness coverage over DTO/constructor-level tests |
| Internal helper coverage tempts visibility hacks | Prefer behavior tests through existing public/internal access patterns already used by the repo; avoid widening production visibility just for tests |

## Sources & References

- [CLAUDE.md](/Users/xshaheen/Dev/framework/headless-framework/CLAUDE.md)
- [tests/Headless.Messaging.Core.Tests.Harness/README.md](/Users/xshaheen/Dev/framework/headless-framework/tests/Headless.Messaging.Core.Tests.Harness/README.md)
- [docs/solutions/guides/messaging-transport-provider-guide.md](/Users/xshaheen/Dev/framework/headless-framework/docs/solutions/guides/messaging-transport-provider-guide.md)
- [docs/solutions/concurrency/circuit-breaker-transport-thread-safety-patterns.md](/Users/xshaheen/Dev/framework/headless-framework/docs/solutions/concurrency/circuit-breaker-transport-thread-safety-patterns.md)
- [docs/solutions/concurrency/startup-pause-gating-and-half-open-recovery.md](/Users/xshaheen/Dev/framework/headless-framework/docs/solutions/concurrency/startup-pause-gating-and-half-open-recovery.md)
- [docs/solutions/messaging/transport-wrapper-drift-and-doc-sync.md](/Users/xshaheen/Dev/framework/headless-framework/docs/solutions/messaging/transport-wrapper-drift-and-doc-sync.md)
