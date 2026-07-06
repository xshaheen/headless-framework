# Headless.Captcha.Turnstile

Cloudflare Turnstile verification (pass/fail, with `idempotency_key` and `cdata`) plus Razor tag helpers, contributed to the `AddHeadlessCaptcha` builder.

## Problem Solved

Provides server-side verification for Cloudflare Turnstile against the `turnstile/v0/siteverify` endpoint, surfaces Turnstile's provider-only data (`cdata`, Enterprise `metadata`) and its `idempotency_key` re-verification through a typed interface, and ships Razor tag helpers that render the Turnstile client script and widget — composed through the shared captcha builder in `Headless.Captcha.Core`.

## Key Features

- `setup.UseTurnstile(...)` - builder entry point, available as a default variant on `HeadlessCaptchaSetupBuilder` and a named variant on `HeadlessCaptchaInstanceBuilder` (added through `setup.AddNamed("name", i => i.UseTurnstile(...))`). Carries the overload trio in order: `IConfiguration`, `Action<TurnstileOptions>`, `Action<TurnstileOptions, IServiceProvider>`. `TurnstileOptions` is init-only, so bind config section `Headless:Captcha:Turnstile` (or object-initialize it inside the delegate).
- `ITurnstileVerifier : ICaptchaVerifier` - adds `new Task<TurnstileVerifyResult> VerifyAsync(TurnstileVerifyRequest, CancellationToken)`. Inject it to read Turnstile-only data; the base `ICaptchaVerifier` view returns pass/fail only.
- `TurnstileVerifyRequest : CaptchaVerifyRequest` - adds `string? IdempotencyKey`, so the same token can be safely re-verified without a duplicate-token error.
- `TurnstileVerifyResult : CaptchaVerifyResult` - adds `string? CData` (the `cdata` echoed back from the widget) and `JsonElement? Metadata` (the Enterprise `metadata` object when present). Turnstile returns no score.
- `ICaptchaLanguageCodeProvider` (shared with reCAPTCHA; default `CultureInfoCaptchaLanguageCodeProvider`) - supplies the `data-language` value on the widget; defaults to the current UI culture.
- Tag helpers: `<turnstile-script>` (props `ScriptAsync`, `ScriptDefer`, `ExplicitRender` → `?render=explicit`, `Onload`) rendering `turnstile/v0/api.js`; `<turnstile-widget>` (props `Theme`, `Size`, `Callback`, `ErrorCallback`, `ExpiredCallback`, `Action`, `CData`, `Language`) rendering `<div class="cf-turnstile" data-sitekey="…" …>`. They read the default Turnstile provider's options.

## Design Notes

- Turnstile is pass/fail with no score. The verdict is the policy — there is no numeric risk signal to threshold (unlike reCAPTCHA v3), so `TurnstileVerifyResult` adds only `CData` and `Metadata`, not a score.
- Native re-verification via `idempotency_key`. Set `TurnstileVerifyRequest.IdempotencyKey` to re-verify the same token without Cloudflare returning a duplicate-token error — useful for retried requests. The base `ICaptchaVerifier` path forwards the key only when the request is actually a `TurnstileVerifyRequest`.
- `VerifyBaseUrl` doubles as the script base. The same option drives both the siteverify endpoint and the `<turnstile-script>` API script URL, so a test stub or a self-hosted gateway is configured in one place.

## Installation

```bash
dotnet add package Headless.Captcha.Turnstile
```

## Quick Start

```csharp
using Headless.Captcha;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHeadlessCaptcha(captcha =>
    captcha.UseTurnstile(builder.Configuration.GetSection("Headless:Captcha:Turnstile"))
);

// Verify and read Turnstile-only data by injecting the concrete interface.
public sealed class ContactFormService(ITurnstileVerifier verifier)
{
    public async Task<bool> IsHumanAsync(string token, string? remoteIp, CancellationToken ct)
    {
        var result = await verifier.VerifyAsync(
            new TurnstileVerifyRequest
            {
                Response = token,
                RemoteIp = remoteIp,
                IdempotencyKey = Guid.NewGuid().ToString(),
            },
            ct
        );

        return result.Success; // result.CData / result.Metadata available when present
    }
}
```

Compose with a second provider (only one default allowed — the rest are named):

```csharp
builder.Services.AddHeadlessCaptcha(captcha =>
    captcha
        .UseTurnstile(builder.Configuration.GetSection("Headless:Captcha:Turnstile")) // default
        .AddNamed(
            "recaptcha",
            i => i.UseReCaptchaV3(builder.Configuration.GetSection("Headless:Captcha:ReCaptchaV3"))
        )
);

// Default (Turnstile) injects directly; the named "recaptcha" resolves through ICaptchaProvider.
```

Razor tag helpers (the default provider's widget/script):

```html
<turnstile-script explicit-render="false" />
<turnstile-widget theme="auto" action="login" />
```

## Configuration

```csharp
options.VerifyBaseUrl = "https://challenges.cloudflare.com/"; // base URL for siteverify and the client script (default)
options.SiteKey = "your-site-key"; // required — rendered into the client widget
options.SiteSecret = "your-secret-key"; // required — used for server-side verification
```

Bound from `Headless:Captcha:Turnstile` when using the `IConfiguration` overload. `TurnstileOptionsValidator` requires `SiteKey` and `SiteSecret` to be non-empty and `VerifyBaseUrl` to be an HTTP(S) URL, validated at startup.

## Dependencies

- `Headless.Captcha.Core`
- `Headless.Extensions`
- `Headless.Hosting`
- `Microsoft.AspNetCore.App` (framework reference, for the Razor tag helpers)
- `Microsoft.Extensions.Http.Resilience`

## Side Effects

- Each `UseTurnstile` registration adds a named `HttpClient` (with the standard resilience handler) pointed at `VerifyBaseUrl`, configures `TurnstileOptions` (with FluentValidation, validated on start), and registers a keyed `ITurnstileVerifier` + `ICaptchaVerifier` under the registration name. A default registration also adds the unkeyed `ITurnstileVerifier` / `ICaptchaVerifier`.
- Registers `ICaptchaLanguageCodeProvider` (`CultureInfoCaptchaLanguageCodeProvider`, transient, `TryAdd`) the first time a Turnstile provider is added.
- Tag helpers emit HTML/script output during Razor rendering. No background services.
