---
status: pending
priority: p2
issue_id: "006"
tags: ["code-review","dotnet","architecture","quality"]
dependencies: []
---

# Enforce case-insensitive uniqueness for runtime subscription routes

## Problem Statement

Runtime route uniqueness checks are case-sensitive, while dispatch matching is case-insensitive. This allows duplicate runtime subscriptions for the same logical route using different casing (for example, 'orders.created' vs 'Orders.Created'), creating ambiguous behavior and unpredictable consumer selection.

## Findings

- **Route uniqueness check:** src/Headless.Messaging.Core/Internal/RuntimeMessageSubscriber.cs:64
- **Duplicate key check:** src/Headless.Messaging.Core/Internal/RuntimeMessageSubscriber.cs:50
- **Case-insensitive dispatch matching:** src/Headless.Messaging.Core/Internal/ConsumerExecutorDescriptor.cs:27
- **Hash/equality mismatch:** src/Headless.Messaging.Core/Internal/ConsumerExecutorDescriptor.cs:50

## Proposed Solutions

### Normalize route keys before storing
- **Pros**: Simple lookup path and deterministic route identity
- **Cons**: Requires clear canonical casing policy
- **Effort**: Small
- **Risk**: Low

### Use case-insensitive comparers everywhere
- **Pros**: Matches dispatch semantics and avoids casing bugs
- **Cons**: Requires updates to dictionaries and comparer hash implementation
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Use case-insensitive topic/group comparison in runtime uniqueness checks and align ConsumerExecutorDescriptorComparer.GetHashCode with Equals semantics.

## Acceptance Criteria

- [ ] Subscribing same topic/group with different casing is rejected
- [ ] ConsumerExecutorDescriptorComparer.GetHashCode uses same case rules as Equals
- [ ] Tests cover mixed-case duplicate runtime subscriptions

## Notes

Discovered during PR #177 review.

## Work Log

### 2026-02-13 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
