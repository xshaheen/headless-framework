---
status: pending
priority: p2
issue_id: "007"
tags: [code-review, async, conventions, sms]
dependencies: []
---

# SMS providers missing AnyContext() on await calls

## Problem Statement

Per project conventions (CLAUDE.md), all async calls should use `.AnyContext()` extension instead of `ConfigureAwait(false)`. None of the SMS providers follow this convention, which can cause deadlocks in synchronous calling contexts.

## Findings

**Affected files and lines:**
- `src/Framework.Sms.Aws/AwsSnsSmsSender.cs:65`
- `src/Framework.Sms.Cequens/CequensSmsSender.cs:50,51,83,84`
- `src/Framework.Sms.Connekio/ConnekioSmsSender.cs:46,47`
- `src/Framework.Sms.Dev/DevSmsSender.cs:36`
- `src/Framework.Sms.Infobip/InfobipSmsSender.cs:60`
- `src/Framework.Sms.VictoryLink/VictoryLinkSmsSender.cs:40,41`
- `src/Framework.Sms.Vodafone/VodafoneSmsSender.cs:42,43`

**Example (current):**
```csharp
var response = await httpClient.PostAsJsonAsync(..., cancellationToken);
```

**Should be:**
```csharp
var response = await httpClient.PostAsJsonAsync(..., cancellationToken).AnyContext();
```

## Proposed Solutions

### Option 1: Add AnyContext() to all await calls

**Approach:** Systematically add `.AnyContext()` to all `await` expressions in SMS providers.

**Pros:**
- Follows framework conventions
- Prevents potential deadlocks

**Cons:**
- Repetitive changes

**Effort:** 1 hour

**Risk:** Low

## Recommended Action

Add `.AnyContext()` to all `await` expressions in SMS provider implementations.

## Technical Details

**Files requiring changes:**
- All 8 SMS sender implementations
- Approximately 2-4 await expressions per file

## Acceptance Criteria

- [ ] All await expressions use `.AnyContext()`
- [ ] Code follows framework async conventions

## Work Log

### 2026-01-12 - Code Review Discovery

**By:** Claude Code

**Actions:**
- Identified convention violation across all SMS providers
- Listed all affected files and line numbers
