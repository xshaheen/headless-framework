---
status: wontfix
priority: p3
issue_id: "070"
tags: [code-review, yagni, simplification, rabbitmq]
created: 2026-01-20
resolved: 2026-01-21
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

## Resolution

**Status: WONTFIX - Not Applicable**

Upon investigation, the code described in the problem section **does not exist** in the current codebase:

1. **Incorrect line reference**: Lines 76-83 contain exception handling code, not header building
2. **Actual implementation** (line 41):
   ```csharp
   Headers = message.Headers.ToDictionary(x => x.Key, object? (x) => x.Value, StringComparer.Ordinal),
   ```

3. **Why current implementation is optimal**:
   - Type conversion required: `IDictionary<string, string?>` → `IDictionary<string, object?>`
   - Dictionary constructor approach fails compilation (no implicit covariance)
   - `.ToDictionary()` is the cleanest way to perform this conversion
   - Already uses `StringComparer.Ordinal` for performance

4. **Suggested alternatives tested**:
   - `new Dictionary<string, object?>(message.Headers)` - **Compilation error CS1503**
   - Direct assignment - **Type mismatch**

The current implementation is already simple and optimal. No changes needed.

**Effort:** 45 min (investigation) | **Risk:** N/A
