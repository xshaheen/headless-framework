# Headless.Coordination.Abstractions

Defines the public coordination contract: `node@incarnation` identity, membership operations, liveness snapshots, lifecycle events, options, and exceptions.

## Problem Solved

Gives consumers a provider-independent way to stamp process incarnation identity on their own rows and read which node identities are alive.

## Key Features

- `NodeIdentity`, `NodeId`, and `NodeIncarnation`.
- `INodeMembership` for register, heartbeat, leave, live reads, snapshot reads, and event watch.
- Heartbeats are incarnation-fenced: an incarnation that is dead, gracefully left, or pruned is terminal and cannot be revived; the process must register a higher incarnation.
- `NodeJoined`, `NodeSuspected`, `NodeRecovered`, `NodeLeft`, and `LocalMembershipLost`.
- `IDeadOwnerReclaimer` — the per-domain reclaim sink driven by the shared dead-owner recovery bridge (carries `ReconcileInterval` and `ReclaimAsync(owners, ct)`, where `owners` is a single owner from the event path or the whole dead set from a reconcile tick so a consumer can collapse the reclaim into one batched write); implemented by each consumer (Jobs, Messaging).
- `CoordinationOptions` for thresholds, cluster name, node id, role, metadata, and membership-loss behavior.

## Design Notes

Coordination is not an ownership ledger or consensus system. Consumers own their domain rows, stamp `NodeIdentity`, and reconcile periodically against `GetLiveNodesAsync()`.

`HeartbeatAsync()` returns `false` when the local incarnation has been superseded or is terminal. Callers must stop ownership-sensitive work and re-register rather than attempting to resurrect that identity.

## Installation

```bash
dotnet add package Headless.Coordination.Abstractions
```

## Quick Start

```csharp
public sealed class Worker(INodeMembership membership)
{
    public async Task StartAsync(CancellationToken ct)
    {
        var identity = await membership.RegisterAsync(ct);
        // Stamp identity on work rows owned by this process.
    }
}
```

## Configuration

Configure through provider setup plus `CoordinationOptions`.

## Dependencies

- `Headless.Checks`

## Side Effects

None.
