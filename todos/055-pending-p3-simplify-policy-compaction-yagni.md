---
status: pending
priority: p3
issue_id: "055"
tags: [code-review, yagni, simplification, aws-sqs]
created: 2026-01-20
dependencies: []
---

# Simplify: Remove Policy Compaction (YAGNI)

## Problem

`AmazonPolicyExtensions.CompactSqsPermissions()` (lines 186-267) adds ~80 LOC of complexity:
- Parses ARNs for group names
- Creates wildcard patterns (`-*`)
- Optimizes multiple statements into one

**No evidence optimization needed:**
- AWS policy limits: 10KB (generous)
- No performance issue documented
- Complex logic increases bug risk

## Solution

**Remove entire method + helpers:**
- CompactSqsPermissions (lines 186-223)
- _GetArnGroupPrefix (lines 234-245)
- _GetGroupName (lines 256-266)

Remove call site in `AmazonSqsConsumerClient._GenerateSqsAccessPolicyAsync:270`

Keep `AddSqsPermissions` (useful) and `HasSqsPermission` (correct).

**LOC savings:** ~82 lines (~30% of policy extension)

## Acceptance Criteria

- [ ] Remove CompactSqsPermissions method
- [ ] Remove helper methods
- [ ] Remove call at line 270
- [ ] Verify policy still works without compaction
- [ ] Run integration tests

**Effort:** 30 min | **Risk:** Low
