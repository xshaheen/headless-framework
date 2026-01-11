# Plain Text Password in Options

**Date:** 2026-01-11
**Status:** pending
**Priority:** P2 - Important
**Tags:** code-review, security, emails, mailkit, credentials

---

## Problem Statement

SMTP password stored in plain text in options. Can be exposed in config files, logs, or memory dumps.

**Location:** `src/Framework.Emails.Mailkit/MailkitSmtpOptions.cs:14`

```csharp
public string? Password { get; init; }  // Plain text!
```

**Risks:**
- Exposed in appsettings.json
- Logged by configuration providers
- Visible in memory dumps
- Exposed via config endpoints

---

## Proposed Solutions

### Option A: Document Secret Store Usage (Recommended Short-term)

Add documentation for Azure Key Vault / AWS Secrets Manager integration.

```csharp
/// <summary>
/// SMTP password. Use user-secrets or key vault in production.
/// Never commit to source control.
/// </summary>
[SensitiveData]  // Custom attribute for logging redaction
public string? Password { get; init; }
```

- **Effort:** Small
- **Risk:** None

### Option B: Override ToString (Prevent Accidental Logging)

```csharp
public override string ToString() => $"SMTP: {Server}:{Port} (User: {User ?? "anonymous"})";
```

- **Effort:** Trivial
- **Risk:** None

### Option C: Secret Reference Pattern

```csharp
public string? PasswordSecretName { get; init; }  // Key vault reference
// Resolver resolves at runtime
```

- **Effort:** Medium
- **Risk:** Low

---

## Recommended Action

Options A + B together. Document proper secrets handling.

---

## Acceptance Criteria

- [ ] Add `[SensitiveData]` or similar attribute
- [ ] Override `ToString()` to exclude password
- [ ] Add documentation for secret store usage
- [ ] Add `[JsonIgnore]` to prevent serialization

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From security-sentinel |
