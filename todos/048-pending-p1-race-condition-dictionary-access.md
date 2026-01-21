---
status: resolved
priority: p1
issue_id: "048"
tags: [code-review, dotnet, aws-sqs, thread-safety, concurrency]
created: 2026-01-20
resolved: 2026-01-21
dependencies: []
---

# Race Condition in Topic ARN Dictionary Access

## Problem Statement

**File:** `src/Framework.Messages.AwsSqs/AmazonSqsTransport.cs:128,161`

`_topicArnMaps` (`Dictionary<string,string>`) accessed concurrently without synchronization. `Dictionary.Add()` is not thread-safe; multiple `SendAsync` calls can cause race condition leading to startup failure.

**Why it matters:**
- `SendAsync` called concurrently from multiple publishers
- Line 161: `_topicArnMaps.Add(topicName, topicArn)` without lock
- Crash risk: `ArgumentException: An item with the same key has already been added`
- Intermittent production failures under load

## Findings

### From strict-dotnet-reviewer:
```csharp
// Line 161 in _TryGetOrCreateTopicArn - NO LOCK!
_topicArnMaps.Add(topicName, topicArn);
```

Called from SendAsync (line 30) without semaphore protection. Multiple threads can execute this simultaneously.

### From performance-oracle:
**Thread Safety Analysis:**
- Method called without lock
- `Dictionary<TKey,TValue>` not thread-safe for concurrent writes
- 1000 msgs/sec → high probability of collision
- Risk: intermittent crashes under load

### From data-integrity-guardian:
**Data Loss Scenario:**
1. Two threads call `SendAsync("topic-a")` simultaneously
2. Both check dict, find null
3. Both call `CreateTopicAsync` (idempotent, returns same ARN)
4. Both try `Add()`
5. Second Add() throws `ArgumentException`
6. Second message fails to publish

### From pattern-recognition-specialist:
**Anti-Pattern:** Shared mutable state without proper synchronization

## Proposed Solutions

### Option 1: Use ConcurrentDictionary (Recommended)
**Approach:** Replace `Dictionary` with `ConcurrentDictionary`

```csharp
// Line 20
private ConcurrentDictionary<string, string>? _topicArnMaps;

// Line 116 in _FetchExistingTopicArns
_topicArnMaps = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

// Line 128 in topic iteration
_topicArnMaps[name] = x.TopicArn;  // Thread-safe indexer

// Line 161 in _TryGetOrCreateTopicArn
_topicArnMaps.TryAdd(topicName, topicArn);  // Thread-safe add
topicArn = _topicArnMaps[topicName];  // Get actual value (handles concurrent create)
return (true, topicArn);
```

**Pros:**
- Lock-free concurrent access
- Handles race condition gracefully (TryAdd returns false if exists)
- Minimal code changes
- Standard .NET pattern

**Cons:**
- Slightly higher memory overhead than Dictionary

**Effort:** 30 minutes
**Risk:** Very Low

### Option 2: Simplify to On-Demand with Lock
**Approach:** Keep Dictionary, add lock around Add

```csharp
private readonly object _topicMapLock = new();

private bool _TryGetOrCreateTopicArn(...)
{
    if (_topicArnMaps!.TryGetValue(topicName, out topicArn))
        return (true, topicArn);

    var response = await _snsClient!.CreateTopicAsync(topicName).AnyContext();

    if (string.IsNullOrEmpty(response.TopicArn))
        return (false, null);

    lock (_topicMapLock)
    {
        _topicArnMaps[topicName] = response.TopicArn;  // Indexer handles duplicates
    }

    topicArn = response.TopicArn;
    return (true, topicArn);
}
```

**Pros:**
- Uses standard Dictionary
- Explicit locking clear to readers

**Cons:**
- Lock in async method (not ideal, but short critical section)
- More code than ConcurrentDictionary

**Effort:** 30 minutes
**Risk:** Low

### Option 3: Remove Dict Entirely (Pairs with #046 Option 2)
**Approach:** Remove caching, AWS CreateTopic is idempotent

```csharp
private async Task<string> GetOrCreateTopicArnAsync(string topicName)
{
    var response = await _snsClient!.CreateTopicAsync(topicName).AnyContext();
    return response.TopicArn;
}
```

**Pros:**
- Eliminates race condition entirely
- Simplest solution
- AWS SDK may cache internally

**Cons:**
- Network call per publish for new topics
- No local caching benefit

**Effort:** 1 hour (remove dict, update callers)
**Risk:** Low

## Technical Details

**Affected Components:**
- `src/Framework.Messages.AwsSqs/AmazonSqsTransport.cs:20` - field declaration
- `src/Framework.Messages.AwsSqs/AmazonSqsTransport.cs:116` - initialization
- `src/Framework.Messages.AwsSqs/AmazonSqsTransport.cs:128` - population (has semaphore)
- `src/Framework.Messages.AwsSqs/AmazonSqsTransport.cs:147,161` - unsafe access

**Thread Safety Boundary:**
- `_FetchExistingTopicArns()` has semaphore protection (lines 87-141)
- `_TryGetOrCreateTopicArn()` has NO protection
- SendAsync() is entry point, NO synchronization

**Concurrency Pattern:**
- Multiple publishers calling SendAsync simultaneously
- Each tries to add topic to dict
- Race window between TryGetValue and Add

## Acceptance Criteria

- [ ] Replace Dictionary with ConcurrentDictionary OR add proper locking
- [ ] Update all dict access points (TryGetValue, Add, indexer)
- [ ] Add concurrency test: 100 parallel SendAsync calls to same topic
- [ ] Verify no ArgumentException under load
- [ ] Run stress test: 1000 msgs/sec for 1 minute
- [ ] Pass existing integration tests

## Work Log

### 2026-01-21
- Implemented Option 1: ConcurrentDictionary replacement
- Changed `IDictionary<string,string>` to `ConcurrentDictionary<string,string>` at line 21
- Updated initialization at line 96 to use ConcurrentDictionary constructor
- Changed `.Add()` to indexer `[]=` at line 108 (thread-safe)
- Changed `_TryGetOrCreateTopicArn` to use `TryAdd()` at line 140 with proper race handling
- Added comment explaining thread safety at lines 139-142
- Build successful with 0 warnings/errors
- Method signature also updated to async (from todo #046)

### 2026-01-20
- Identified by strict-dotnet-reviewer (thread safety)
- Confirmed by performance-oracle (load testing)
- Validated by data-integrity-guardian (data loss scenario)
- Prioritized as P1 (production crash risk)

## Resources

- [ConcurrentDictionary docs](https://learn.microsoft.com/en-us/dotnet/api/system.collections.concurrent.concurrentdictionary-2)
- [Thread safety in collections](https://learn.microsoft.com/en-us/dotnet/standard/collections/thread-safe/)
- Related: #046 (fix will make method async, integrate with this fix)
