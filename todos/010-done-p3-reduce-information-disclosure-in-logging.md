---
status: done
priority: p3
issue_id: "010"
tags: ["code-review","security","dotnet"]
dependencies: []
---

# Reduce information disclosure in logging

## Problem Statement

Cache keys are logged at Trace level and the full invalidation message (including all keys) is logged on errors. This could leak sensitive information contained in cache keys (user IDs, session tokens, etc.).

## Findings

- **Trace logging:** src/Headless.Caching.Hybrid/HybridCache.cs:82-88
- **Error logging:** src/Headless.Caching.Hybrid/HybridCacheInvalidationConsumer.cs:33
- **String allocation:** string.Join on keys even when log level disabled
- **Discovered by:** security-sentinel, performance-oracle

## Proposed Solutions

### Option 1: Redact or hash sensitive portions of keys
- **Pros**: Protects sensitive data
- **Cons**: Less debugging info
- **Effort**: Medium
- **Risk**: Low

### Option 2: Log only metadata (key count, prefix, flags)
- **Pros**: No sensitive data leaked
- **Cons**: Harder to debug specific keys
- **Effort**: Small
- **Risk**: Low

### Option 3: Add ILogger.IsEnabled check before string operations
- **Pros**: Avoids allocation when logging disabled
- **Cons**: Doesn't address info disclosure
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Log only metadata (key count, prefix, flags) and add IsEnabled check.

## Acceptance Criteria

- [ ] Full cache keys not logged
- [ ] Error logs don't include full message with keys
- [ ] String.Join only executed when logging enabled

## Notes

Security finding - information disclosure through logging.

## Work Log

### 2026-02-04 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-02-04 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-02-04 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
