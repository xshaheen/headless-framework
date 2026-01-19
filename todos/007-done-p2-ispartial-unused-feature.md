---
status: done
priority: p2
issue_id: "007"
tags: [code-review, yagni, api-design, messages]
created: 2026-01-19
dependencies: []
---

# isPartial Parameter - Unused Feature

## Problem Statement

`IConsumerBuilder.Topic(string topic, bool isPartial = false)` documents partial topic composition but feature is not implemented. Parameter stored but never used.

**Why Important:** Broken promise in API. Erodes developer trust. Adds complexity for zero value.

## Evidence from Reviews

**Simplicity Reviewer (Agent a9e76f8):**
> "No tests for `isPartial` = feature doesn't exist."

**Pragmatic Reviewer (Agent a256ec8):**
> "This is embarrassing... You documented it: 'partial topic combines with class-level topic.' But there's *no class-level topic concept* in this codebase."

**Code Evidence:**
```csharp
// IConsumerBuilder.cs:36 - Parameter exists
IConsumerBuilder<TConsumer> Topic(string topic, bool isPartial = false);

// ConsumerBuilder.cs:33 - Stored but ignored
_isPartial = isPartial;

// MessagingBuilder.cs:105-111 - Never used
var finalTopic = topic
    ?? (_topicMappings.TryGetValue(messageType, out var mappedTopic) ? mappedTopic : null)
    ?? messageType.Name;
// isPartial never referenced!
```

## Proposed Solutions

### Option 1: Remove Parameter (Recommended)
**Effort:** Small
**Risk:** Low (breaking change but pre-1.0)

```csharp
// Remove isPartial entirely
public IConsumerBuilder<TConsumer> Topic(string topic);
```

Clean, honest API.

### Option 2: Implement Feature
**Effort:** Medium
**Risk:** Medium - adds complexity

```csharp
// Add class-level topic prefix
public IMessagingBuilder WithTopicPrefix(string prefix);

// Then in ConsumerBuilder
var finalTopic = _isPartial
    ? $"{_messagingBuilder.TopicPrefix}.{topic}"
    : topic;
```

But: **Zero demos use this.** YAGNI.

### Option 3: Mark Obsolete
**Effort:** Small
**Risk:** Medium - leaves dead code

```csharp
[Obsolete("Partial topics not yet implemented. Parameter ignored.")]
IConsumerBuilder<TConsumer> Topic(string topic, bool isPartial = false);
```

Honest but messy.

## Technical Details

**Affected Files:**
- `src/Framework.Messages.Abstractions/IConsumerBuilder.cs:36`
- `src/Framework.Messages.Core/ConsumerBuilder.cs:33`
- `src/Framework.Messages.Core/MessagingBuilder.cs:105-111`

**Usage Across Demos:**
```bash
grep -r "isPartial" demo/
# No results - never used
```

## Acceptance Criteria

- [ ] Choose approach (recommend: Remove)
- [ ] Update `IConsumerBuilder.Topic()` signature
- [ ] Remove `_isPartial` field from `ConsumerBuilder`
- [ ] Update XML docs
- [ ] Verify all demos still compile
- [ ] Run full test suite

## Work Log

- **2026-01-19:** Issue identified during simplicity + pragmatic reviews

## Resources

- Simplicity Review: Agent a9e76f8
- Pragmatic Review: Agent a256ec8
- YAGNI principle: Don't implement features you might need later

### 2026-01-19 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-01-19 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
