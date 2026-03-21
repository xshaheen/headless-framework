---
status: ready
priority: p2
issue_id: "098"
tags: ["code-review","dotnet","messaging"]
dependencies: []
---

# Fix GroupHandle.Clients public property bypasses _clientsLock

## Problem Statement

GroupHandle.Clients is a public required List<IConsumerClient> property. AddClient and SnapshotClients correctly use _clientsLock, but nothing prevents a caller from calling handle.Clients.Add(client) directly, bypassing the lock entirely. This is a thread-safety bug waiting for future code to trigger.

## Findings

- **Location:** src/Headless.Messaging.Core/Internal/IConsumerRegister.cs (GroupHandle class, Clients property)
- **Risk:** Lock bypass on mutable list — thread-safety violation possible in future code
- **Discovered by:** pragmatic-dotnet-reviewer

## Proposed Solutions

### Make Clients private, expose only via AddClient and SnapshotClients
- **Pros**: Enforces lock discipline
- **Cons**: Minor refactor
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Change `public required List<IConsumerClient> Clients { get; init; }` to `private readonly List<IConsumerClient> _clients = []` and update all existing access to use AddClient/SnapshotClients.

## Acceptance Criteria

- [ ] No public access to the underlying client list
- [ ] All access goes through AddClient/SnapshotClients
- [ ] Init-only or constructor-set internal list

## Notes

PR #194.

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-21 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
