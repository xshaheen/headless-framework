# No Error Handling - All Exceptions Propagate

**Date:** 2026-01-11
**Status:** pending
**Priority:** P2 - Important
**Tags:** code-review, dotnet, emails, mailkit, error-handling

---

## Problem Statement

No try-catch in `SendAsync`. All SMTP exceptions propagate to caller. Ops team has no visibility into failure types.

**Location:** `src/Framework.Emails.Mailkit/MailkitEmailSender.cs:11-29`

```csharp
public async ValueTask<SendSingleEmailResponse> SendAsync(...)
{
    // No try-catch - exceptions propagate
    await client.SendAsync(mimeMessage, cancellationToken);
    return SendSingleEmailResponse.Succeeded();
}
```

**Failure scenarios unhandled:**
- DNS resolution fails
- Connection timeout
- Authentication failure
- Message rejected by server
- TLS negotiation failure

**Also:** Inconsistent with AWS sender which catches some exceptions.

---

## Proposed Solutions

### Option A: Catch SMTP Exceptions, Return Failed() (Recommended)

```csharp
try
{
    await client.SendAsync(mimeMessage, cancellationToken).AnyContext();
    return SendSingleEmailResponse.Succeeded();
}
catch (SmtpCommandException ex)
{
    _logger.LogWarning(ex, "SMTP command failed: {StatusCode}", ex.StatusCode);
    return SendSingleEmailResponse.Failed($"SMTP error: {ex.Message}");
}
catch (SmtpProtocolException ex)
{
    _logger.LogError(ex, "SMTP protocol error");
    return SendSingleEmailResponse.Failed($"Protocol error: {ex.Message}");
}
catch (AuthenticationException ex)
{
    _logger.LogCritical(ex, "SMTP authentication failed");
    throw; // Config error - should not be swallowed
}
```

- **Effort:** Small
- **Risk:** Behavior change (returns Failed instead of throwing)

### Option B: Let All Throw (Document)

Keep current behavior, document that all errors throw.

- **Effort:** Trivial
- **Risk:** Medium - inconsistent with result pattern

---

## Recommended Action

Option A - use the Result pattern that `SendSingleEmailResponse` was designed for.

---

## Technical Details

**Affected Files:**
- `src/Framework.Emails.Mailkit/MailkitEmailSender.cs`

**Dependencies:**
- Inject `ILogger<MailkitEmailSender>` for logging

---

## Acceptance Criteria

- [ ] Add try-catch for SMTP-specific exceptions
- [ ] Return `Failed()` for recoverable errors
- [ ] Log with appropriate levels
- [ ] Let config errors (auth) propagate
- [ ] Document error handling behavior

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From strict-dotnet-reviewer + pattern-recognition |
