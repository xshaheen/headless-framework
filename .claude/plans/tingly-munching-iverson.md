# Migration: Headless.Messaging.Nats from NATS.Client v1 → NATS.Net v2

## Context

NATS.Client v1 (`nats-io/nats.net.v1`) is archived and in maintenance-only mode. NATS.Net v2 (`nats-io/nats.net`) is a ground-up rewrite based on AlterNats with zero-copy memory, native async, pull-only JetStream consumers, and built-in connection pooling. The migration eliminates several P0/P1 bugs in the current implementation (async void handler, blocking ListeningAsync, shared mutable Options) by design.

## Package Change

- **Remove:** `NATS.Client` from `Directory.Packages.props`
- **Add:** `NATS.Net` (meta-package pulling `NATS.Client.Core` + `NATS.Client.JetStream`)
- **Update:** `Headless.Messaging.Nats.csproj` PackageReference

## Breaking Changes (Public API)

| Property | v1 | v2 |
|---|---|---|
| `MessagingNatsOptions.Options` | `NATS.Client.Options?` | **Removed** → `Func<NatsOpts, NatsOpts>? ConfigureConnection` |
| `.StreamOptions` | `Action<StreamConfigurationBuilder>?` | `Func<StreamConfig, StreamConfig>?` |
| `.ConsumerOptions` | `Action<ConsumerConfigurationBuilder>?` | `Func<ConsumerConfig, ConsumerConfig>?` |
| `.CustomHeadersBuilder` | `Func<MsgHandlerEventArgs, ...>` | `Func<NatsJSMsg<ReadOnlyMemory<byte>>, ...>` |
| `INatsConnectionPool` | `RentConnection()` / `Return()` | `GetConnection()` (no return needed) |
| `NatsConnectionPool` | `IDisposable` | `IAsyncDisposable` |

## File-by-File Plan

### 1. `MessagingNatsOptions.cs` — Breaking API changes

- Remove `Options` property (v1 `NATS.Client.Options`)
- Add `Func<NatsOpts, NatsOpts>? ConfigureConnection` — NatsOpts is a record, needs `with` pattern
- Change `StreamOptions` to `Func<StreamConfig, StreamConfig>?`
- Change `ConsumerOptions` to `Func<ConsumerConfig, ConsumerConfig>?`
- Change `CustomHeadersBuilder` parameter type from `MsgHandlerEventArgs` to `NatsJSMsg<ReadOnlyMemory<byte>>`
- Add `internal NatsOpts BuildNatsOpts()` helper
- Update `MessagingNatsOptionsValidator` accordingly

### 2. `INatsConnectionPool.cs` — Simplify to v2 pool wrapper

- `INatsConnectionPool`: replace `RentConnection()`/`Return()` with `GetConnection()` returning `NatsConnection`, add `IAsyncDisposable`
- `NatsConnectionPool`: wrap v2's built-in `NATS.Client.Core.NatsConnectionPool` (round-robin multiplexing, no manual return)
- Delete all CAS logic, `ConcurrentQueue`, `_pCount`, `_disposed` tracking — v2 handles it

### 3. `NatsTransport.cs` — Publisher rewrite

- `GetConnection()` (no return needed, v2 pool is stateless round-robin)
- `new NatsJSContext(connection)` per publish (lightweight, just wraps connection ref)
- `NatsHeaders` for headers, `NatsRawSerializer<ReadOnlyMemory<byte>>.Default` for zero-copy body
- `NatsJSPubOpts { MsgId = ... }` for dedup
- `ack.EnsureSuccess()` replaces `resp.Seq > 0` check
- Native `CancellationToken` support

### 4. `NatsConsumerClient.cs` — Major rewrite (push → pull)

**Architecture change:** Push callbacks + blocking wait → Pull-based `ConsumeAsync` IAsyncEnumerable loop

- **Connection:** `NatsConnection` created directly (not from pool), `NatsJSContext` created once
- **`ConnectAsync()`:** replaces sync `Connect()`, creates connection + JS context
- **`FetchTopicsAsync()`:** `CreateStreamAsync`/`UpdateStreamAsync` with `StreamConfig` records. Catch `NatsJSApiException` (404) for create-if-not-exists pattern
- **`SubscribeAsync()`:** just stores topics (no subscription yet)
- **`ListeningAsync()`:** THE BIG CHANGE — spawns `_ConsumeSubjectAsync` tasks per subject, each running a resilient `ConsumeAsync` loop with retry
- **`_ConsumeSubjectAsync()`:** `CreateOrUpdateConsumerAsync` + `consumer.ConsumeAsync<ReadOnlyMemory<byte>>()` in a while loop with error recovery
- **Message processing:** inline in the consume loop via `_ProcessMessageAsync()`, uses semaphore for concurrency (same pattern)
- **`CommitAsync`/`RejectAsync`:** `msg.AckAsync()` / `msg.NakAsync()` — now truly async. Sender is `NatsJSMsg<ReadOnlyMemory<byte>>` (struct, boxed to `object?`)
- **`PauseAsync`/`ResumeAsync`:** gate blocks inside the consume loop at `WaitIfPausedAsync` — simpler than v1 (no unsubscribe/resubscribe needed)
- **`DisposeAsync`:** dispose `NatsConnection` (async)

**Eliminated:** `async void` handler, `_subscriptions` list, `_UnsubscribeWithoutDrain`, `_DrainSubscriptions`, `WaitHandle.WaitOne` blocking, `_connectionLock`

### 5. `NatsConsumerClientFactory.cs` — Minor

- `Connect()` → `await client.ConnectAsync()`

### 6. `Setup.cs` — Minor

- Update DI registrations (same structure, `IAsyncDisposable`-aware)

### 7. Unit Tests — Full rewrite

All 6 test files rewrite since v1 types (`IConnection`, `Msg`, `IJetStream`, `MsgHandlerEventArgs`) no longer exist.

- `NatsTransportTests.cs`: mock `INatsConnectionPool.GetConnection()`. Note: `NatsJSContext` is concrete — may need thinner tests + integration coverage
- `NatsConsumerClientTests.cs`: remove async void bug test (fixed by design), test `CommitAsync`/`RejectAsync` with `NatsJSMsg<ReadOnlyMemory<byte>>`, test pause/resume
- `NatsConnectionPoolTests.cs`: thin tests verifying wrapper delegates to v2 pool
- `NatsConsumerClientFactoryTests.cs`: `CreateAsync` now awaits `ConnectAsync()`
- `MessagingNatsOptionsTests.cs`: update for new Func-based callback types, `BuildNatsOpts()` helper
- `SetupTests.cs`: likely minimal changes

## Implementation Order

1. Update package references (`Directory.Packages.props`, `.csproj`)
2. `MessagingNatsOptions.cs` — new types, validator
3. `INatsConnectionPool.cs` — simplified pool wrapper
4. `NatsTransport.cs` — publisher
5. `NatsConsumerClient.cs` — consumer (largest change)
6. `NatsConsumerClientFactory.cs` — async connect
7. `Setup.cs` — DI
8. All unit tests

## Open Questions

1. **`StreamConfig`/`ConsumerConfig` exact property names** — verify against NATS.Net v2 API at implementation time (use Context7). Names like `StreamConfigStorage.Memory`, `ConsumerConfigDeliverPolicy.New` are approximate.
2. **`NatsConnectionPool` constructor** — verify exact signature: `new NatsConnectionPool(poolSize, opts)` or `new NatsConnectionPool(opts, poolSize)`
3. **Unit testability** — `NatsConnection` and `NatsJSContext` are concrete classes (no interfaces). Accept thinner unit tests + integration coverage rather than introducing wrapper interfaces.

## Verification

1. `dotnet build` — 0 errors
2. `dotnet test` — all unit tests pass
3. Manual integration test with NATS Docker container (if available)
