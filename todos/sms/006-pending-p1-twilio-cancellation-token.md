---
status: pending
priority: p1
issue_id: "006"
tags: [code-review, twilio, sms, async]
dependencies: []
---

# TwilioSmsSender does not propagate CancellationToken

## Problem Statement

`TwilioSmsSender.SendAsync` accepts a `CancellationToken` but does not pass it to `MessageResource.CreateAsync`. This means cancellation requests are ignored, and the SMS will still be sent even if the caller cancels.

## Findings

- **File:** `src/Framework.Sms.Twilio/TwilioSmsSender.cs:34-39`
- **Current code:**
```csharp
var respond = await MessageResource.CreateAsync(
    to: new PhoneNumber(request.Destinations.ToString()),
    from: new PhoneNumber(_options.PhoneNumber),
    body: request.Text,
    maxPrice: _options.MaxPrice
);  // No cancellationToken!
```
- `MessageResource.CreateAsync` supports a `CancellationToken` parameter
- Without it, the operation cannot be cancelled

## Proposed Solutions

### Option 1: Pass CancellationToken

**Approach:** Add the `cancellationToken` parameter to `CreateAsync`.

```csharp
var respond = await MessageResource.CreateAsync(
    to: new PhoneNumber(request.Destinations[0].ToString(hasPlusPrefix: true)),
    from: new PhoneNumber(_options.PhoneNumber),
    body: request.Text,
    maxPrice: _options.MaxPrice,
    cancellationToken: cancellationToken  // Add this
);
```

**Pros:**
- Proper cancellation support
- Follows async best practices

**Cons:**
- None

**Effort:** 5 minutes

**Risk:** Low

## Recommended Action

Add the `cancellationToken` parameter to the Twilio API call.

## Technical Details

**Affected files:**
- `src/Framework.Sms.Twilio/TwilioSmsSender.cs:34-39`

**Note:** This fix should be combined with fixing the phone number bug (todo 001).

## Acceptance Criteria

- [ ] CancellationToken is passed to MessageResource.CreateAsync
- [ ] Cancellation request properly aborts the operation

## Work Log

### 2026-01-12 - Code Review Discovery

**By:** Claude Code

**Actions:**
- Identified missing CancellationToken propagation
- Confirmed Twilio SDK supports cancellation
