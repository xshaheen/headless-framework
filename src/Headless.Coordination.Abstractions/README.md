# Headless.Coordination.Abstractions

Defines the public coordination contract: `node@incarnation` identity, membership operations, liveness snapshots, lifecycle events, options, and exceptions.

## Problem Solved

Gives consumers a provider-independent way to stamp process incarnation identity on their own rows and read which node identities are alive.

## Key Features

- `NodeIdentity`, `NodeId`, and `NodeIncarnation`.
- `INodeMembership` for register, heartbeat, leave, live reads, snapshot reads, and event watch.
- `NodeJoined`, `NodeSuspected`, `NodeRecovered`, `NodeLeft`, and `LocalMembershipLost`.
- `CoordinationOptions` for thresholds, cluster name, node id, role, metadata, and membership-loss behavior.

## Design Notes

Coordination is not an ownership ledger or consensus system. Consumers own their domain rows, stamp `NodeIdentity`, and reconcile periodically against `GetLiveNodesAsync()`.

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
- `FluentValidation`

## Side Effects

None.
