---
status: ready
priority: p2
issue_id: "008"
tags: [code-review, dry, api-design, messages]
created: 2026-01-19
dependencies: []
---

# Topic Name Duplication Across Demos

## Problem Statement

Topic names repeated 2-3 times across registration methods, violating DRY and defeating type-safe API purpose.

**Why Important:** Magic strings duplicated 16 times. Refactoring topic names requires 3 updates per consumer. No actual type safety benefit.

## Evidence from Reviews

**Pattern Recognition Specialist (Agent a850c2e):**
```csharp
// Amazon SQS Demo - line 8-9
messaging.Consumer<AmazonSqsMessageConsumer>().Topic("sample.aws.in-memory").Build();
messaging.WithTopicMapping<AmazonSqsMessage>("sample.aws.in-memory");  // DUPLICATE

// Then published:
await producer.Publish("sample.aws.in-memory", ...);  // 3rd time!
```

**Across 8 demos:**
- 8 consumers × 2 registrations = 16 topic string duplicates
- 8 publishers × 1 call = 8 more duplicates
- **Total: 24 magic strings**

## Proposed Solutions

### Option 1: Make Mapping Implicit from Consumer (Recommended)
**Effort:** Medium
**Risk:** Low

```csharp
// Register consumer → automatically maps message type to topic
services.AddMessages(messaging =>
{
    messaging.Consumer<AmazonSqsMessageConsumer>("sample.aws.in-memory");
    // Implicit: AmazonSqsMessage → "sample.aws.in-memory"
});

// No separate WithTopicMapping needed!
```

### Option 2: Mapping-First Registration
**Effort:** Medium
**Risk:** Low

```csharp
// Map first, then consumers use it
services.AddMessages(messaging =>
{
    messaging.MapTopic<AmazonSqsMessage>("sample.aws.in-memory")
        .Consumer<AmazonSqsMessageConsumer>();  // Uses mapped topic
});
```

### Option 3: Constants Class (Workaround)
**Effort:** Small
**Risk:** Medium - doesn't fix root cause

```csharp
public static class Topics
{
    public const string AmazonSqs = "sample.aws.in-memory";
}

messaging.Consumer<C>().Topic(Topics.AmazonSqs).Build();
messaging.WithTopicMapping<M>(Topics.AmazonSqs);
```

Still duplicated logic, just DRY-er strings.

## Technical Details

**Affected Files (all demos):**
- `demo/Framework.Messages.AmazonSqs.InMemory.Demo/Program.cs`
- `demo/Framework.Messages.Kafka.PostgreSql.Demo/Program.cs`
- `demo/Framework.Messages.Redis.SqlServer.Demo/Program.cs`
- ... (8 total demos)

**Current Pattern:**
```csharp
// Step 1: Consumer registration
messaging.Consumer<XConsumer>().Topic("x.topic").Build();

// Step 2: Topic mapping (why?)
messaging.WithTopicMapping<XMessage>("x.topic");

// Step 3: Publishing
await publisher.PublishAsync("x.topic", msg);
```

**Proposed Pattern:**
```csharp
// Step 1: Map + register
messaging.Consumer<XConsumer>("x.topic");  // Implicit mapping

// Step 2: Publishing
await publisher.PublishAsync<XMessage>(msg);  // Type-safe!
```

## Acceptance Criteria

- [ ] Choose approach (recommend: Option 1)
- [ ] Update `IMessagingBuilder` API
- [ ] Refactor all 8 demos
- [ ] Ensure `ScanConsumers()` still works
- [ ] Add migration guide to README
- [ ] Run full test suite

## Work Log

- **2026-01-19:** Issue identified across 8 demos

## Resources

- Pattern Recognition Review: Agent a850c2e
- Simplicity Review: Agent a9e76f8 (YAGNI violations)
- DRY Principle: Don't Repeat Yourself

### 2026-01-19 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
