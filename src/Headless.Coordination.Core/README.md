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

## Configuration

Set `HeartbeatInterval < SuspicionThreshold < DeadThreshold`; `DeadRetentionWindow` must be at least two heartbeat intervals.

## Dependencies

- `Headless.Coordination.Abstractions`
- `Headless.Checks`
- `Headless.Core`
- `Headless.Extensions`
- `Headless.Hosting`
- `Microsoft.Extensions.Configuration.Abstractions`
- `Microsoft.Extensions.Hosting.Abstractions`
- `Microsoft.Extensions.Logging.Abstractions`

## Side Effects

Registers `TimeProvider.System`, framework GUID generator defaults, `INodeIdProvider`, `INodeMembership`, `IMembershipEventSource`, and the heartbeat hosted service.
