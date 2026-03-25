---
status: done
priority: p1
issue_id: "002"
tags: ["code-review","dotnet","build","quality"]
dependencies: []
---

# Fix Pulsar client disposal compile error

## Problem Statement

The Pulsar transport now calls `DisposeAsync()` on `PulsarClient`, but the referenced client type does not expose that API. This currently breaks solution builds and any package that depends on Headless.Messaging.Pulsar.

## Findings

- **Location:** src/Headless.Messaging.Pulsar/IConnectionFactory.cs:57-60
- **Evidence:** dotnet build headless-framework.slnx -c Release fails with CS1061 on PulsarClient.DisposeAsync
- **Discovered by:** code review

## Proposed Solutions

### Use the supported disposal API
- **Pros**: Matches actual library contract
- **Cons**: May require sync disposal path
- **Effort**: Small
- **Risk**: Low

### Wrap client lifetime behind an adapter
- **Pros**: Encapsulates version-specific disposal differences
- **Cons**: Adds indirection
- **Effort**: Medium
- **Risk**: Low


## Recommended Action

Switch to the supported PulsarClient disposal method and verify the Pulsar project builds again.

## Acceptance Criteria

- [x] ConnectionFactory compiles against the current Pulsar client package
- [x] dotnet build headless-framework.slnx -c Release no longer reports CS1061 for PulsarClient
- [x] Pulsar unit tests run successfully

## Notes

Review of branch xshaheen/review-transports on 2026-03-25.

## Work Log

### 2026-03-25 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-25 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-03-25 - Implemented

**By:** Agent
**Actions:**
- Switched Pulsar client disposal to the supported `CloseAsync()` API after verifying the referenced package surface
- Verified with solution build and Pulsar unit tests

### 2026-03-25 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
