---
status: pending
priority: p3
issue_id: "004"
tags: ["code-review","security"]
dependencies: []
---

# Guard Type.GetType in RecordingTransport against assembly-qualified names

## Problem Statement

Type.GetType(messageTypeName) on a header-controlled string could resolve arbitrary types if Headers.Type is ever changed to store assembly-qualified names. Currently the header stores short names so this is effectively dead code, but it's a latent type confusion gadget.

## Findings

- **Location:** src/Headless.Messaging.Testing/Internal/RecordingTransport.cs:27-34
- **Discovered by:** security-sentinel

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Add guard: if (messageTypeName.Contains(',')) skip type resolution. Or restrict to types registered in ConsumerRegistry.

## Acceptance Criteria

- [ ] Assembly-qualified type names are rejected before Type.GetType call

## Notes

Currently low risk since Headers.Type uses short names only

## Work Log

### 2026-03-20 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
