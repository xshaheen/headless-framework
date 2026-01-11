# Missing Timeout Configuration

**Date:** 2026-01-11
**Status:** pending
**Priority:** P2 - Important
**Tags:** code-review, emails, mailkit, configuration

---

## Problem Statement

No timeout configuration. MailKit's default is 2 minutes. A hung SMTP server will block for 2 minutes per email.

**Location:** `src/Framework.Emails.Mailkit/MailkitSmtpOptions.cs` - Missing

**Impact:**
- Long hangs on network issues
- Thread pool starvation
- Poor user experience waiting for email send
- DoS vector (slow server)

---

## Proposed Solutions

### Option A: Add Timeout Option (Recommended)

```csharp
// In MailkitSmtpOptions
public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

// In sender
client.Timeout = (int)options.Timeout.TotalMilliseconds;
```

- **Effort:** Trivial
- **Risk:** None

### Option B: Separate Connect/Send Timeouts

```csharp
public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);
public TimeSpan SendTimeout { get; init; } = TimeSpan.FromSeconds(30);
```

- **Effort:** Small
- **Risk:** None
- **Pros:** More granular control

---

## Recommended Action

Option A for simplicity. Option B if finer control needed.

---

## Acceptance Criteria

- [ ] Add `Timeout` property to options
- [ ] Apply timeout in `_BuildClientAsync`
- [ ] Add validation (must be positive)
- [ ] Document default value

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From strict-dotnet-reviewer + pragmatic-dotnet-reviewer |
