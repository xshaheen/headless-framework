---
status: ready
priority: p2
issue_id: "011"
tags: [code-review, di, architecture, sms]
dependencies: []
---

# Redundant singleton registration before AddHttpClient

## Problem Statement

All HTTP-based SMS providers register `ISmsSender` as Singleton, then immediately call `AddHttpClient<ISmsSender, TSender>`. The singleton registration is overwritten by the HttpClient factory registration.

## Findings

- **Pattern in all HTTP providers:**
```csharp
services.AddSingleton<ISmsSender, CequensSmsSender>();  // Dead code!
var httpClientBuilder = services.AddHttpClient<ISmsSender, CequensSmsSender>(...);
```

- **Files affected:**
  - `src/Framework.Sms.Cequens/Setup.cs:54`
  - `src/Framework.Sms.Connekio/Setup.cs:54`
  - `src/Framework.Sms.Infobip/Setup.cs:54`
  - `src/Framework.Sms.VictoryLink/Setup.cs:54`
  - `src/Framework.Sms.Vodafone/Setup.cs:54`

- `AddHttpClient<TClient, TImplementation>` registers a factory for `TClient`
- The previous `AddSingleton` is overwritten (DI uses last registration)
- The singleton line is effectively dead code

## Proposed Solutions

### Option 1: Use Named HttpClient (SELECTED)

**Approach:** Use named HttpClient pattern - keep singleton, inject `IHttpClientFactory`, use `CreateClient("ProviderName")`.

```csharp
// Setup.cs
services.AddSingleton<ISmsSender, CequensSmsSender>();
var httpClientBuilder = services.AddHttpClient("CequensSms", configureClient);

// CequensSmsSender.cs
public sealed class CequensSmsSender(IHttpClientFactory httpClientFactory, ...) : ISmsSender
{
    public async Task<SmsResult> SendAsync(...)
    {
        using var client = httpClientFactory.CreateClient("CequensSms");
        // ...
    }
}
```

**Pros:**
- True singleton for the service
- HttpClient pooling/handler management via factory
- Cleaner separation of concerns
- Follows .NET best practices for HttpClient usage

**Cons:**
- Requires updating sender classes to inject IHttpClientFactory

**Effort:** Medium (5 Setup.cs + 5 Sender classes)

**Risk:** Low

## Recommended Action

Switch to named HttpClient pattern across all 5 HTTP-based SMS providers.

## Technical Details

**Files requiring changes:**
- `src/Framework.Sms.Cequens/Setup.cs` - Use `AddHttpClient("CequensSms")`
- `src/Framework.Sms.Cequens/CequensSmsSender.cs` - Inject IHttpClientFactory
- `src/Framework.Sms.Connekio/Setup.cs` - Use `AddHttpClient("ConnekioSms")`
- `src/Framework.Sms.Connekio/ConnekioSmsSender.cs` - Inject IHttpClientFactory
- `src/Framework.Sms.Infobip/Setup.cs` - Use `AddHttpClient("InfobipSms")`
- `src/Framework.Sms.Infobip/InfobipSmsSender.cs` - Inject IHttpClientFactory
- `src/Framework.Sms.VictoryLink/Setup.cs` - Use `AddHttpClient("VictoryLinkSms")`
- `src/Framework.Sms.VictoryLink/VictoryLinkSmsSender.cs` - Inject IHttpClientFactory
- `src/Framework.Sms.Vodafone/Setup.cs` - Use `AddHttpClient("VodafoneSms")`
- `src/Framework.Sms.Vodafone/VodafoneSmsSender.cs` - Inject IHttpClientFactory

**Note:** Twilio and AWS don't use HttpClient from IHttpClientFactory, so their singleton registrations are correct.

## Acceptance Criteria

- [ ] Setup.cs files use named AddHttpClient("ProviderSms")
- [ ] Sender classes inject IHttpClientFactory
- [ ] Sender classes call CreateClient("ProviderSms")
- [ ] Verify services still resolve correctly
- [ ] Twilio/AWS unchanged

## Work Log

### 2026-01-12 - Code Review Discovery

**By:** Claude Code

**Actions:**
- Identified dead code pattern across 5 providers
- Confirmed AddHttpClient overwrites previous registration

### 2026-01-12 - Approved for Work

**By:** Claude Triage System

**Actions:**
- Issue approved during triage session
- Status changed from pending -> ready
- Solution updated to use named HttpClient pattern per user preference
- Ready to be picked up and worked on
