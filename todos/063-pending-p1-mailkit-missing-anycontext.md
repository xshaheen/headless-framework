# Missing AnyContext() on All Async Calls

**Date:** 2026-01-11
**Status:** pending
**Priority:** P1 - Critical
**Tags:** code-review, dotnet, emails, mailkit, async

---

## Problem Statement

Per codebase conventions (CLAUDE.md), all async calls in library code must use `AnyContext()` extension. Every `await` in MailkitEmailSender is missing this.

**Location:** `src/Framework.Emails.Mailkit/MailkitEmailSender.cs`

**Missing on:**
- Line 23: `await request.ConvertToMimeMessageAsync(cancellationToken)`
- Line 24: `await _BuildClientAsync(settings, cancellationToken)`
- Line 25: `await client.SendAsync(mimeMessage, cancellationToken)`
- Line 26: `await client.DisconnectAsync(quit: true, cancellationToken)`
- Line 42: `await _ConfigureClient(client, options, cancellationToken)`
- Line 60-65: `ConnectAsync` and `AuthenticateAsync`

**Impact:**
- Potential deadlocks in certain hosting environments
- Violates codebase conventions

---

## Proposed Solutions

### Option A: Add AnyContext() to All Awaits (Recommended)

```csharp
using var mimeMessage = await request.ConvertToMimeMessageAsync(cancellationToken).AnyContext();
using var client = await _BuildClientAsync(settings, cancellationToken).AnyContext();
await client.SendAsync(mimeMessage, cancellationToken).AnyContext();
await client.DisconnectAsync(quit: true, cancellationToken).AnyContext();
```

- **Effort:** Trivial
- **Risk:** None

---

## Acceptance Criteria

- [ ] Add `AnyContext()` to all 6+ await calls
- [ ] Verify Framework.Hosting is referenced (for extension)

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From strict-dotnet-reviewer |
