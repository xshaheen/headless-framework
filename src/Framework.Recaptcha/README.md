# Framework.Recaptcha

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
dotnet add package Framework.Recaptcha
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddReCaptchaV3(options =>
{
    options.SiteKey = builder.Configuration["ReCaptcha:SiteKey"];
    options.SiteSecret = builder.Configuration["ReCaptcha:SiteSecret"];
});
```

## Usage

### Server-Side Verification

```csharp
public class LoginController(IReCaptchaSiteVerifyV3 recaptcha)
{
    [HttpPost]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var result = await recaptcha.VerifyAsync(new ReCaptchaSiteVerifyRequest
        {
            Response = request.RecaptchaToken
        });

        if (!result.Success || result.Score < 0.5f)
            return BadRequest("reCAPTCHA validation failed");

        // Continue with login...
    }
}
```

### Razor Tag Helpers

```html
<!-- reCAPTCHA v3 -->
<recaptcha-v3-script site-key="@config.SiteKey" />
<recaptcha-v3-js action="login" callback="onRecaptchaVerified" />

<!-- reCAPTCHA v2 -->
<recaptcha-v2-script />
<recaptcha-v2-div site-key="@config.SiteKey" />
```

## Configuration

### appsettings.json

```json
{
  "ReCaptcha": {
    "SiteKey": "your-site-key",
    "SiteSecret": "your-secret-key"
  }
}
```

## Dependencies

- `Microsoft.AspNetCore.Razor`

## Side Effects

- Registers `IReCaptchaSiteVerifyV2` and/or `IReCaptchaSiteVerifyV3` as scoped
- Configures HttpClient for reCAPTCHA API
