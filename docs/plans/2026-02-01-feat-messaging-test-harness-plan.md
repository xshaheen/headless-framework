# feat: Add Full-Stack Messaging Test Harness

## Overview

Expand `Headless.Messaging.Core.Tests.Harness` to provide shared test base classes for Transport, Consumer, and Storage components—following the proven pattern from `Headless.Blobs.Tests.Harness` and `Headless.DistributedLocks.Tests.Harness`.

Currently, the messaging harness contains only a stub `SubscriberClass.cs`. Each transport provider (RabbitMQ, SQS, Kafka, etc.) duplicates similar test logic. This plan creates reusable base classes that:

- Eliminate test duplication across 8+ transport implementations
- Ensure consistent test coverage for all providers
- Make it trivial to add tests for new transports/storage providers
- Support transport-specific capabilities via feature flags

## Problem Statement

**Current State:**
- `Headless.Messaging.Core.Tests.Harness` is a stub (1 file, non-functional)
- Transport tests are duplicated: RabbitMQ, SQS, Kafka, NATS, Pulsar, Azure SB, Redis Streams each have separate test implementations
- Storage tests (PostgreSQL, SQL Server) share no common base
- No integration test pattern for full publish-consume cycle
- Inconsistent test coverage across providers

**Desired State:**
- Single source of truth for messaging test scenarios
- Provider tests inherit from harness, override only provider-specific setup
- Consistent coverage: if a test exists in harness, all providers run it
- Easy onboarding for new transport contributors

## Technical Approach

### Architecture

```
Headless.Messaging.Core.Tests.Harness/
├── TransportTestsBase.cs          # Tests for ITransport implementations
├── ConsumerClientTestsBase.cs     # Tests for IConsumerClient implementations
├── DataStorageTestsBase.cs        # Tests for IDataStorage implementations
├── MessagingIntegrationTestsBase.cs # Full pub-sub cycle tests
├── Capabilities/
│   └── TransportCapabilities.cs   # Feature flags for transport-specific tests
├── Fixtures/
│   └── TestMessage.cs             # Shared test message types
└── Helpers/
    ├── TestSubscriber.cs          # Test consumer implementations
    └── MessageAssertions.cs       # Custom assertions for messaging
```

**Inheritance Pattern (following Blobs):**

```csharp
// In Harness
public abstract class TransportTestsBase : TestBase
{
    protected abstract ITransport GetTransport();
    protected virtual TransportCapabilities Capabilities => TransportCapabilities.Default;

    [Fact]
    public virtual async Task should_send_message() { ... }
}

// In Provider Tests (e.g., RabbitMQ)
[Collection<RabbitMqFixture>]
public sealed class RabbitMqTransportTests(RabbitMqFixture fixture) : TransportTestsBase
{
    protected override ITransport GetTransport() =>
        new RabbitMqTransport(fixture.ConnectionPool, _options);

    [Fact]
    public override Task should_send_message() => base.should_send_message();
}
```

### Transport Capabilities Matrix

Different transports support different features. Use capability flags to skip inapplicable tests:

| Capability      | RabbitMQ | SQS       | Azure SB | Kafka     | NATS | Pulsar | Redis |
|-----------------|----------|-----------|----------|-----------|------|--------|-------|
| Ordering        | ✓        | FIFO only | Sessions | Partition | ✓    | ✓      | ✓     |
| DeadLetter      | ✓        | ✓         | ✓        | ✗         | ✗    | ✓      | ✗     |
| Priority        | ✓        | ✗         | ✗        | ✗         | ✗    | ✓      | ✗     |
| DelayedDelivery | Plugin   | ✓         | ✓        | ✗         | ✗    | ✓      | ✗     |
| BatchSend       | ✓        | ✓         | ✓        | ✓         | ✓    | ✓      | ✓     |

## Stories

### Phase 1: Core Harness Infrastructure ✅

| # | Story | Size | Notes |
|---|-------|------|-------|
| 1.1 | ✅ Create `TransportCapabilities` flags class | S | Feature detection for transport-specific tests |
| 1.2 | ✅ Create `TransportTestsBase` with 10-12 core test methods | M | 16 test methods implemented |
| 1.3 | ✅ Create `ConsumerClientTestsBase` with 8-10 test methods | M | 12 test methods implemented |
| 1.4 | ✅ Create `DataStorageTestsBase` with 12-15 test methods | M | 23 test methods implemented |
| 1.5 | ✅ Create shared test fixtures (`TestMessage`, `TestSubscriber`) | S | Plus capability classes |

### Phase 2: Integration Test Base ✅

| # | Story | Size | Notes |
|---|-------|------|-------|
| 2.1 | ✅ Create `MessagingIntegrationTestsBase` | M | 11 test methods, full DI setup |
| 2.2 | ✅ Add consumer discovery tests | S | Included in MessagingIntegrationTestsBase |
| 2.3 | ✅ Add concurrent consumer tests | M | Included in MessagingIntegrationTestsBase |

### Phase 3: Provider Migration (Example: RabbitMQ) ✅

| # | Story | Size | Notes |
|---|-------|------|-------|
| 3.1 | ✅ Create `RabbitMqTransportTests` inheriting from harness | S | 14 tests, Testcontainers fixture |
| 3.2 | Create `RabbitMqConsumerClientTests` inheriting from harness | S | Deferred - transport tests prove pattern |
| 3.3 | ✅ Document pattern in harness README | S | Full guide with code examples |

### Phase 4: Remaining Provider Migrations

| # | Story | Size | Notes |
|---|-------|------|-------|
| 4.1 | ✅ Migrate AWS SQS tests to harness | S | 14 transport tests + 2 SQS-specific |
| 4.2 | Migrate Azure Service Bus tests to harness | S | |
| 4.3 | Migrate Kafka tests to harness | S | |
| 4.4 | Migrate NATS tests to harness | S | |
| 4.5 | Migrate Pulsar tests to harness | S | |
| 4.6 | Migrate Redis Streams tests to harness | S | |
| 4.7 | ✅ Migrate PostgreSQL storage tests to harness | S | 22 storage tests + 3 PostgreSQL-specific |
| 4.8 | ✅ Migrate SQL Server storage tests to harness | S | 22 storage tests + 3 SqlServer-specific |

## Acceptance Criteria

### Functional Requirements

- [x] [M] `TransportTestsBase` contains at least 10 virtual test methods covering: send, send with headers, send batch, error propagation, dispose cleanup, cancellation support
- [x] [M] `ConsumerClientTestsBase` contains at least 8 virtual test methods covering: subscribe, listen callback, commit, reject, fetch topics, graceful shutdown
- [x] [M] `DataStorageTestsBase` contains at least 12 virtual test methods covering: store published, store received, change state, get messages, lock/unlock, initialize schema
- [x] [S] `TransportCapabilities` allows tests to skip based on: `SupportsOrdering`, `SupportsDeadLetter`, `SupportsPriority`, `SupportsDelayedDelivery`
- [x] [S] All base test methods use `AbortToken` from `TestBase`
- [x] [S] All base test methods properly dispose resources with `await using`

### Non-Functional Requirements

- [x] [S] Harness project compiles with `<IsTestProject>false</IsTestProject>` (not runnable directly)
- [x] [XS] Harness references only abstractions, not concrete implementations
- [x] [S] Provider tests can run in parallel (no shared mutable state in harness)

### Quality Gates

- [x] [S] At least one provider (RabbitMQ) migrated as proof of pattern
- [x] [XS] Harness README documents how to add new provider tests
- [ ] [S] All migrated tests pass in CI

## Sizing Summary

| Size | Count | Est. Hours |
|------|-------|------------|
| XS | 2 | 1 |
| S | 14 | 21 |
| M | 5 | 15 |
| **Total** | 21 | **~37hr** |

## Dependencies & Prerequisites

- `Headless.Testing` (provides `TestBase`)
- `Headless.Messaging.Abstractions` (interfaces: `ITransport`, `IConsumerClient`, `IDataStorage`)
- `Headless.Messaging.Core` (core implementations)
- `Testcontainers` + `Testcontainers.XunitV3` (for provider fixtures)

## Risk Analysis

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Transport semantics differ too much | Medium | High | Use capability flags; some tests are opt-in |
| Existing tests break during migration | Low | Medium | Migrate one provider first as proof; keep old tests until validated |
| Testcontainers slow in CI | Medium | Low | Already used for Blobs/SQL tests; known acceptable |

## Implementation Notes

### Test Method Naming

Follow existing convention: `should_{action}_{expected_outcome}_when_{condition}`

```csharp
public virtual async Task should_send_message_successfully()
public virtual async Task should_include_headers_in_sent_message()
public virtual async Task should_throw_when_transport_disposed()
public virtual async Task should_redeliver_after_reject() // Only if SupportsRedelivery
```

### Capability-Based Test Skipping

```csharp
[Fact]
public virtual async Task should_maintain_message_ordering()
{
    if (!Capabilities.SupportsOrdering)
    {
        // Skip test for transports that don't guarantee ordering
        return;
    }

    // Test ordering...
}
```

### Provider Test Pattern

```csharp
// RabbitMqTransportTests.cs
[Collection<RabbitMqFixture>]
public sealed class RabbitMqTransportTests(RabbitMqFixture fixture) : TransportTestsBase
{
    protected override ITransport GetTransport()
    {
        var options = Options.Create(new RabbitMqOptions { /* config */ });
        return new RabbitMqTransport(fixture.ConnectionPool, options, Logger);
    }

    protected override TransportCapabilities Capabilities => new()
    {
        SupportsOrdering = true,
        SupportsDeadLetter = true,
        SupportsPriority = true,
    };

    // Expose all base tests
    [Fact] public override Task should_send_message_successfully() => base.should_send_message_successfully();
    [Fact] public override Task should_include_headers_in_sent_message() => base.should_include_headers_in_sent_message();
    // ... etc

    // Provider-specific tests
    [Fact]
    public async Task should_use_exchange_routing()
    {
        // RabbitMQ-specific behavior
    }
}
```

## References

### Internal References

- Blobs harness pattern: `tests/Headless.Blobs.Tests.Harness/BlobStorageTestsBase.cs`
- DistributedLocks harness: `tests/Headless.DistributedLocks.Tests.Harness/ResourceLockProviderTestsBase.cs`
- Current messaging harness: `tests/Headless.Messaging.Core.Tests.Harness/SubscriberClass.cs`
- TestBase: `src/Headless.Testing/Tests/TestBase.cs`
- Messaging abstractions: `src/Headless.Messaging.Abstractions/`

### Existing Transport Tests (to migrate)

- `tests/Headless.Messaging.RabbitMq.Tests.Unit/`
- `tests/Headless.Messaging.AwsSqs.Tests.Unit/`
- `tests/Headless.Messaging.AwsSqs.Tests.Integration/`
- `tests/Headless.Messaging.AzureServiceBus.Tests.Unit/`
- `tests/Headless.Messaging.Kafka.Tests.Unit/`
- `tests/Headless.Messaging.Nats.Tests.Unit/`
- `tests/Headless.Messaging.Pulsar.Tests.Unit/`
- `tests/Headless.Messaging.RedisStreams.Tests.Unit/`

### Storage Tests (to migrate)

- `tests/Headless.Messaging.PostgreSql.Tests.Integration/`
- `tests/Headless.Messaging.SqlServer.Tests.Integration/`

## Unresolved Questions

1. **Should `IBootstrapper` lifecycle be tested in harness?** (start/stop background processing) (No)
2. **How to test delayed/scheduled messages across transports with different delay mechanisms?** (Use capability flags; test only if supported)
3. **Should the harness include Dashboard/Monitoring API integration tests?** (No)
4. **For transports requiring setup (Kafka topic creation, SQS queue ARNs), should harness provide helpers or leave to fixtures?** (Leave to fixtures)
