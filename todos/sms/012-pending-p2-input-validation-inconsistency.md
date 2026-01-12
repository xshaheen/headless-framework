---
status: pending
priority: p2
issue_id: "012"
tags: [code-review, validation, consistency, sms]
dependencies: []
---

# Inconsistent input validation across SMS providers

## Problem Statement

Input validation patterns are inconsistent across SMS providers. Some validate `Destinations` and `Text`, some only validate the request object, and some have no validation.

## Findings

| Provider | Validation | File:Line |
|----------|-----------|-----------|
| AWS | `Argument.IsNotEmpty(Destinations)`, `Argument.IsNotEmpty(Text)` | `AwsSnsSmsSender.cs:24-25` |
| Cequens | **None** | `CequensSmsSender.cs:22` |
| Connekio | `Argument.IsNotNull(request)` only | `ConnekioSmsSender.cs:36` |
| Dev | **None** | `DevSmsSender.cs:12` |
| Infobip | **None** | `InfobipSmsSender.cs:29` |
| Twilio | `Argument.IsNotEmpty(Destinations)`, `Argument.IsNotEmpty(Text)` | `TwilioSmsSender.cs:26-27` |
| VictoryLink | `Argument.IsNotNull(request)` only | `VictoryLinkSmsSender.cs:25` |
| Vodafone | `Argument.IsNotNull(request)` only | `VodafoneSmsSender.cs:37` |

## Proposed Solutions

### Option 1: Standardize validation in all providers

**Approach:** Add consistent validation to all providers:

```csharp
public async ValueTask<SendSingleSmsResponse> SendAsync(
    SendSingleSmsRequest request,
    CancellationToken cancellationToken = default)
{
    Argument.IsNotNull(request);
    Argument.IsNotEmpty(request.Destinations);
    Argument.IsNotEmpty(request.Text);

    // ... rest of implementation
}
```

**Pros:**
- Fail-fast with clear error messages
- Consistent behavior across providers

**Cons:**
- Minor code duplication

**Effort:** 1 hour

**Risk:** Low

---

### Option 2: Validate in abstraction layer

**Approach:** Create a base class or extension method for validation.

**Pros:**
- Single place for validation logic

**Cons:**
- Adds abstraction layer
- May be overkill for simple validation

**Effort:** 2 hours

**Risk:** Low

## Recommended Action

Implement Option 1 - add consistent validation to all providers.

## Technical Details

**Files requiring changes:**
- `src/Framework.Sms.Cequens/CequensSmsSender.cs`
- `src/Framework.Sms.Connekio/ConnekioSmsSender.cs`
- `src/Framework.Sms.Dev/DevSmsSender.cs`
- `src/Framework.Sms.Infobip/InfobipSmsSender.cs`
- `src/Framework.Sms.VictoryLink/VictoryLinkSmsSender.cs`
- `src/Framework.Sms.Vodafone/VodafoneSmsSender.cs`

## Acceptance Criteria

- [ ] All providers validate: request not null, Destinations not empty, Text not empty
- [ ] Validation throws `ArgumentException` with clear message

## Work Log

### 2026-01-12 - Pattern Recognition Review

**By:** Claude Code

**Actions:**
- Cataloged validation patterns across all providers
- Found only 2/8 have complete validation
