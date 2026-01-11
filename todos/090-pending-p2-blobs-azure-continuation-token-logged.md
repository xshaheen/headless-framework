# Continuation Token Logged (Potential Information Disclosure)

**Date:** 2026-01-11
**Status:** pending
**Priority:** P2 - Important
**Tags:** code-review, security, blobs-azure, logging

---

## Problem Statement

Azure continuation tokens are logged on error:

```csharp
_logger.LogError(
    e,
    "Error while getting blobs from Azure Storage. PageSizeToLoad={PageSizeToLoad} ContinuationToken={ContinuationToken}",
    pageSizeToLoad,
    continuationToken  // Potentially sensitive
);
```

**Why it matters:**
- Continuation tokens may contain encoded state information
- In multi-tenant scenarios, tokens might leak storage account internal state
- Could aid enumeration attacks
- Log aggregation systems may expose this data

---

## Proposed Solutions

### Option A: Remove Continuation Token from Logs
```csharp
_logger.LogError(
    e,
    "Error while getting blobs from Azure Storage. PageSizeToLoad={PageSizeToLoad}",
    pageSizeToLoad
);
```
- **Pros:** Simple, eliminates risk
- **Cons:** Loses debugging information
- **Effort:** Small
- **Risk:** Low

### Option B: Hash or Truncate Token
```csharp
var tokenForLogging = continuationToken?[..Math.Min(10, continuationToken.Length)] + "...";
```
- **Pros:** Some debugging info preserved
- **Cons:** Still leaks partial info
- **Effort:** Small
- **Risk:** Low

### Option C: Log Only on Debug Level
```csharp
_logger.LogDebug("Continuation token: {ContinuationToken}", continuationToken);
_logger.LogError(e, "Error while getting blobs from Azure Storage. PageSizeToLoad={PageSizeToLoad}", pageSizeToLoad);
```
- **Pros:** Available for debugging when needed
- **Cons:** Debug logs might also be captured
- **Effort:** Small
- **Risk:** Low

---

## Recommended Action

**Option A** - Remove continuation token from error logs. It's not useful for production troubleshooting.

---

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.Azure/AzureBlobStorage.cs` (lines 530-535)

---

## Acceptance Criteria

- [ ] Continuation token not logged at ERROR level
- [ ] Sufficient debugging info still available

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From security review |
