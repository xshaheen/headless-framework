---
status: completed
priority: p3
issue_id: "017"
tags: [code-review, conventions, sms]
dependencies: []
---

# Mixed constructor styles (primary vs traditional)

## Problem Statement

SMS sender implementations mix primary constructors and traditional constructors, violating the project's preference for primary constructors.

## Findings

| Provider | Style | File:Line |
|----------|-------|-----------|
| AWS | Primary | `AwsSnsSmsSender.cs:11-15` |
| Cequens | Primary | `CequensSmsSender.cs:14-18` |
| **Connekio** | **Traditional** | `ConnekioSmsSender.cs:18-29` |
| **Infobip** | **Traditional** | `InfobipSmsSender.cs:17-27` |
| **Twilio** | **Traditional** | `TwilioSmsSender.cs:15-19` |
| VictoryLink | Primary | `VictoryLinkSmsSender.cs:11-15` |
| **Vodafone** | **Traditional** | `VodafoneSmsSender.cs:19-30` |
| Dev | Primary | `DevSmsSender.cs:7` |

Per CLAUDE.md: "Primary constructors for DI"

## Proposed Solutions

### Option 1: Convert all to primary constructors

**Approach:** Refactor traditional constructors to primary constructor syntax.

**Before (Connekio):**
```csharp
public sealed class ConnekioSmsSender : ISmsSender
{
    private readonly HttpClient _httpClient;
    private readonly ConnekioSmsOptions _options;
    private readonly ILogger<ConnekioSmsSender> _logger;

    public ConnekioSmsSender(
        HttpClient httpClient,
        IOptions<ConnekioSmsOptions> optionsAccessor,
        ILogger<ConnekioSmsSender> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = optionsAccessor.Value;
    }
}
```

**After:**
```csharp
public sealed class ConnekioSmsSender(
    HttpClient httpClient,
    IOptions<ConnekioSmsOptions> optionsAccessor,
    ILogger<ConnekioSmsSender> logger
) : ISmsSender
{
    private readonly ConnekioSmsOptions _options = optionsAccessor.Value;
    // Use httpClient and logger directly
}
```

**Pros:**
- Follows project conventions
- Less boilerplate

**Cons:**
- Code churn for existing implementations

**Effort:** 1-2 hours

**Risk:** Low

## Recommended Action

Convert to primary constructors for consistency with project conventions.

## Technical Details

**Files requiring changes:**
- `src/Framework.Sms.Connekio/ConnekioSmsSender.cs`
- `src/Framework.Sms.Infobip/InfobipSmsSender.cs`
- `src/Framework.Sms.Twilio/TwilioSmsSender.cs`
- `src/Framework.Sms.Vodafone/VodafoneSmsSender.cs`

## Acceptance Criteria

- [x] All SMS senders use primary constructors
- [x] Functionality unchanged

## Work Log

### 2026-01-12 - Pattern Recognition Review

**By:** Claude Code

**Actions:**
- Cataloged constructor styles across providers
- Found 4/8 using traditional constructors

### 2026-01-12 - Converted remaining constructors

**By:** Claude Code

**Actions:**
- Connekio & Infobip already converted by prior agents
- Converted `TwilioSmsSender.cs` to primary constructor with lazy init for `TwilioClient.Init`
- Converted `VodafoneSmsSender.cs` to primary constructor
