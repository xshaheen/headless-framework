---
status: completed
priority: p1
issue_id: "058"
tags: [code-review, security, rabbitmq, credentials]
created: 2026-01-20
completed: 2026-01-21
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

- [x] Remove hardcoded credentials
- [x] Add options validation
- [x] Update README with security notes
- [x] Add test: verify validation fires
- [x] Verify integration tests still pass (no integration tests exist for RabbitMQ)

**Effort:** 1 hour | **Risk:** Low

## Resolution Summary

Removed hardcoded default credentials and implemented runtime validation:
- Removed `DefaultUser` and `DefaultPass` constants
- Changed properties to `string.Empty` defaults
- Added validation in `RabbitMQOptionsValidator` to reject empty or "guest" credentials (case-insensitive)
- Updated README with security warnings and best practices
- Added comprehensive unit tests for credential validation
- Source builds successfully
