# Headless.Captcha.ReCaptcha

Google reCAPTCHA v2 (visible checkbox) and v3 (invisible score) verification plus Razor tag helpers, contributed to the `AddHeadlessCaptcha` builder.

## Problem Solved

Provides server-side verification for both Google reCAPTCHA v2 and v3 against Google's `recaptcha/api/siteverify` endpoint, exposes v3's numeric risk score through a typed interface, and ships Razor tag helpers that render the client-side script and widget — all composed through the shared captcha builder in `Headless.Captcha.Core`.

## Key Features

- `setup.UseReCaptchaV2(...)` / `setup.UseReCaptchaV3(...)` - builder entry points, available as default variants on `HeadlessCaptchaSetupBuilder` and named variants on `HeadlessCaptchaInstanceBuilder` (added through `setup.AddNamed("name", i => i.UseReCaptchaV3(...))`). Each carries the overload trio in order: `IConfiguration`, `Action<ReCaptchaOptions>`, `Action<ReCaptchaOptions, IServiceProvider>`. `ReCaptchaOptions` is settable, so any overload works — bind config section `Headless:Captcha:ReCaptchaV2` / `Headless:Captcha:ReCaptchaV3`, or set its properties inside the delegate.
- `IReCaptchaV3Verifier : ICaptchaVerifier` - adds `new Task<ReCaptchaV3VerifyResult> VerifyAsync(CaptchaVerifyRequest, CancellationToken)`. Inject it to read the score; the base `ICaptchaVerifier` view returns pass/fail only.
- reCAPTCHA v2 implements the plain `ICaptchaVerifier` (no provider-only data beyond the base contract) — resolve it as `ICaptchaVerifier` (default unkeyed, or keyed by name / `CaptchaConstants.ReCaptchaV2Provider`). Returns a `ReCaptchaV2VerifyResult` at runtime.
- `ReCaptchaV3VerifyResult : CaptchaVerifyResult, IReCaptchaVerifyResult` - adds `float Score` (0.0–1.0; defaults to `0f` on failure when no score is present in the wire response).
- `ReCaptchaV2VerifyResult : CaptchaVerifyResult, IReCaptchaVerifyResult` - concrete v2 result type; carries no extra fields beyond the base contract.
- `ReCaptchaError` enum and `ReCaptchaResultExtensions.ToReCaptchaErrors(this IReCaptchaVerifyResult)` - parse the raw `ErrorCodes` into the typed reCAPTCHA error enum. Scoped to `IReCaptchaVerifyResult` so it does not appear on unrelated types such as `TurnstileVerifyResult`.
- `ICaptchaLanguageCodeProvider` (shared with Turnstile; default `CultureInfoCaptchaLanguageCodeProvider`) - supplies the `?hl=` language code appended to the script URL; defaults to the current UI culture.
- Tag helpers: `<recaptcha-script-v2>`, `<recaptcha-div-v2>`, the `recaptcha-v2-*` attribute element helper, `<recaptcha-script-v3>`, and `<recaptcha-script-v3-js>`. They read the default provider's options under the matching canonical key.

## Design Notes

- v2 and v3 are one package, two entry points. They share `ReCaptchaOptions` and the `recaptcha/api/siteverify` endpoint but register under distinct canonical keys (`CaptchaConstants.ReCaptchaV2Provider` / `ReCaptchaV3Provider`) and ship distinct tag helpers, because v2 is a visible challenge and v3 is a score signal.
- The score is on the v3 result only. v3 returns a per-request score with no user friction; that score has no equivalent in v2 or Turnstile, so it lives on `ReCaptchaV3VerifyResult` behind `IReCaptchaV3Verifier`. You own the threshold policy (for example, treat `Score < 0.5f` as suspicious).
- Breaking rename from `Headless.ReCaptcha`. The standalone `AddReCaptchaV2` / `AddReCaptchaV3` `IServiceCollection` entry points and the old `IReCaptchaSiteVerifyV2` / `IReCaptchaSiteVerifyV3` interfaces were removed (greenfield, no compat shim). Migrate to `AddHeadlessCaptcha(b => b.UseReCaptchaV2/V3(...))` and `IReCaptchaV3Verifier` / `ICaptchaVerifier`.

## Installation

```bash
dotnet add package Headless.Captcha.ReCaptcha
```

## Quick Start

```csharp
using Headless.Captcha;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHeadlessCaptcha(captcha =>
    captcha.UseReCaptchaV3(builder.Configuration.GetSection("Headless:Captcha:ReCaptchaV3"))
);

// Read the v3 score by injecting the concrete interface.
public sealed class LoginService(IReCaptchaV3Verifier verifier)
{
    public async Task<bool> IsLikelyHumanAsync(string token, CancellationToken ct)
    {
        var result = await verifier.VerifyAsync(new CaptchaVerifyRequest { Response = token }, ct);

        return result.Success && result.Score >= 0.5f;
    }
}
```

Razor tag helpers (the widget/script for the default provider):

```html
<!-- reCAPTCHA v3 -->
<recaptcha-script-v3 hide-badge="true" />
<recaptcha-script-v3-js action="login" callback="onRecaptchaVerified" />

<!-- reCAPTCHA v2 -->
<recaptcha-script-v2 />
<recaptcha-div-v2 />
```

## Configuration

```csharp
options.VerifyBaseUrl = "https://www.google.com/"; // base URL of the reCAPTCHA API (default)
options.SiteKey = "your-site-key"; // required — rendered into the client widget/script
options.SiteSecret = "your-secret-key"; // required — used for server-side verification
```

Bound from `Headless:Captcha:ReCaptchaV2` / `Headless:Captcha:ReCaptchaV3` when using the `IConfiguration` overload. `ReCaptchaOptionsValidator` requires `SiteKey` and `SiteSecret` to be non-empty and `VerifyBaseUrl` to be an HTTP(S) URL, validated at startup.

## Dependencies

- `Headless.Captcha.Core`
- `Headless.Extensions`
- `Headless.Hosting`
- `Microsoft.AspNetCore.App` (framework reference, for the Razor tag helpers)
- `Microsoft.Extensions.Http.Resilience`

## Side Effects

- Each `UseReCaptchaV2` / `UseReCaptchaV3` registration adds a named `HttpClient` (with the standard resilience handler) pointed at `VerifyBaseUrl`, configures `ReCaptchaOptions` (with FluentValidation, validated on start), and registers a keyed `ICaptchaVerifier` (v2) / keyed `IReCaptchaV3Verifier` + `ICaptchaVerifier` (v3) under the registration name. A default registration also adds the unkeyed verifier(s).
- Registers `ICaptchaLanguageCodeProvider` (`CultureInfoCaptchaLanguageCodeProvider`, transient, `TryAdd`) the first time a reCAPTCHA provider is added.
- Tag helpers emit HTML/script output during Razor rendering. No background services.
