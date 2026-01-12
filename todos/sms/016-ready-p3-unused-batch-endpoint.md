---
status: ready
priority: p3
issue_id: "016"
tags: [code-review, dead-code, cequens, sms]
dependencies: []
---

# CequensSmsOptions.BatchSmsEndpoint is configured but never used

## Problem Statement

`CequensSmsOptions` has a `BatchSmsEndpoint` property that is configured and validated but never used in the implementation.

## Findings

- **Options file:** `src/Framework.Sms.Cequens/CequensSmsOptions.cs:12`
```csharp
public required string BatchSmsEndpoint { get; init; } = "https://apis.cequens.com/sms/v1/megabulk/recipients";
```

- **Validator file:** `src/Framework.Sms.Cequens/CequensSmsOptions.cs:30`
```csharp
RuleFor(x => x.BatchSmsEndpoint).NotEmpty().HttpUrl();
```

- **Sender file:** `src/Framework.Sms.Cequens/CequensSmsSender.cs:50`
  - Only uses `_options.SingleSmsEndpoint`
  - Never references `BatchSmsEndpoint`

## Proposed Solutions

### Option 1: Remove unused property

**Approach:** Delete `BatchSmsEndpoint` property and its validation.

**Pros:**
- Removes dead code
- Reduces confusion
- Smaller options class

**Cons:**
- Breaking change for existing configurations

**Effort:** 15 minutes

**Risk:** Low (property was never used anyway)

---

### Option 2: Implement batch endpoint usage

**Approach:** Actually use the batch endpoint for batch sends.

**Pros:**
- Uses the intended API

**Cons:**
- More development work
- May not be needed

**Effort:** 2-4 hours

**Risk:** Medium

## Recommended Action

Remove the unused property (Option 1). If batch support is needed, it can be added later.

## Technical Details

**Files requiring changes:**
- `src/Framework.Sms.Cequens/CequensSmsOptions.cs:12` - remove property
- `src/Framework.Sms.Cequens/CequensSmsOptions.cs:30` - remove validation

## Acceptance Criteria

- [ ] BatchSmsEndpoint property removed
- [ ] Validation rule removed
- [ ] No references to removed property

## Work Log

### 2026-01-12 - Simplicity Review

**By:** Claude Code

**Actions:**
- Found unused BatchSmsEndpoint property
- Confirmed never referenced in sender implementation
