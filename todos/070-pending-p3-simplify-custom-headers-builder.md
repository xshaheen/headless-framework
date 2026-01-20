---
status: pending
priority: p3
issue_id: "070"
tags: [code-review, yagni, simplification, rabbitmq]
created: 2026-01-20
dependencies: []
---

# YAGNI: CustomHeadersBuilder Complexity

## Problem

**File:** `src/Framework.Messages.RabbitMQ/RabbitMqTransport.cs:76-83`

Complex builder for simple dictionary:
```csharp
var customHeader = new Dictionary<string, object?>
{
    [Headers.MessageId] = message.GetId(),
    [Headers.MessageName] = message.GetName(),
};

message.Headers.TryAdd(Headers.SentTime, DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString());
```

**Complexity:**
- Unnecessary intermediate dictionary
- Could inline into BasicProperties.Headers

## Solution

```csharp
var headers = new Dictionary<string, object?>(message.Headers)
{
    [Headers.MessageId] = message.GetId(),
    [Headers.MessageName] = message.GetName(),
};

if (!headers.ContainsKey(Headers.SentTime))
    headers[Headers.SentTime] = DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString();

basicProperties.Headers = headers;
```

Or simpler:
```csharp
message.Headers[Headers.MessageId] = message.GetId();
message.Headers[Headers.MessageName] = message.GetName();
message.Headers.TryAdd(Headers.SentTime, DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString());

basicProperties.Headers = message.Headers;
```

## Acceptance Criteria

- [ ] Simplify header building logic
- [ ] Remove intermediate dictionary
- [ ] Verify headers still populated correctly
- [ ] Run integration tests

**Effort:** 30 min | **Risk:** Very Low
