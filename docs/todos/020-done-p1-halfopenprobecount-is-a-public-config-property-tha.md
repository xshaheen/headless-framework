---
status: done
priority: p1
issue_id: "020"
tags: ["code-review","messaging","circuit-breaker","dotnet","api-design"]
dependencies: []
---

# HalfOpenProbeCount is a public config property that is explicitly ignored

## Problem Statement

CircuitBreakerOptions.HalfOpenProbeCount is a public init property with XML doc that states 'Kept for API completeness; the state manager currently uses a fixed value of 1.' This is a public API lie in a NuGet package. Once shipped, this property cannot be un-shipped. Users will configure it, observe no change in behavior, and file bugs. The property adds validation burden, documentation debt, and erodes trust in the API.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerOptions.cs:HalfOpenProbeCount
- **Risk:** User trust and support overhead — once NuGet-published, cannot remove without major version
- **Discovered by:** strict-dotnet-reviewer, pragmatic-dotnet-reviewer, code-simplicity-reviewer, agent-native-reviewer

## Proposed Solutions

### Remove the property entirely
- **Pros**: Clean API, no dead surface
- **Cons**: Must be added back when implemented
- **Effort**: Small
- **Risk**: Low

### Add [EditorBrowsable(Never)] + throw from validator if != 1
- **Pros**: Discoverable that it is not yet implemented
- **Cons**: Still public
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Remove HalfOpenProbeCount from CircuitBreakerOptions before the first NuGet publish. Add it back when multi-probe half-open is implemented.

## Acceptance Criteria

- [ ] HalfOpenProbeCount removed from CircuitBreakerOptions
- [ ] CircuitBreakerOptionsValidator updated accordingly
- [ ] No public API surface that silently does nothing

## Notes

PR #194 review. Pragmatic reviewer rates this P1 (blocks merge) specifically because of NuGet immutability.

## Work Log

### 2026-03-20 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-20 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-03-20 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
