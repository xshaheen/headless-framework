---
status: pending
priority: p1
issue_id: "006"
tags: ["code-review","architecture"]
dependencies: []
---

# Remove unused xunit.v3.extensibility.core package reference

## Problem Statement

Headless.Messaging.Testing.csproj references xunit.v3.extensibility.core but no .cs file in the package uses any xUnit type. This forces xUnit as a transitive dependency on all consumers, contradicting the framework's 'zero lock-in' principle. NUnit/MSTest consumers get unwanted xUnit assemblies.

## Findings

- **Location:** src/Headless.Messaging.Testing/Headless.Messaging.Testing.csproj:13
- **Discovered by:** security-sentinel, strict-dotnet-reviewer, pragmatic-dotnet-reviewer
- **Impact:** All consumers get transitive xUnit dependency; version conflicts with xunit v2 consumers

## Proposed Solutions

### Remove the PackageReference line
- **Pros**: One-line fix, zero risk
- **Cons**: None
- **Effort**: Small
- **Risk**: None


## Recommended Action

Remove the line. It is unused.

## Acceptance Criteria

- [ ] xunit.v3.extensibility.core removed from csproj
- [ ] Package builds successfully without it
- [ ] All 39 tests still pass

## Notes

The reference was added anticipating IAsyncLifetime usage but was never used in the implementation

## Work Log

### 2026-03-20 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
