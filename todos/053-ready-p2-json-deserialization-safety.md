---
status: ready
priority: p2
issue_id: "053"
tags: [code-review, dotnet, aws-sqs, security, data-integrity]
created: 2026-01-20
resolved: 2026-01-21
dependencies: []
---

# Unsafe JSON Deserialization - RESOLVED

## Problem

**File:** `src/Framework.Messages.AwsSqs/AmazonSqsConsumerClient.cs:87`

No validation before deserializing untrusted SQS messages. Risks:
- `JsonException` → infinite retry loop (no commit/reject)
- `null` return → `NullReferenceException`
- JSON bombs → DoS
- Poison messages block queue

## Solution Implemented

### Code Changes

1. **Added ILogger support** (`AmazonSqsConsumerClient.cs`, `AmazonSqsConsumerClientFactory.cs`):
   - Injected `ILogger<AmazonSqsConsumerClient>` via primary constructor
   - Updated factory to pass logger instance

2. **Wrapped deserialization in try-catch** (`AmazonSqsConsumerClient.cs:95-126`):
   - Added `JsonException` handler
   - Logs error with context
   - Calls `RejectAsync()` to move malformed messages to DLQ

3. **Added null validation** (`AmazonSqsConsumerClient.cs:99-106`):
   - Checks `messageObj?.MessageAttributes == null`
   - Logs descriptive error
   - Rejects invalid messages

4. **Fixed null safety** (`AmazonSqsConsumerClient.cs:128-135`):
   - Updated dictionary creation to handle nullable values
   - Explicit type parameters for `ToDictionary` to match `IDictionary<string, string?>`

5. **Made consumeAsync truly async** (`AmazonSqsConsumerClient.cs:111`):
   - Changed from `Task` to `async Task`
   - Uses `.AnyContext()` for proper async flow

### Tests Added

**File:** `tests/Framework.Messages.AwsSqs.Tests.Integration/MalformedMessageTests.cs`

- ✅ `should_reject_message_with_invalid_json` - Malformed JSON → reject
- ✅ `should_reject_message_with_null_deserialization` - Null result → reject
- ✅ `should_reject_message_with_missing_message_attributes` - Missing attributes → reject
- ✅ `should_handle_well_formed_message_correctly` - Valid message → process

## Acceptance Criteria

- ✅ Add try-catch around deserialization
- ✅ Validate messageObj not null
- ✅ Reject malformed messages (send to DLQ)
- ✅ Add test: malformed JSON → reject
- ✅ Add test: null MessageAttributes → reject

## Files Changed

1. `src/Framework.Messages.AwsSqs/AmazonSqsConsumerClient.cs` - Added error handling and validation
2. `src/Framework.Messages.AwsSqs/AmazonSqsConsumerClientFactory.cs` - Added logger injection
3. `tests/Framework.Messages.AwsSqs.Tests.Integration/MalformedMessageTests.cs` - Added comprehensive tests

**Effort:** 1 hour | **Risk:** Low
