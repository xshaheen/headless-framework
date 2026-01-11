# No TLS Enforcement - Nullable SocketOptions Default

**Date:** 2026-01-11
**Status:** pending
**Priority:** P1 - Critical
**Tags:** code-review, security, emails, mailkit

---

## Problem Statement

`SecureSocketOptions` is nullable with default `null`, falling back to `SecureSocketOptions.Auto` which may allow unencrypted connections if TLS negotiation fails.

**Location:** `src/Framework.Emails.Mailkit/MailkitSmtpOptions.cs:18`

```csharp
public SecureSocketOptions? SocketOptions { get; init; }
```

And in sender:
```csharp
options: options.SocketOptions ?? SecureSocketOptions.Auto
```

**Impact:**
- `Auto` can fall back to unencrypted if TLS fails
- Combined with port 25 = credentials sent in plain text
- No "pit of success" for security

---

## Proposed Solutions

### Option A: Require TLS by Default (Recommended)

```csharp
public SecureSocketOptions SocketOptions { get; init; } = SecureSocketOptions.StartTls;
```

- **Effort:** Trivial
- **Risk:** Breaking for users with no TLS support (rare)
- **Pros:** Secure by default

### Option B: Add AllowInsecure Flag

```csharp
public bool AllowInsecure { get; init; } = false;

// In sender
if (options.SocketOptions == SecureSocketOptions.None && !options.AllowInsecure)
    throw new SecurityException("Insecure SMTP not allowed. Set AllowInsecure=true to override.");
```

- **Effort:** Small
- **Risk:** None
- **Pros:** Explicit opt-in for insecure

---

## Recommended Action

Option A with clear documentation. Make security the default.

---

## Technical Details

**Affected Files:**
- `src/Framework.Emails.Mailkit/MailkitSmtpOptions.cs`
- `src/Framework.Emails.Mailkit/MailkitEmailSender.cs`

---

## Acceptance Criteria

- [ ] Change default to `SecureSocketOptions.StartTls`
- [ ] Remove nullable
- [ ] Document TLS requirement
- [ ] Consider adding explicit insecure opt-in flag

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From security-sentinel |
