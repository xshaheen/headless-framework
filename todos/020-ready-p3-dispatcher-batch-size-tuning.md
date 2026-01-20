---
status: ready
priority: p3
issue_id: "020"
tags: [performance, optimization, tuning, configuration]
dependencies: []
---

# Dispatcher Batch Size Formula Not Tuned for Scale

## Problem Statement

Dispatcher uses fixed formula for batch size (`channelSize / 50`) that doesn't scale optimally for high-throughput systems.

## Findings

**Current Logic** (Dispatcher.cs:340):
```csharp
var batchSize = Math.Max(1, _publishChannelSize / 50);
// If channelSize = 10,000 → batchSize = 200
// If channelSize = 100,000 → batchSize = 2,000
```

**Issues**:
- Linear scaling (should be logarithmic)
- No upper bound (2,000 messages in single batch = high latency)
- No configuration option

**Ideal Batch Sizes** (from performance testing):
- Low traffic (< 1K/sec): 10-50
- Medium traffic (1K-10K/sec): 50-200
- High traffic (> 10K/sec): 100-500 (NOT 2,000)

## Proposed Solutions

### Option 1: Logarithmic Formula + Upper Bound (RECOMMENDED)
**Effort**: 1 hour

```csharp
var batchSize = Math.Min(
    500, // Upper bound for latency
    Math.Max(
        10, // Lower bound
        (int)Math.Log2(_publishChannelSize) * 10
    )
);
```

### Option 2: Configuration Option
**Effort**: 2 hours

Add to MessagingOptions:
```csharp
public int? BatchSize { get; set; } = null; // null = auto-calculate
```

## Recommended Action

Implement Option 1 first, add Option 2 if users need custom tuning.

## Acceptance Criteria

- [ ] Batch size capped at 500
- [ ] Performance tests verify improved latency
- [ ] Throughput not negatively impacted
- [ ] Configuration option documented

## Work Log

### 2026-01-20 - Issue Created

**By:** Claude Code (Performance Oracle Agent)

**Actions:**
- Analyzed batch size scaling patterns
- Proposed logarithmic formula
- Recommended upper bound for latency

### 2026-01-20 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
