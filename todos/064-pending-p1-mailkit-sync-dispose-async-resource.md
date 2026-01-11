# Synchronous Dispose of IAsyncDisposable (SmtpClient)

**Date:** 2026-01-11
**Status:** pending
**Priority:** P1 - Critical
**Tags:** code-review, dotnet, emails, mailkit, async

---

## Problem Statement

`SmtpClient` implements `IAsyncDisposable` but code uses synchronous `using var` which calls `Dispose()` not `DisposeAsync()`.

**Location:** `src/Framework.Emails.Mailkit/MailkitEmailSender.cs:24`

```csharp
using var client = await _BuildClientAsync(settings, cancellationToken);  // Sync disposal!
```

**Impact:**
- Blocks thread during socket cleanup
- Not following async best practices
- MailKit's `DisposeAsync()` properly cleans up connections

---

## Proposed Solutions

### Option A: Use await using (Recommended)

```csharp
await using var client = await _BuildClientAsync(settings, cancellationToken).AnyContext();
```

- **Effort:** Trivial
- **Risk:** None

---

## Technical Details

**Affected Files:**
- `src/Framework.Emails.Mailkit/MailkitEmailSender.cs`

**Also check:** `using var mimeMessage` - verify if MimeMessage implements IAsyncDisposable.

---

## Acceptance Criteria

- [ ] Change `using var client` to `await using var client`
- [ ] Verify all IAsyncDisposable resources use `await using`

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From strict-dotnet-reviewer |
