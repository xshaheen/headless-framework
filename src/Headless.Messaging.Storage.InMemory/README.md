# Headless.Messaging.Storage.InMemory

In-memory outbox storage for testing and development.

## Problem Solved

Provides ephemeral message storage without database dependencies for local development, integration tests, and prototyping.

## Key Features

- `IMessageRevocationStorage` atomically deletes a scheduled row before reservation, fenced by storage version, terminal status, and retry state. Claimed but unreserved rows remain revocable; deleted rows cannot be restored by reservation or shutdown flush.
- **Zero Dependencies**: No database required
- **Fast**: In-memory operations
- **Testing**: Deterministic behavior for tests
- **Full API**: Complete outbox storage implementation
- **Intent-Aware Identity**: Mirrors durable providers by storing bus/queue intent and including it in received-message de-duplication
- **Monitoring**: In-memory dashboard data

InMemoryStorage uses its injected `TimeProvider` for both application-scheduled `NextRetryAt` and authoritative lease ownership. It implements the same duration-based lease SPI and returns the persisted `(LockedUntil, Owner)` identity. Delayed scheduling atomically transitions and leases each per-message winner before returning a deterministic bounded batch. Circuit-open received retries atomically advance `NextRetryAt` and clear only the exact live `(lane, Owner, LockedUntil)` lease generation under the per-row lock. Retry pickup claims due rows in `NextRetryAt` order, as the relational providers do, so an earlier-scheduled row is never starved by a later one once `RetryBatchSize` bounds the batch. Rows sharing an identical `NextRetryAt` fall back to a deterministic per-provider tie-break, which no fairness guarantee depends on.

Inbox lease release and retry deferral also require the complete stored inbox attempt fence, including generation, incarnation, and attempt ID. Missing or mismatched fences leave the row unchanged. Admission uses the same identity validation as PostgreSQL and SQL Server: nonblank contract names, consumer identities, and message IDs up to 200 characters; contract versions up to 100 characters; nonnegative generations; and nonblank tenant IDs up to 200 characters. Blank tenant headers normalize to no tenant.

Inline inbox reservations verify the complete active attempt fence before advancing the counter and preserve its attempt ID within the lease. Acquiring a fresh lease or claiming a due retry allocates a new attempt ID.

Its inbox tier is `ProcessLocal`: duplicate suppression, 30-day default terminal retention, per-consumer `InboxRetention(...)`, replay provenance, holds, and recovery state disappear on process restart. Expiry or purge resets the deduplication identity.

Direct admission suppresses duplicates while its root is retained. After that root expires or is purged, a new admission starts a fresh lifecycle, even when older replay descendants remain held. Replay generations increment within their own lifecycle and retain their parent incarnation; they cannot collide with a new lifecycle or an explicitly admitted generation. Holds and operation receipts continue to identify exact incarnations.

## Installation

```bash
dotnet add package Headless.Messaging.Storage.InMemory
```

## Quick Start

```csharp
builder.Services.AddHeadlessMessaging(options =>
{
    options.Bus.ForMessage<OrderPlaced>(message =>
        message.Consumer<OrderPlacedConsumer>(consumer =>
            consumer.ConsumerIdentity("orders.order-placed")
        )
    );
    options.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.ProcessLocal;
    options.UseInMemoryStorage();
    options.UseRabbitMq(config);
});
```

## Configuration

No configuration required. Just call `UseInMemoryStorage()`.

## Dependencies

- `Headless.Messaging.Core`

## Side Effects

None. All messages are stored in memory and lost on restart. Not suitable for production.
