# No Graceful Disconnect on SendAsync Failure

**Date:** 2026-01-11
**Status:** pending
**Priority:** P2 - Important
**Tags:** code-review, dotnet, emails, mailkit

---

## Problem Statement

If `SendAsync` throws, `DisconnectAsync` is never called. The `using` handles disposal, but `Dispose()` doesn't send QUIT command - it just drops the connection.

**Location:** `src/Framework.Emails.Mailkit/MailkitEmailSender.cs:25-26`

```csharp
await client.SendAsync(mimeMessage, cancellationToken);  // If this throws...
await client.DisconnectAsync(quit: true, cancellationToken);  // ...this never runs
```

**Impact:**
- SMTP connection left in bad state
- Server may keep connection open expecting more
- Not following SMTP protocol properly

---

## Proposed Solutions

### Option A: Finally Block for Disconnect (Recommended)

```csharp
await using var client = await _BuildClientAsync(settings, cancellationToken).AnyContext();
try
{
    await client.SendAsync(mimeMessage, cancellationToken).AnyContext();
}
finally
{
    if (client.IsConnected)
    {
        await client.DisconnectAsync(quit: true, cancellationToken).AnyContext();
    }
}
return SendSingleEmailResponse.Succeeded();
```

- **Effort:** Small
- **Risk:** None

---

## Acceptance Criteria

- [ ] Add finally block for graceful disconnect
- [ ] Check `IsConnected` before disconnect
- [ ] Handle disconnect failure silently (already disposing)

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From strict-dotnet-reviewer |
