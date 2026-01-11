# RequiresAuthentication Property Naming Confusion

**Date:** 2026-01-11
**Status:** pending
**Priority:** P3 - Nice-to-have
**Tags:** code-review, emails, mailkit, naming

---

## Problem Statement

`RequiresAuthentication` computes from User/Password presence, but name suggests a requirement flag. If User is set but Password is null, auth is silently skipped.

**Location:** `src/Framework.Emails.Mailkit/MailkitSmtpOptions.cs:20`

```csharp
public bool RequiresAuthentication => !string.IsNullOrEmpty(User) && !string.IsNullOrEmpty(Password);
```

**Issue:** Name suggests "does server require auth?" but behavior is "do we have credentials?"

---

## Proposed Solutions

### Option A: Rename to HasCredentials (Recommended)

```csharp
public bool HasCredentials => !string.IsNullOrEmpty(User) && !string.IsNullOrEmpty(Password);
```

- **Effort:** Trivial
- **Risk:** Breaking change for existing code

### Option B: Add Validation for Partial Credentials

```csharp
// In validator
RuleFor(x => x.User)
    .NotEmpty()
    .When(x => !string.IsNullOrEmpty(x.Password))
    .WithMessage("User required when Password is set");
```

- **Effort:** Small
- **Risk:** None

---

## Recommended Action

Option A + B - rename for clarity AND validate partial credentials.

---

## Acceptance Criteria

- [ ] Rename to `HasCredentials` or make explicit
- [ ] Add validation for User/Password consistency
- [ ] Document expected behavior

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From strict-dotnet-reviewer + pattern-recognition |
