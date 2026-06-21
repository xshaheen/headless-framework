# Headless.ReCaptcha

Google reCAPTCHA v2 and v3 integration with verification services and tag helpers.

## Problem Solved

Provides complete Google reCAPTCHA integration including server-side verification for both v2 (checkbox) and v3 (invisible score-based), plus Razor tag helpers for easy frontend integration.

## Key Features

- `IReCaptchaSiteVerifyV2` - reCAPTCHA v2 verification
- `IReCaptchaSiteVerifyV3` - reCAPTCHA v3 verification with score
- Razor tag helpers for script and widget rendering
- Language code provider for localization
- Source-generated JSON serialization

## Installation

```bash
dotnet add package Headless.ReCaptcha
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Bind from configuration (see appsettings.json below), or use the Action<ReCaptchaOptions> overload.
builder.Services.AddReCaptchaV3(builder.Configuration.GetSection("ReCaptcha:V3"));
```

## Usage

### Server-Side Verification

```csharp
public class LoginController(IReCaptchaSiteVerifyV3 recaptcha)
{
    [HttpPost]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        // Enforces success + action match (anti-replay) + score threshold server-side.
        var result = await recaptcha.VerifyAsync(
            new ReCaptchaSiteVerifyRequest { Response = request.RecaptchaToken },
            expectedAction: "login",
            minimumScore: 0.5f);

        if (!result.IsValid)
            return BadRequest($"reCAPTCHA validation failed: {result.FailureReason}");

        // Continue with login...
    }
}
```

### Razor Tag Helpers

```html
<!-- reCAPTCHA v3 -->
<recaptcha-script-v3 hide-badge="true" />
<recaptcha-script-v3-js action="login" callback="onRecaptchaVerified" />

<!-- reCAPTCHA v2 -->
<recaptcha-script-v2 />
<recaptcha-div-v2 />
```

## Configuration

reCAPTCHA v2 and v3 each have their own named options. Bind them from configuration sections:

```csharp
builder.Services.AddReCaptchaV3(builder.Configuration.GetSection("ReCaptcha:V3"));
builder.Services.AddReCaptchaV2(builder.Configuration.GetSection("ReCaptcha:V2"));
```

### appsettings.json

```json
{
  "ReCaptcha": {
    "V3": { "SiteKey": "your-v3-site-key", "SiteSecret": "your-v3-secret-key" },
    "V2": { "SiteKey": "your-v2-site-key", "SiteSecret": "your-v2-secret-key" }
  }
}
```

## Dependencies

- `Microsoft.AspNetCore.App` (framework reference)
- `Microsoft.Extensions.Http.Resilience`
- `Headless.Hosting`

## Side Effects

- Registers `IReCaptchaSiteVerifyV2` and/or `IReCaptchaSiteVerifyV3` as transient
- Configures a named `HttpClient` (with the standard resilience handler) for the reCAPTCHA API
