---
status: ready
priority: p3
issue_id: "015"
tags: [code-review, security, logging, sms]
dependencies: []
---

# Potential credential/sensitive data in log messages

## Problem Statement

Several SMS providers log entire request/response objects which may contain sensitive metadata or internal details that shouldn't be logged.

## Findings

### AWS SNS
- **File:** `src/Framework.Sms.Aws/AwsSnsSmsSender.cs:72,80`
- Logs entire `PublishRequest` and `PublishResponse` objects
- May include `MessageAttributes` with sensitive metadata

### Cequens
- **File:** `src/Framework.Sms.Cequens/CequensSmsSender.cs:64,67`
- Returns raw API response body as error message
- May expose internal API structure

### Infobip
- **File:** `src/Framework.Sms.Infobip/InfobipSmsSender.cs:61,67`
- Logs entire `SmsRequest` and `ErrorContent`

## Proposed Solutions

### Option 1: Log only safe fields

**Approach:** Instead of logging entire objects, log specific safe fields.

**Before:**
```csharp
logger.LogError("Failed to send SMS {@Request} {@Response}", publishRequest, publishResponse);
```

**After:**
```csharp
logger.LogError("Failed to send SMS to {PhoneNumber}, StatusCode={StatusCode}",
    request.Destinations[0],
    publishResponse.HttpStatusCode);
```

**Pros:**
- No sensitive data in logs
- Still useful for debugging

**Cons:**
- Requires identifying which fields are safe

**Effort:** 1-2 hours

**Risk:** Low

## Recommended Action

Review all logging statements and ensure only safe, non-sensitive fields are logged.

## Technical Details

**Files to review:**
- `src/Framework.Sms.Aws/AwsSnsSmsSender.cs:72,80`
- `src/Framework.Sms.Cequens/CequensSmsSender.cs:64,67`
- `src/Framework.Sms.Connekio/ConnekioSmsSender.cs:61`
- `src/Framework.Sms.Infobip/InfobipSmsSender.cs:61,67`
- `src/Framework.Sms.VictoryLink/VictoryLinkSmsSender.cs:57-60`
- `src/Framework.Sms.Vodafone/VodafoneSmsSender.cs:59`

## Acceptance Criteria

- [ ] No credentials or secrets in log output
- [ ] No full API keys in logs
- [ ] Error responses sanitized before logging
- [ ] Still useful debugging information available

## Work Log

### 2026-01-12 - Security Review

**By:** Claude Code

**Actions:**
- Identified logging patterns across providers
- Flagged potential sensitive data exposure
