# Headless.Coordination.Core

Implements the provider-agnostic membership engine over an `IMembershipStore`.

## Problem Solved

Provides registration, heartbeat, event derivation, fail-stop self-loss, and ordered liveness reads without binding to a storage backend.

## Key Features

- `MembershipService` implements `INodeMembership`.
- Background heartbeat service derives lifecycle events from authoritative snapshots.
- Bounded per-subscriber event channels isolate slow consumers from heartbeats.
- Default node-id provider resolves configured id, Kubernetes pod identity, hostname, then generated id.

## Design Notes

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

## Configuration

Set `HeartbeatInterval < SuspicionThreshold < DeadThreshold`; `DeadRetentionWindow` must be at least two heartbeat intervals.

## Dependencies

- `Headless.Coordination.Abstractions`
- `Headless.Core`
- `Headless.Extensions`
- `Headless.Hosting`
- `Microsoft.Extensions.Configuration.Abstractions`
- `Microsoft.Extensions.Hosting.Abstractions`
- `Microsoft.Extensions.Logging.Abstractions`

## Side Effects

Registers `TimeProvider.System`, framework GUID generator defaults, `INodeIdProvider`, `INodeMembership`, `IMembershipEventSource`, `ProviderCapabilities`, and the heartbeat hosted service.
