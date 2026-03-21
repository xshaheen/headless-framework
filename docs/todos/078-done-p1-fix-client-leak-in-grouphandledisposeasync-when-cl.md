---
status: done
priority: p1
issue_id: "078"
tags: ["code-review","dotnet","messaging"]
dependencies: []
---

# Fix client leak in GroupHandle.DisposeAsync when client added after SnapshotClients

## Problem Statement

GroupHandle.DisposeAsync calls Cts.CancelAsync() then SnapshotClients() and iterates the snapshot calling DisposeAsync() on each. During the async iteration gap, a Task.Factory.StartNew lambda inside ExecuteAsync could race past the CTS check and call AddClient after the snapshot was taken. That client would never be disposed. The ordering dependency (CTS cancellation → AddClient guard) is not enforced by any synchronization.

## Findings

- **Location:** src/Headless.Messaging.Core/Internal/IConsumerRegister.cs (GroupHandle.DisposeAsync ~line 3817)
- **Risk:** Resource leak — IConsumerClient not disposed on shutdown
- **Discovered by:** strict-dotnet-reviewer

## Proposed Solutions

### Lock and drain Clients inside DisposeAsync after cancellation
- **Pros**: Guarantees all clients disposed
- **Cons**: Slightly more complex dispose logic
- **Effort**: Small
- **Risk**: Low

### Re-snapshot after all Task.Run lambdas complete (await task completion)
- **Pros**: Clean ordering
- **Cons**: Requires tracking all running tasks
- **Effort**: Medium
- **Risk**: Medium


## Recommended Action

After CancelAsync, take a second snapshot under _clientsLock at the end of DisposeAsync to catch any stragglers added during the async iteration gap. Or set a _disposing flag under _clientsLock so AddClient can reject new clients atomically with the snapshot.

## Acceptance Criteria

- [ ] No IConsumerClient added after CancelAsync escapes DisposeAsync without being disposed
- [ ] Test added: client added concurrently with DisposeAsync is either rejected or disposed

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

### 2026-03-21 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
