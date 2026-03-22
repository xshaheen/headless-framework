---
status: pending
priority: p1
issue_id: "034"
tags: ["code-review","async","correctness"]
dependencies: []
---

# Fix ValueTask.DisposeAsync() discarded inside lock in AddClientAsync

## Problem Statement

GroupHandle.AddClientAsync (IConsumerRegister.cs:664) fires and forgets client.DisposeAsync() with '_ = client.DisposeAsync()' because you cannot await inside a lock. However, ValueTask must be consumed. PulsarConsumerClient and AzureServiceBusConsumerClient have real async dispose operations — the lost ValueTask means disposal may not complete, leaking connections.

## Findings

- **Location:** src/Headless.Messaging.Core/Internal/IConsumerRegister.cs:664
- **Risk:** High — leaked connections from incomplete async disposal (Pulsar, ASB)
- **Discovered by:** strict-dotnet-reviewer
- **Suppressed warning:** CA2012 — Use ValueTasks correctly

## Proposed Solutions

### Extract client outside lock, await after
- **Pros**: Correct, no leaked ValueTask, minimal restructuring
- **Cons**: Slightly different control flow
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Extract the client to dispose into a local variable, exit the lock, then await client.DisposeAsync() outside the lock. Remove the CA2012 suppression.

## Acceptance Criteria

- [ ] DisposeAsync() is awaited, not discarded
- [ ] Lock is not held during async disposal
- [ ] CA2012 suppression removed
- [ ] Pulsar and ASB transports properly cleaned up on dispose-during-add

## Notes

Pattern: capture what to dispose under lock, release lock, then await dispose.

## Work Log

### 2026-03-22 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
