---
status: pending
priority: p1
issue_id: "058"
tags: [code-review, security, rabbitmq, credentials]
created: 2026-01-20
dependencies: []
---

# Hardcoded Default Credentials (Security)

## Problem

**File:** `src/Framework.Messages.RabbitMQ/RabbitMqOptions.cs:14-16,66-68`

```csharp
public const string DefaultPass = "guest";
public const string DefaultUser = "guest";

public string Password { get; set; } = DefaultPass;
public string UserName { get; set; } = DefaultUser;
```

**Security risks:**
- Developers may ship "guest/guest" to production
- No warning about insecure defaults
- OWASP A07:2021 Identification/Authentication Failures

## Solution

**Option 1: No defaults** (Recommended)
```csharp
public required string UserName { get; init; }
public required string Password { get; init; }
```

Force explicit configuration via Options validation.

**Option 2: Runtime validation**
```csharp
public string UserName { get; set; } = string.Empty;
public string Password { get; set; } = string.Empty;

// In extension method:
services.AddOptions<RabbitMqOptions>()
    .Validate(opts =>
        !string.IsNullOrEmpty(opts.UserName) && opts.UserName != "guest",
        "RabbitMQ username must be configured and not 'guest'")
    .Validate(opts =>
        !string.IsNullOrEmpty(opts.Password) && opts.Password != "guest",
        "RabbitMQ password must be configured and not 'guest'");
```

## Acceptance Criteria

- [ ] Remove hardcoded credentials
- [ ] Add options validation
- [ ] Update README with security notes
- [ ] Add test: verify validation fires
- [ ] Verify integration tests still pass

**Effort:** 1 hour | **Risk:** Low
