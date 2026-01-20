---
status: pending
priority: p2
issue_id: "053"
tags: [code-review, dotnet, aws-sqs, security, data-integrity]
created: 2026-01-20
dependencies: []
---

# Unsafe JSON Deserialization

## Problem

**File:** `src/Framework.Messages.AwsSqs/AmazonSqsConsumerClient.cs:87`

```csharp
var messageObj = JsonSerializer.Deserialize<SqsReceivedMessage>(response.Messages[0].Body);
var header = messageObj!.MessageAttributes.ToDictionary(...);  // Null-forgiving!
```

No validation before deserializing untrusted SQS messages. Risks:
- `JsonException` → infinite retry loop (no commit/reject)
- `null` return → `NullReferenceException`
- JSON bombs → DoS
- Poison messages block queue

## Solution

```csharp
Task consumeAsync()
{
    try
    {
        var messageObj = JsonSerializer.Deserialize<SqsReceivedMessage>(
            response.Messages[0].Body);

        if (messageObj?.MessageAttributes == null)
        {
            _logger.LogError("Invalid SQS message structure");
            return RejectAsync(response.Messages[0].ReceiptHandle);
        }

        var header = messageObj.MessageAttributes.ToDictionary(
            x => x.Key,
            x => x.Value?.Value ?? string.Empty,
            StringComparer.Ordinal);

        var body = messageObj.Message;
        var message = new TransportMessage(header,
            body != null ? Encoding.UTF8.GetBytes(body) : null)
        {
            Headers = { [Headers.Group] = groupId },
        };

        return OnMessageCallback!(message, response.Messages[0].ReceiptHandle);
    }
    catch (JsonException ex)
    {
        _logger.LogError(ex, "Failed to deserialize SQS message. Moving to DLQ.");
        return RejectAsync(response.Messages[0].ReceiptHandle);
    }
}
```

## Acceptance Criteria

- [ ] Add try-catch around deserialization
- [ ] Validate messageObj not null
- [ ] Reject malformed messages (send to DLQ)
- [ ] Add test: malformed JSON → reject
- [ ] Add test: null MessageAttributes → reject

**Effort:** 1 hour | **Risk:** Low
