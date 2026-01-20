---
status: pending
priority: p1
issue_id: "047"
tags: [code-review, dotnet, aws-sqs, async, framework-conventions]
created: 2026-01-20
dependencies: []
---

# Missing AnyContext() Framework Convention

## Problem Statement

**Files:** All async methods in `src/Framework.Messages.AwsSqs/`

17 occurrences of `ConfigureAwait(false)` should use framework's `AnyContext()` extension per project conventions. Library code must avoid capturing synchronization context.

**Why it matters:**
- Framework convention mandates `.AnyContext()` for library code (see CLAUDE.md)
- Consistency across all provider implementations
- Performance: prevents unnecessary context capture
- Part of broader messaging-consume refactoring

## Findings

### From strict-dotnet-reviewer:
Transport (AmazonSqsTransport.cs): **0** uses of ConfigureAwait - missing entirely
Consumer (AmazonSqsConsumerClient.cs): **17** uses of `.ConfigureAwait(false)` instead of `.AnyContext()`

### From architecture-strategist:
Framework standard documented in `llms-full.txt`:
```csharp
// Use AnyContext() extension instead of ConfigureAwait(false) for library code
await _sqsClient.ReceiveMessageAsync(...).AnyContext();
```

Current non-compliance impacts architectural consistency.

### From git-history-analyzer:
AnyContext convention introduced in Framework.Base
AwsSqs created before convention fully adopted - missed migration

## Proposed Solutions

### Option 1: Global Replace (Recommended)
**Approach:** Replace all 17 occurrences + add to Transport

```bash
# In AmazonSqsConsumerClient.cs - replace all
.ConfigureAwait(false) → .AnyContext()

# In AmazonSqsTransport.cs - add to all awaits
await _snsClient!.PublishAsync(request).AnyContext();
await _snsClient.ListTopicsAsync().AnyContext();
```

**Pros:**
- Simple find-replace operation
- Full convention compliance
- Matches other framework providers

**Cons:**
- None (pure convention alignment)

**Effort:** 30 minutes
**Risk:** Very Low (semantic equivalent)

## Technical Details

**Affected Files:**
- `src/Framework.Messages.AwsSqs/AmazonSqsConsumerClient.cs` - 17 locations
- `src/Framework.Messages.AwsSqs/AmazonSqsTransport.cs` - 5+ locations (missing)

**Locations in AmazonSqsConsumerClient.cs:**
```
Line 37:  await _ConnectAsync(true, false).ConfigureAwait(false);
Line 44:  .CreateTopicAsync(createTopicRequest).ConfigureAwait(false);
Line 49:  await _GenerateSqsAccessPolicyAsync(topicArns).ConfigureAwait(false);
Line 58:  await _ConnectAsync().ConfigureAwait(false);
Line 60:  await _SubscribeToTopics(topics).ConfigureAwait(false);
Line 65:  await _ConnectAsync().ConfigureAwait(false);
Line 71:  var response = await _sqsClient!.ReceiveMessageAsync(...).ConfigureAwait(false);
Line 77:  await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
Line 78:  _ = Task.Run(consumeAsync, cancellationToken).ConfigureAwait(false);  // Also needs fixing
Line 82:  await consumeAsync().ConfigureAwait(false);
... (7 more occurrences)
```

**Locations in AmazonSqsTransport.cs (missing):**
```
Line 48:  await _snsClient!.PublishAsync(request);  // ADD .AnyContext()
Line 87:  await _semaphore.WaitAsync();  // ADD .AnyContext()
Line 123: await _snsClient.ListTopicsAsync()  // ADD .AnyContext()
Line 124: await _snsClient.ListTopicsAsync(nextToken)  // ADD .AnyContext()
```

**Framework Reference:**
From `/Users/xshaheen/.claude/rules/dotnet/headless.md`:
```markdown
## Async
- Use `AnyContext()` extension instead of `ConfigureAwait(false)` for library code
- Always pass `CancellationToken` as the last parameter to async methods
```

## Acceptance Criteria

- [ ] Replace all `.ConfigureAwait(false)` with `.AnyContext()` in AwsSqs project
- [ ] Add `.AnyContext()` to all async calls in AmazonSqsTransport.cs
- [ ] Verify AnyContext extension is accessible (using Framework.Base)
- [ ] Run all unit tests (pass)
- [ ] Run integration tests with LocalStack (pass)
- [ ] Check other messaging providers for consistency (RabbitMQ, Kafka, InMemory)

## Work Log

### 2026-01-20
- Identified by strict-dotnet-reviewer and architecture-strategist
- 17 occurrences in Consumer, 5+ missing in Transport
- Tagged as P1 for framework convention compliance

## Resources

- Framework convention: `/Users/xshaheen/.claude/rules/dotnet/headless.md`
- Extension source: `src/Framework.Base/Threading/TaskExtensions.cs`
- Related: #046 (sync-over-async fix will use AnyContext)
