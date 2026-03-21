---
status: done
priority: p1
issue_id: "035"
tags: ["code-review","dotnet","quality","architecture"]
dependencies: []
---

# Fix dead IValidateOptions<T> registrations bypassed by snapshot IOptions

## Problem Statement

Setup.cs registers IOptions<CircuitBreakerOptions> and IOptions<RetryProcessorOptions> as pre-built frozen snapshots via Options.Create(). It then ALSO registers IValidateOptions<CircuitBreakerOptionsValidator> and IValidateOptions<RetryProcessorOptionsValidator>. The IValidateOptions<T> registrations are dead because the IOptions<T> snapshot bypasses the Options framework pipeline entirely — the validators are never invoked at runtime. Additionally, the comment claims init-only properties prevent use of the standard Configure<T> pipeline but both types use { get; set; } properties, not init. The options bypass is unnecessary.

## Findings

- **Location:** src/Headless.Messaging.Core/Setup.cs:195-206
- **Risk:** Medium - dead validator registrations create false confidence
- **Discovered by:** compound-engineering:review:pragmatic-dotnet-reviewer

## Proposed Solutions

### Use standard Options pipeline with ValidateOnStart
- **Pros**: Idiomatic .NET, validators actually run, single registration path
- **Cons**: Requires copying options values via Configure<T> callback
- **Effort**: Small
- **Risk**: Low

### Keep snapshot approach but remove dead IValidateOptions registrations
- **Pros**: Minimal change
- **Cons**: Still non-idiomatic, eager validation is private method
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Remove the dead IValidateOptions<T> registrations. Either use services.AddOptions<T>().Configure(...).ValidateOnStart() or keep the hand-rolled validation but remove the misleading IValidateOptions registrations that are never called.

## Acceptance Criteria

- [ ] IValidateOptions<T> registrations either wired correctly or removed
- [ ] Validation still occurs at startup
- [ ] Invalid options still throw at startup

## Notes

Discovered during PR #194 code review (round 2)

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
