# Headless.Coordination.Core

Implements the provider-agnostic membership engine over an `IMembershipStore`.

## Problem Solved

Provides registration, heartbeat, event derivation, fail-stop self-loss, and ordered liveness reads without binding to a storage backend.

## Key Features

- An internal membership service implements `INodeMembership` (consumers resolve `INodeMembership`).
- `IsAliveAsync(identity)` is a targeted single-node check backed by the store SPI's `ReadNodeLivenessAsync` — one guarded single-row query (or small Lua), not a full cluster snapshot, so per-request liveness checks stay O(1) instead of scaling with cluster size. It returns the same result the snapshot would for that identity (current-generation-only, store-clock classified, retention-bounded).
- Background heartbeat service derives lifecycle events from authoritative snapshots, leaves gracefully on host shutdown under a bounded timeout, and stops beating once local membership is lost.
- Bounded per-subscriber event channels isolate slow consumers from heartbeats.
- Default node-id provider resolves configured id, Kubernetes pod identity, hostname, then generated id.

## Design Notes

`RegisterAsync` durably establishes both the cold descriptor and an initial store-clock liveness entry in one guarded write, so a node is `Alive` (and its role/metadata are visible) immediately after register — without waiting for the first heartbeat. The background loop owns every subsequent beat. Registration is incarnation-guarded: a stale or superseded incarnation establishes no liveness.

Self-heartbeat rejection is a local fencing failure. The default `MembershipLostBehavior.StopApplication` asks the host to stop; `StopMembershipOnly` is for hosts that explicitly quiesce every worker.

Core also hosts the shared dead-owner recovery bridge — a generic `BackgroundService` parameterized by an `IDeadOwnerReclaimer` that reclaims dead-incarnation resources on `NodeLeft` events plus a periodic `Dead`-only snapshot reconcile (idempotent dedup, `CancellationToken.None` writes). It is internal infrastructure consumed by registering a closed generic from the owning assembly (Jobs, Messaging) via `InternalsVisibleTo`; each closed type yields a distinct hosted service and logger category. Coordination.Core does not register it — the consuming feature does.

## Installation

```bash
dotnet add package Headless.Coordination.Core
```

## Quick Start

```csharp
services.AddCoordinationCore<MyMembershipStore>(options =>
{
    options.ClusterName = "orders";
});
```

Applications normally use a provider package and call `AddHeadlessCoordination(setup => setup.Use...)`; `AddCoordinationCore<TStore>` is the lower-level hook for provider authors and custom stores.

## Provider SPI

Custom `IMembershipStore` implementations must provide `ReadNodeLivenessAsync(NodeIdentity, CancellationToken) -> NodeLivenessState?` alongside the cluster-wide `ReadLivenessAsync`. It returns the classified state of a single identity, or `null` when that identity is absent from the current-generation view. The contract: it MUST equal what `ReadLivenessAsync` would conclude for that identity — current-generation-only, store-clock classified, and retention-bounded so a row at/beyond the retention window reads as `null`, not `Dead` — and it MUST be read-only (no pruning, no generation-mirror backfill) so `IsAliveAsync` stays a bounded single-row read. There is no default implementation; this method is a required part of the SPI, so existing custom stores must add it.

## Configuration

Set `HeartbeatInterval < SuspicionThreshold < DeadThreshold`; `DeadRetentionWindow` must be at least two heartbeat intervals.

## Dependencies

- `Headless.Coordination.Abstractions`
- `Headless.Checks`
- `Headless.Core`
- `Headless.Extensions`
- `Headless.Hosting`
- `FluentValidation`
- `Microsoft.Extensions.Configuration.Abstractions`
- `Microsoft.Extensions.Hosting.Abstractions`
- `Microsoft.Extensions.Logging.Abstractions`

## Side Effects

Registers `TimeProvider.System`, framework GUID generator defaults, `INodeIdProvider`, `INodeMembership`, `IMembershipEventSource`, and the heartbeat hosted service.
