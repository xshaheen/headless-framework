---
status: completed
priority: p1
issue_id: "001"
tags: [code-review, bug, twilio, sms]
dependencies: []
---

# TwilioSmsSender uses wrong phone number format

## Problem Statement

`TwilioSmsSender.SendAsync` calls `request.Destinations.ToString()` on a `List<SmsRequestDestination>`, which returns `"System.Collections.Generic.List'1[Framework.Sms.SmsRequestDestination]"` instead of the actual phone number.

This is a **critical bug** that will cause all Twilio SMS sends to fail.

## Findings

- **File:** `src/Framework.Sms.Twilio/TwilioSmsSender.cs:35`
- **Current code:** `to: new PhoneNumber(request.Destinations.ToString())`
- **Should be:** `to: new PhoneNumber(request.Destinations[0].ToString())`
- All other providers correctly use `request.Destinations[0]` for single SMS
- Likely copy-paste error

## Proposed Solutions

### Option 1: Fix the accessor

**Approach:** Change `.ToString()` on the list to `[0].ToString()` on the first element.

```csharp
to: new PhoneNumber(request.Destinations[0].ToString(hasPlusPrefix: true))
```

**Pros:**
- Simple one-line fix
- Matches pattern used by other providers

**Cons:**
- None

**Effort:** 5 minutes

**Risk:** Low

## Recommended Action

Fix immediately - this is a critical bug preventing Twilio SMS from working.

## Technical Details

**Affected files:**
- `src/Framework.Sms.Twilio/TwilioSmsSender.cs:35`

**Related:**
- Twilio requires E.164 format (+1234567890), which `SmsRequestDestination.ToString(hasPlusPrefix: true)` provides

## Acceptance Criteria

- [ ] Phone number is correctly extracted from `Destinations[0]`
- [ ] Consider adding `hasPlusPrefix: true` for E.164 format
- [ ] Manual test with Twilio sandbox if possible

## Work Log

### 2026-01-12 - Code Review Discovery

**By:** Claude Code

**Actions:**
- Identified bug during code review
- Confirmed via pattern comparison with other providers
- All other providers use `Destinations[0]` correctly

### 2026-01-12 - Fixed

**By:** Claude Code

**Actions:**
- Changed `request.Destinations.ToString()` to `request.Destinations[0].ToString(hasPlusPrefix: true)`
- Phone number now correctly extracted in E.164 format
