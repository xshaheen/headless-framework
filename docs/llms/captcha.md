---
domain: Captcha
packages: Captcha.Abstractions, Captcha, Captcha.ReCaptcha, Captcha.Turnstile
---

# Captcha

> Server-side CAPTCHA verification through `ICaptchaVerifier`, with Google reCAPTCHA (v2 and v3) and Cloudflare Turnstile providers and Razor tag helpers for their widgets.

## Orientation

Install `Headless.Captcha.Abstractions` and one or more provider packages. Each provider package brings `Headless.Captcha`, which owns `AddHeadlessCaptcha`.

| Need | Package | Call |
| --- | --- | --- |
| A visible "I'm not a robot" challenge | `Headless.Captcha.ReCaptcha` | `UseReCaptchaV2(...)` |
| An invisible risk score you can act on | `Headless.Captcha.ReCaptcha` | `UseReCaptchaV3(...)`, then inject `IReCaptchaV3Verifier` to read `Score` |
| A privacy-friendly pass or fail, with token re-verification | `Headless.Captcha.Turnstile` | `UseTurnstile(...)` |

Register every provider in one `AddHeadlessCaptcha` call: at most one **default** provider, chosen by a `Use*` call on the setup builder, plus any number of **named** providers added with `setup.AddNamed(name, i => i.Use…())`. Inject `ICaptchaVerifier` for the default. Resolve a named provider with `ICaptchaProvider.GetVerifier("name")`.

`ICaptchaVerifier.VerifyAsync(CaptchaVerifyRequest, CancellationToken)` posts the client token to the provider's siteverify endpoint and returns a `CaptchaVerifyResult`.

```csharp
builder.Services.AddHeadlessCaptcha(captcha =>
    captcha.UseTurnstile(builder.Configuration.GetSection("Headless:Captcha:Turnstile"))
);
```

**Verify:** start the host. A missing `SiteKey` or `SiteSecret`, or an invalid `VerifyBaseUrl`, fails the start with `OptionsValidationException`. Under `RunHeadlessAsync`, `dotnet MyApp.dll check` prints `Host check failed: ...` and exits 1 for the same mistake. Validation does not prove the secret is right: verify one token from the real widget and assert `result.Success`. A wrong secret makes the verification fail, either as `Success = false` or as a thrown `HttpRequestException`.

## Agent Rules

- Inject `ICaptchaVerifier` for a pass-or-fail check. Inject `IReCaptchaV3Verifier` only to read the v3 `Score`, and `ITurnstileVerifier` only to read `CData` or `Metadata` or to send an `IdempotencyKey`.
- Configure every provider in one `AddHeadlessCaptcha` call. It requires at least one provider (default or named). A second call on the same service collection, zero providers, or a second default throws `InvalidOperationException`. A setup that throws leaves the service collection unchanged.
- Add every provider after the first as a named instance: `setup.AddNamed("name", i => i.Use…(...))`, with exactly one provider per name.
- Do not name a provider with a value that starts with `Headless.Captcha:`. Those names are the reserved `CaptchaConstants` keys, and `CaptchaConstants.IsReservedProviderKey(name)` tests for them. `AddNamed` throws `ArgumentException` for a reserved name and `InvalidOperationException` for a duplicate name.
- Expect `VerifyAsync` to throw when the provider cannot answer: `HttpRequestException` for an unsuccessful siteverify response, and `InvalidOperationException` for a body it cannot read. It does not return a failed result for an outage, unlike `ISmsSender` and `IEmailSender`. Catch the exception and refuse the request, so an outage never lets a request through.
- Check `CaptchaVerifyResult.Success` before you trust the request. On failure, read `ErrorCodes`.
- For reCAPTCHA v3, set `ExpectedAction` to the action the form sends, and set `MinimumScore`. The default `MinimumScore` is `0`, so any scored token passes until you set it. Register one named v3 provider per action when forms send different actions.
- Set `ExpectedHostName` on each provider. Without it, a token solved on another site that uses your site key verifies.
- Render widgets only for the default provider. The tag helpers read the default provider's options and ignore named providers, so make a provider the default when a page renders its widget.
- Register reCAPTCHA through `AddHeadlessCaptcha(c => c.UseReCaptchaV2(...))` or `c.UseReCaptchaV3(...)`. No `AddReCaptchaV2` or `AddReCaptchaV3` method exists on `IServiceCollection`.

---

## Core Concepts

### The pass-or-fail contract

`CaptchaVerifyResult` carries only the fields every provider returns: `Success`, `HostName`, `ChallengeTimestamp`, `Action`, and `ErrorCodes`. Provider-only data lives on derived types behind each provider's own interface:

| Interface | Request | Result adds |
| --- | --- | --- |
| `ICaptchaVerifier` (reCAPTCHA v2, and the base view of every provider) | `CaptchaVerifyRequest` | Nothing |
| `IReCaptchaV3Verifier` | `CaptchaVerifyRequest` | `Score` on `ReCaptchaV3VerifyResult` |
| `ITurnstileVerifier` | `TurnstileVerifyRequest` (adds `IdempotencyKey`) | `CData` and `Metadata` on `TurnstileVerifyResult` |

Code that depends only on `ICaptchaVerifier` works with any provider.

### Default and named providers

- **Default.** A `Use*` call on the setup builder. It resolves unkeyed (`ICaptchaVerifier` and its own interface) and also under its `CaptchaConstants` key (`ReCaptchaV2Provider`, `ReCaptchaV3Provider`, or `TurnstileProvider`). At most one.
- **Named.** Added with `AddNamed`. It is keyed only: resolve it by name.

`ICaptchaProvider` resolves both: a named provider by its name, and the default by its `CaptchaConstants` key.

- `GetVerifier(name)` throws `InvalidOperationException` when nothing is registered under the name, listing the registered names.
- `GetVerifierOrNull(name)` returns `null`.
- `RegisteredNames` lists every named provider plus the default's `CaptchaConstants` key.

Use `ICaptchaProvider` when the provider is chosen at run time, for example from a request field. When the provider is fixed and is the default, inject the verifier directly.

`ICaptchaProvider` returns only `ICaptchaVerifier`. To read provider-only data from a named provider, inject its keyed interface: `[FromKeyedServices("login")] IReCaptchaV3Verifier` for `Score`, or `[FromKeyedServices("contact")] ITurnstileVerifier` for `CData`. Every provider is a singleton.

Each provider's `HttpClient` is named by its registration key: the `CaptchaConstants` key for the default, or the instance name for a named provider. To add a proxy or a handler, configure that client: `services.AddHttpClient(CaptchaConstants.TurnstileProvider).ConfigurePrimaryHttpMessageHandler(...)`.

## Choosing a Provider

| Provider | Use when | Avoid when | Trade-off |
| --- | --- | --- | --- |
| reCAPTCHA v2 | You want a visible checkbox or image challenge the user solves. | You need an invisible flow or a risk score. | User friction, but a clear human-solved signal. Pass or fail only. |
| reCAPTCHA v3 | You want an invisible score and your own policy on it, such as asking for a second factor between two thresholds. | You need a hard challenge with no policy, or cannot depend on Google. | No friction. `Score` is reCAPTCHA-only; Turnstile and v2 have no equivalent. |
| Turnstile | You want a privacy-friendly, mostly invisible check, with safe re-verification and `cdata`. | You need a numeric score. | No score: the verdict is the policy. |

reCAPTCHA v2 and v3 ship in the same `Headless.Captcha.ReCaptcha` package. They share `ReCaptchaOptions` but register under separate keys and have separate tag helpers.

---

## Headless.Captcha.Abstractions

The provider-agnostic contracts: `ICaptchaVerifier`, the request and result types, `ICaptchaProvider`, and `CaptchaConstants`.

### Setup

```bash
dotnet add package Headless.Captcha.Abstractions
```

```csharp
using Headless.Captcha;

// Verify a token through the default provider.
public sealed class SignupService(ICaptchaVerifier verifier)
{
    public async Task<bool> IsHumanAsync(string token, string? remoteIp, CancellationToken ct)
    {
        var result = await verifier.VerifyAsync(new CaptchaVerifyRequest { Response = token, RemoteIp = remoteIp }, ct);

        return result.Success;
    }
}

// Resolve a provider chosen at run time.
public sealed class MultiProviderService(ICaptchaProvider captchaProvider)
{
    public async Task<bool> IsHumanAsync(string providerName, string token, CancellationToken ct)
    {
        // The name comes from the request, so use the non-throwing lookup.
        var verifier = captchaProvider.GetVerifierOrNull(providerName);

        if (verifier is null)
        {
            return false;
        }

        var result = await verifier.VerifyAsync(new CaptchaVerifyRequest { Response = token }, ct);

        return result.Success;
    }
}
```

**Verify:** in a test, substitute `ICaptchaVerifier` and assert that your code rejects a result with `Success = false` and that it refuses the request when `VerifyAsync` throws `HttpRequestException`.

### Design and runtime behavior

- `ICaptchaVerifier.VerifyAsync(CaptchaVerifyRequest request, CancellationToken cancellationToken = default)` returns `Task<CaptchaVerifyResult>`.
- `CaptchaVerifyRequest` has `required string Response` (the client token) and an optional `string? RemoteIp`.
- `CaptchaVerifyResult` has `bool Success`, `DateTimeOffset? ChallengeTimestamp`, `string? HostName`, `string? Action`, and `IReadOnlyList<string>? ErrorCodes`.
- `CaptchaConstants` holds the reserved keys: `ReCaptchaV2Provider` (`"Headless.Captcha:ReCaptchaV2"`), `ReCaptchaV3Provider` (`"Headless.Captcha:ReCaptchaV3"`), `TurnstileProvider` (`"Headless.Captcha:Turnstile"`), and `IsReservedProviderKey(name)`.
- `ICaptchaLanguageCodeProvider` supplies the widget language. The default implementation uses the current UI culture. Register your own to override it.

---

## Headless.Captcha

The registration entry point, `AddHeadlessCaptcha`. Each provider package references it.

### Setup

```bash
dotnet add package Headless.Captcha
```

```csharp
using Headless.Captcha;

builder.Services.AddHeadlessCaptcha(captcha =>
{
    // Default: at most one.
    captcha.UseTurnstile(builder.Configuration.GetSection("Headless:Captcha:Turnstile"));

    // Named, keyed "recaptcha": one provider per AddNamed call.
    captcha.AddNamed("recaptcha", i => i.UseReCaptchaV3(builder.Configuration.GetSection("Headless:Captcha:ReCaptchaV3")));
});

var verifier = provider.GetRequiredService<ICaptchaProvider>().GetVerifier("recaptcha");
```

**Verify:** a registration mistake throws from `AddHeadlessCaptcha`, before the host builds. In a test, assert that `ICaptchaProvider.RegisteredNames` contains each named provider and the default's `CaptchaConstants` key.

### Design and runtime behavior

- `AddHeadlessCaptcha(Action<HeadlessCaptchaSetupBuilder>)` registers `ICaptchaProvider`, then the default provider (unkeyed and under its `CaptchaConstants` key), then each named provider.
- `HeadlessCaptchaSetupBuilder` carries no shared options. Each provider binds its own options in its `Use*` call.
- The default `ICaptchaLanguageCodeProvider`, `CultureInfoCaptchaLanguageCodeProvider`, ships here. Providers register it as transient only when no other implementation is registered.

---

## Headless.Captcha.ReCaptcha

Google reCAPTCHA v2 (visible challenge) and v3 (invisible score) verification, plus Razor tag helpers.

### Setup

```bash
dotnet add package Headless.Captcha.ReCaptcha
```

`UseReCaptchaV2` and `UseReCaptchaV3` each take `IConfiguration`, `Action<ReCaptchaOptions>`, or `Action<ReCaptchaOptions, IServiceProvider>`, on the setup builder (default) or a named instance. Bind from `Headless:Captcha:ReCaptchaV2` or `Headless:Captcha:ReCaptchaV3`.

```csharp
using Headless.Captcha;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHeadlessCaptcha(captcha =>
    captcha.UseReCaptchaV3(builder.Configuration.GetSection("Headless:Captcha:ReCaptchaV3"))
);

// Read the v3 score by injecting the provider's interface.
public sealed class LoginService(IReCaptchaV3Verifier verifier)
{
    public async Task<bool> IsLikelyHumanAsync(string token, CancellationToken ct)
    {
        var result = await verifier.VerifyAsync(new CaptchaVerifyRequest { Response = token }, ct);

        // Success already includes MinimumScore, ExpectedAction, and ExpectedHostName.
        // Read Score only for a finer policy, such as asking for a second factor below 0.7.
        return result.Success;
    }
}
```

Razor tag helpers render the default provider's widget and script:

```html
<!-- reCAPTCHA v3 -->
<recaptcha-script-v3 hide-badge="true" />
<recaptcha-script-v3-js action="login" callback="onRecaptchaVerified" />

<!-- reCAPTCHA v2 -->
<recaptcha-script-v2 />
<recaptcha-div-v2 />
```

**Verify:** start the host; invalid options fail the start with `OptionsValidationException`. Then verify a token from the rendered widget and assert `result.Success`. For v3, log `Score` on real traffic before you raise `MinimumScore`.

### Configuration

```csharp
options.VerifyBaseUrl = "https://www.google.com/"; // default
options.SiteKey = "your-site-key"; // required; rendered into the widget
options.SiteSecret = "your-secret-key"; // required; used for server-side verification
options.MinimumScore = 0.5f; // v3 only; 0.0 to 1.0 (default 0: no threshold)
options.ExpectedAction = "login"; // v3 only (default null: not checked)
options.ExpectedHostName = "example.com"; // v2 and v3 (default null: not checked)
```

- `SiteKey` and `SiteSecret` must not be empty.
- `MinimumScore` must be between 0 and 1.
- `ExpectedAction` and `ExpectedHostName` must be `null` or non-blank.
- `VerifyBaseUrl` must be HTTPS, or HTTP on loopback for a test server, with no user info.

### Design and runtime behavior

- **Verdict.** `Success` is the full verdict, not Google's raw `success` flag. Google's flag says only that the token is valid for the site key: a bot gets a valid v3 token with a low score, and a token minted for one page's action verifies on any other. A v3 result passes only with a score of at least `MinimumScore`. A v3 response with no score fails, even when `MinimumScore` is `0`. When set, the action must equal `ExpectedAction` (case-sensitive), and the host name must equal `ExpectedHostName` (ignoring case). v2 checks only the host name.
- **Error codes.** A failed check sets `Success = false` and adds `score-too-low`, `action-mismatch`, or `hostname-mismatch` to `ErrorCodes`. `result.ToReCaptchaErrors()` parses `ErrorCodes` into `ReCaptchaError` values, including `ScoreTooLow`, `ActionMismatch`, and `HostNameMismatch`.
- **Score.** `ReCaptchaV3VerifyResult.Score` is between 0.0 and 1.0 when Google returns one, otherwise `0`. It is reported even when the score check fails.
- **No retry.** The siteverify POST is never retried, because a token is single-use and a replay fails. A host-wide resilience handler, such as `AddHeadless()` service defaults, is removed from this client so it cannot retry either.
- **HTTP failure.** Before `VerifyAsync` throws for an unsuccessful response, it logs a warning with the status code. The response body is logged only when `IHostEnvironment.IsDevelopment()` is true, because it can echo request detail. A host with no environment counts as Production.
- **Language.** The script URL gets `?hl=` from `ICaptchaLanguageCodeProvider`.
- **Tag helpers.** `<recaptcha-script-v2>`, `<recaptcha-div-v2>`, the `recaptcha-v2-*` attributes on any element (`recaptcha-v2-theme`, `recaptcha-v2-size`, `recaptcha-v2-badge`, `recaptcha-v2-tab-index`, `recaptcha-v2-callback`, `recaptcha-v2-expired-callback`, `recaptcha-v2-error-callback`), `<recaptcha-script-v3>`, and `<recaptcha-script-v3-js>`.

---

## Headless.Captcha.Turnstile

Cloudflare Turnstile verification, with `idempotency_key` and `cdata`, plus Razor tag helpers.

### Setup

```bash
dotnet add package Headless.Captcha.Turnstile
```

`UseTurnstile` takes `IConfiguration`, `Action<TurnstileOptions>`, or `Action<TurnstileOptions, IServiceProvider>`, on the setup builder (default) or a named instance. Bind from `Headless:Captcha:Turnstile`.

```csharp
using Headless.Captcha;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHeadlessCaptcha(captcha =>
    captcha.UseTurnstile(builder.Configuration.GetSection("Headless:Captcha:Turnstile"))
);

// Read Turnstile-only data by injecting the provider's interface.
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

        return result.Success; // result.CData and result.Metadata when present
    }
}
```

With a second provider, make one the default and name the rest:

```csharp
builder.Services.AddHeadlessCaptcha(captcha =>
    captcha
        .UseTurnstile(builder.Configuration.GetSection("Headless:Captcha:Turnstile")) // default
        .AddNamed(
            "recaptcha",
            i => i.UseReCaptchaV3(builder.Configuration.GetSection("Headless:Captcha:ReCaptchaV3"))
        )
);
```

Razor tag helpers render the default Turnstile provider's widget and script:

```html
<turnstile-script explicit-render="false" />
<turnstile-widget theme="auto" action="login" />
```

**Verify:** start the host; invalid options fail the start with `OptionsValidationException`. Then verify a token from the rendered widget and assert `result.Success`.

### Configuration

```csharp
options.VerifyBaseUrl = "https://challenges.cloudflare.com/"; // default; also the client script base
options.SiteKey = "your-site-key"; // required; rendered into the widget
options.SiteSecret = "your-secret-key"; // required; used for server-side verification
options.ExpectedAction = "login"; // default null: not checked
options.ExpectedHostName = "example.com"; // default null: not checked
```

- `SiteKey` and `SiteSecret` must not be empty.
- `VerifyBaseUrl` must be HTTPS, or HTTP on loopback for a test server, with no user info. It sets both the siteverify endpoint and the `<turnstile-script>` URL, so a test stub needs one setting.

### Design and runtime behavior

- **Verdict.** When set, the token's action must equal `ExpectedAction` (case-sensitive) and its host name must equal `ExpectedHostName` (ignoring case). A mismatch returns `Success = false` with `action-mismatch` or `hostname-mismatch` in `ErrorCodes`. Unset, the verdict says only that the token is valid for the site key.
- **No score.** `TurnstileVerifyResult` adds `CData` (the `cdata` the widget echoed) and `Metadata` (the Enterprise `metadata` object as a `JsonElement?`), but no score.
- **Re-verification.** Set `TurnstileVerifyRequest.IdempotencyKey` to verify the same token again without a duplicate-token error, for example on a retried request. Through the base `ICaptchaVerifier`, the key is sent only when the request object is a `TurnstileVerifyRequest`.
- **No retry.** The siteverify POST is never retried, and a host-wide resilience handler is removed from this client, as for reCAPTCHA.
- **HTTP failure.** Logged as for reCAPTCHA: a warning with the status code, and the body only in Development.
- **Tag helpers.** `<turnstile-script>` (properties `ScriptAsync`, `ScriptDefer`, `ExplicitRender`, `Onload`) renders `turnstile/v0/api.js`. `<turnstile-widget>` (properties `Theme`, `Size`, `Callback`, `ErrorCallback`, `ExpiredCallback`, `Action`, `CData`, `Language`) renders `<div class="cf-turnstile" data-sitekey="…">`. The widget's `data-language` defaults to the value from `ICaptchaLanguageCodeProvider`.
