---
date: 2026-03-19
topic: messaging-test-harness
focus: test harness for messaging like MassTransit
---

# Ideation: Messaging Test Harness

## Codebase Context

**headless-framework** is a modular .NET 10 framework (~94 NuGet packages). Messaging follows abstraction+provider pattern with ~10 transport providers (RabbitMQ, Kafka, NATS, SQS, Pulsar, Redis, PostgreSQL, SQL Server, Azure Service Bus, InMemoryQueue).

**Current messaging architecture:**
- `IMessagePublisher` — publishes typed messages with topic resolution
- `IConsume<TMessage>` — type-safe consumer handlers with scoped DI
- `ConsumeContext<TMessage>` — carries payload, MessageId, CorrelationId, Headers, Topic
- `IConsumerClient` — broker transport contract (Subscribe -> Listen -> Commit/Reject)
- `Dispatcher` — async channel orchestrator (PublishedChannel, ReceivedChannel, ScheduledMediumMessageQueue)
- `ConsumerRegistry` — thread-safe singleton, frozen on first read
- `IConsumeFilter` — pipeline filters for cross-cutting concerns
- `IRetryBackoffStrategy` — exponential/fixed backoff

**Current test infrastructure:**
- `MessagingIntegrationTestsBase` — abstract base for full pub-sub cycle
- `TestMessage` / `TestSubscriber` — simple capture helpers
- InMemoryQueue — basic test double (not a harness)

**Key gaps:** No message capture/assertion helpers, no dead-letter/failure simulation, no consumer isolation between parallel tests, no filter pipeline testing, no temporal assertions, ConsumerRegistry singleton leaks between tests.

**Related work:** Circuit breaker + adaptive retry brainstorm (2026-03-18), saga pattern brainstorm exists.

## Ranked Ideas

### 1. Test Harness Core (ITestHarness + Assertions + TestConsumer<T>)
**Description:** Central ITestHarness interface with observable collections (Published, Consumed, Faulted), TestConsumer<T> capturing full ConsumeContext, and awaitable assertion primitives.
**Rationale:** Foundational primitive — enables all other test harness features. Eliminates Thread.Sleep/polling patterns.
**Downsides:** API surface design is critical — too thin not useful, too thick too opinionated.
**Confidence:** 95%
**Complexity:** Medium
**Status:** Explored

### 2. Full Test Isolation Layer
**Description:** Per-test scoped transport + ConsumerRegistry fork + automatic teardown via IAsyncLifetime.
**Rationale:** Without isolation, parallel tests are fundamentally broken.
**Downsides:** Requires ConsumerRegistry internals changes. Per-test DI rebuild overhead.
**Confidence:** 90%
**Complexity:** Medium
**Status:** Unexplored

### 3. Fault Injection Engine
**Description:** Decorator IConsumerClient with configurable fault policies (fail on Nth message, duplicate delivery, specific exceptions).
**Rationale:** Circuit breaker and retry logic untestable without controlled failures.
**Downsides:** Fault policy API surface can grow unbounded.
**Confidence:** 85%
**Complexity:** Medium-High
**Status:** Unexplored

### 4. Deterministic Test Clock
**Description:** Fake IClock/TimeProvider for retry backoff, circuit breaker, scheduled messages. Advance(TimeSpan) API.
**Rationale:** Turns O(minutes) time-dependent tests into O(milliseconds).
**Downsides:** Requires TimeProvider abstraction retrofit across messaging core.
**Confidence:** 85%
**Complexity:** Medium-High
**Status:** Unexplored

### 5. Filter Pipeline Probe
**Description:** IConsumeFilter that records filter execution sequence, context mutations, short-circuiting.
**Rationale:** Filter ordering bugs invisible until runtime; pipeline grows with saga/circuit breaker.
**Downsides:** Requires filter pipeline interception points.
**Confidence:** 75%
**Complexity:** Medium
**Status:** Unexplored

### 6. Saga State Snapshot Assertions
**Description:** ISagaTestHarness<TSaga> with state machine snapshots at each transition.
**Rationale:** Most complex messaging construct — transition bugs cascade silently.
**Downsides:** Depends on saga design settling.
**Confidence:** 70%
**Complexity:** High
**Status:** Unexplored

### 7. Message Contract Verifier
**Description:** Serialization round-trip + golden snapshot schema compatibility tests.
**Rationale:** Schema drift is silent data loss in multi-version NuGet distribution.
**Downsides:** Golden snapshot maintenance adds friction.
**Confidence:** 70%
**Complexity:** Low-Medium
**Status:** Unexplored

## Rejection Summary

| # | Idea | Reason Rejected |
|---|------|-----------------|
| 1 | Topic Routing Verifier | Static analysis utility, not a test harness feature |
| 2 | Message Timeline/Ledger Recorder | Subsumed by ITestHarness observable collections |
| 3 | Message Ordering Assertion DSL | Folds into awaitable assertion DSL |
| 4 | Consumer Named Scope Factories | Over-engineered; DI scope inspection rarely needed |
| 5 | Correlation Trace Collector | Query-by-CorrelationId is a feature of observable collections |
| 6 | Overlay Harness for Real Brokers | Too ambitious for v1; Testcontainers covers this |
| 7 | Consumer Latency Budget Assertions | Performance testing != correctness testing |
| 8 | Pull-Mode Test Double | Inverts programming model; tests diverge from production |
| 9 | Backpressure Simulator | Load testing != test harness |
| 10 | Scoped Topic Namespace | Implementation detail of isolation layer |

## Session Log
- 2026-03-19: Initial ideation — 40 raw candidates from 5 agents, 22 unique after dedupe, 7 survivors. Idea #1 selected for brainstorm.
