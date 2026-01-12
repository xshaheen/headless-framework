---
status: pending
priority: p2
issue_id: "013"
tags: [code-review, twilio, sms, di]
dependencies: []
---

# TwilioSetup missing IConfiguration overload

## Problem Statement

`TwilioSetup` is the only SMS provider missing the `IConfiguration` overload for registration. All other providers support all three overload patterns.

## Findings

- **File:** `src/Framework.Sms.Twilio/Setup.cs:10-36`
- **Current overloads:**
  1. `Action<TwilioSmsOptions, IServiceProvider> setupAction`
  2. `Action<TwilioSmsOptions> setupAction`
- **Missing:**
  - `IConfiguration config` overload

All other providers have:
```csharp
public static IServiceCollection Add{Provider}SmsSender(
    this IServiceCollection services,
    IConfiguration config,
    ...
)
```

## Proposed Solutions

### Option 1: Add IConfiguration overload

**Approach:** Add the missing overload for consistency.

```csharp
public static IServiceCollection AddTwilioSmsSender(
    this IServiceCollection services,
    IConfiguration config)
{
    services.Configure<TwilioSmsOptions, TwilioSmsOptionsValidator>(config);
    return _AddCore(services);
}
```

**Pros:**
- Consistent API with other providers
- Enables appsettings.json configuration

**Cons:**
- Slightly more code

**Effort:** 15 minutes

**Risk:** Low

## Recommended Action

Add the `IConfiguration` overload for API consistency.

## Technical Details

**Affected files:**
- `src/Framework.Sms.Twilio/Setup.cs`

**New method:**
```csharp
public static IServiceCollection AddTwilioSmsSender(
    this IServiceCollection services,
    IConfiguration config)
{
    services.Configure<TwilioSmsOptions, TwilioSmsOptionsValidator>(config);
    return _AddCore(services);
}
```

## Acceptance Criteria

- [ ] `IConfiguration` overload added to TwilioSetup
- [ ] Follows same pattern as other providers

## Work Log

### 2026-01-12 - Pattern Recognition Review

**By:** Claude Code

**Actions:**
- Compared Setup overloads across all providers
- Found Twilio is the only one missing IConfiguration overload
