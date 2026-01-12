---
status: ready
priority: p2
issue_id: "008"
tags: [code-review, twilio, sms, architecture]
dependencies: []
---

# TwilioClient.Init() uses global static state

## Problem Statement

`TwilioSmsSender` calls `TwilioClient.Init()` in its constructor, which sets credentials in global static state. This is problematic for testing and multi-tenant scenarios.

## Findings

- **File:** `src/Framework.Sms.Twilio/TwilioSmsSender.cs:17-18`
- **Current code:**
```csharp
public TwilioSmsSender(IOptions<TwilioSmsOptions> optionsAccessor)
{
    _options = optionsAccessor.Value;
    TwilioClient.Init(_options.Sid, _options.AuthToken);  // Global state!
}
```
- `TwilioClient.Init()` sets static `TwilioClient.Username` and `TwilioClient.Password`
- Multiple instances with different credentials overwrite each other
- Makes unit testing difficult without integration with Twilio

## Proposed Solutions

### Option 1: Use ITwilioRestClient injection (Recommended)

**Approach:** Inject `ITwilioRestClient` and use it for API calls instead of static client.

```csharp
public sealed class TwilioSmsSender(
    ITwilioRestClient twilioClient,
    IOptions<TwilioSmsOptions> optionsAccessor
) : ISmsSender
{
    public async ValueTask<SendSingleSmsResponse> SendAsync(...)
    {
        var message = await MessageResource.CreateAsync(
            to: ...,
            from: ...,
            body: ...,
            client: twilioClient  // Use injected client
        );
    }
}
```

**Pros:**
- Testable with mock `ITwilioRestClient`
- Supports multi-tenant scenarios
- No global state

**Cons:**
- Requires DI setup for `ITwilioRestClient`
- More complex registration

**Effort:** 2-3 hours

**Risk:** Medium (API change)

---

### Option 2: Keep current but document limitation

**Approach:** Document that only one Twilio account is supported per application.

**Pros:**
- No code changes

**Cons:**
- Limitation remains
- Testing still difficult

**Effort:** 15 minutes

**Risk:** Low

## Recommended Action

For now, document the limitation. Consider Option 1 if multi-tenant or testability becomes a requirement.

## Technical Details

**Affected files:**
- `src/Framework.Sms.Twilio/TwilioSmsSender.cs:17-18`
- `src/Framework.Sms.Twilio/Setup.cs` (would need DI changes for Option 1)

## Acceptance Criteria

- [ ] Either inject ITwilioRestClient OR document single-tenant limitation
- [ ] Consider adding factory pattern if multi-tenant needed

## Work Log

### 2026-01-12 - Code Review Discovery

**By:** Claude Code

**Actions:**
- Identified global state anti-pattern
- Proposed two approaches: proper DI or documentation
