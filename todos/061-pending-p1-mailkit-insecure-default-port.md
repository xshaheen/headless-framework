# Insecure Default Port 25 (Unencrypted SMTP)

**Date:** 2026-01-11
**Status:** pending
**Priority:** P1 - Critical
**Tags:** code-review, security, emails, mailkit

---

## Problem Statement

Default port 25 transmits credentials and email content in **plain text**. Port 25 is also blocked by most cloud providers (AWS, Azure, GCP).

**Location:** `src/Framework.Emails.Mailkit/MailkitSmtpOptions.cs:16`

```csharp
public int Port { get; init; } = 25;  // Dangerous default!
```

**Impact:**
- SMTP credentials interceptable via MITM
- Email content exposed
- Won't work on cloud providers (port blocked)

---

## Proposed Solutions

### Option A: Default to Port 587 (Recommended)

```csharp
public int Port { get; init; } = 587;  // STARTTLS submission port
```

- **Effort:** Trivial
- **Risk:** Breaking change for existing users on port 25
- **Pros:** Modern standard, encrypted by default

### Option B: Default to Port 465

```csharp
public int Port { get; init; } = 465;  // Implicit TLS
```

- **Effort:** Trivial
- **Risk:** Low - widely supported
- **Pros:** TLS from connection start

---

## Recommended Action

Option A - Port 587 is the modern submission standard.

---

## Technical Details

**Affected Files:**
- `src/Framework.Emails.Mailkit/MailkitSmtpOptions.cs`

**Port comparison:**
| Port | Security | Usage |
|------|----------|-------|
| 25 | None | Legacy relay (blocked by clouds) |
| 587 | STARTTLS | Modern submission (recommended) |
| 465 | Implicit TLS | Secure submission |

---

## Acceptance Criteria

- [ ] Change default port to 587
- [ ] Update documentation
- [ ] Add migration note for existing users

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From security-sentinel + pragmatic-dotnet-reviewer |
