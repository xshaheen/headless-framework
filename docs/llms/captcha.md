---
domain: Captcha
packages: Captcha.Abstractions, Captcha.ReCaptcha, Captcha.Turnstile
---

# Captcha

## Table of Contents

- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [Core Concepts](#core-concepts)
    - [The pass/fail abstraction](#the-passfail-abstraction)
    - [Default vs. named providers](#default-vs-named-providers)
    - [Keyed resolution](#keyed-resolution)
    - [Provider-only data](#provider-only-data)
- [Choosing a Provider](#choosing-a-provider)
- [Headless.Captcha.Abstractions](#headlesscaptchaabstractions)
    - [Problem Solved](#problem-solved)
    - [Key Features](#key-features)
    - [Design Notes](#design-notes)
    - [Installation](#installation)
    - [Quick Start](#quick-start)
    - [Configuration](#configuration)
    - [Dependencies](#dependencies)
    - [Side Effects](#side-effects)
- [Headless.Captcha.ReCaptcha](#headlesscaptcharecaptcha)
    - [Problem Solved](#problem-solved-1)
    - [Key Features](#key-features-1)
    - [Design Notes](#design-notes-1)
    - [Installation](#installation-1)
    - [Quick Start](#quick-start-1)
    - [Configuration](#configuration-1)
    - [Dependencies](#dependencies-1)
    - [Side Effects](#side-effects-1)
- [Headless.Captcha.Turnstile](#headlesscaptchaturnstile)
    - [Problem Solved](#problem-solved-2)
    - [Key Features](#key-features-2)
    - [Design Notes](#design-notes-2)
    - [Installation](#installation-2)
    - [Quick Start](#quick-start-2)
    - [Configuration](#configuration-2)
    - [Dependencies](#dependencies-2)
    - [Side Effects](#side-effects-2)

> Unified CAPTCHA verification abstraction with Google reCAPTCHA (v2 + v3) and Cloudflare Turnstile providers, composed through one `AddHeadlessCaptcha` builder.

## Quick Orientation

Install `Headless.Captcha.Abstractions` plus one or more provider packages. All registration flows through a single `services.AddHeadlessCaptcha(setup => ...)` call; provider packages contribute `Use*` extension members on the setup builder (`UseReCaptchaV2`, `UseReCaptchaV3`, `UseTurnstile`). Code against `ICaptchaVerifier` for the pass/fail outcome every provider returns; resolve a provider's concrete interface (`IReCaptchaV3Verifier`, `ITurnstileVerifier`) only when you need provider-only data.

- Visible checkbox / "I'm not a robot" challenge: `Headless.Captcha.ReCaptcha` with `setup.UseReCaptchaV2(...)`.
- Invisible, score-based risk signal (reCAPTCHA-only): `Headless.Captcha.ReCaptcha` with `setup.UseReCaptchaV3(...)` and inject `IReCaptchaV3Verifier` to read `Score`.
- Privacy-friendly pass/fail with native token re-verification: `Headless.Captcha.Turnstile` with `setup.UseTurnstile(...)`.

`ICaptchaVerifier.VerifyAsync(CaptchaVerifyRequest, CancellationToken)` posts the client token to the provider's siteverify endpoint and returns a normalized `CaptchaVerifyResult` (`Success`, `HostName`, `ChallengeTimestamp`, `Action`, `ErrorCodes`). At most one provider may be the *default* (registered with the no-name `Use*` overload): it resolves both unkeyed (`ICaptchaVerifier`) and under its canonical `CaptchaConstants` key. Additional providers are *named* (`Use*("name", ...)`), keyed-only, and resolved through `ICaptchaProvider.GetVerifier(name)`. Both reCAPTCHA and Turnstile providers ship Razor tag helpers for rendering the client-side widget/script.

## Agent Instructions

- Use `ICaptchaVerifier` from `Headless.Captcha.Abstractions` for pass/fail verification at application boundaries. Inject `IReCaptchaV3Verifier` (reCAPTCHA v3 `Score`) or `ITurnstileVerifier` (Turnstile `CData` / `Metadata`, `idempotency_key`) only where you actually read provider-only data — that code is provider-specific by definition.
- Configure every provider in one `services.AddHeadlessCaptcha(setup => ...)` call. At least one provider is required; calling `AddHeadlessCaptcha` twice on the same service collection throws `InvalidOperationException`, and a throwing setup leaves the service collection unchanged (contributions are deferred until the gates pass).
- Register at most one *default* provider — the no-name `UseReCaptchaV2(...)` / `UseReCaptchaV3(...)` / `UseTurnstile(...)` overload. A second default throws `InvalidOperationException`. Every additional provider must use the name-taking overload (`Use…("name", ...)`).
- A default provider resolves unkeyed (`ICaptchaVerifier` / its concrete interface) and under its canonical `CaptchaConstants` key. Named providers are keyed-only: resolve them with `ICaptchaProvider.GetVerifier(name)` / `GetVerifierOrNull(name)`, or inject the keyed concrete verifier.
- Do not name a provider with a value under the `Headless.Captcha:` namespace — those are the framework's reserved `CaptchaConstants` keys. `CaptchaConstants.IsReservedProviderKey(name)` is the check; the builder throws `ArgumentException` for a reserved name and `InvalidOperationException` for a duplicate name.
- `ICaptchaVerifier.VerifyAsync` throws `HttpRequestException` when the siteverify HTTP call is unsuccessful and `InvalidOperationException` when the response body cannot be deserialized. Always check `CaptchaVerifyResult.Success` before trusting the request; on failure read `ErrorCodes`.
- reCAPTCHA v3's `Score` (0.0–1.0) is exposed only on `ReCaptchaV3VerifyResult` via `IReCaptchaV3Verifier.VerifyAsync`. It has no Turnstile equivalent, so it lives off the base contract. Do not expect a score from `ICaptchaVerifier` or from Turnstile.
- Turnstile-only data — `CData` and the Enterprise `Metadata` (`JsonElement?`) — is on `TurnstileVerifyResult` via `ITurnstileVerifier.VerifyAsync(TurnstileVerifyRequest, ...)`. Set `TurnstileVerifyRequest.IdempotencyKey` to re-verify the same token without a duplicate-token error.
- The reCAPTCHA and Turnstile tag helpers read the **default** provider's options (the canonical `CaptchaConstants` key). They do not render a named provider's widget — if a provider must be both rendered via tag helpers and a default is needed, make it the default.
- Bind provider options from configuration with the `IConfiguration` overload; the canonical section names are `Headless:Captcha:ReCaptchaV2`, `Headless:Captcha:ReCaptchaV3`, and `Headless:Captcha:Turnstile`.
- The old standalone `Headless.ReCaptcha` package and its `AddReCaptchaV2` / `AddReCaptchaV3` `IServiceCollection` entry points were removed (greenfield, no compat shim). reCAPTCHA now ships as `Headless.Captcha.ReCaptcha` and registers only through `AddHeadlessCaptcha(b => b.UseReCaptchaV2/V3(...))`.

## Core Concepts

CAPTCHA verification is the same shape across every provider — post a client-issued token to the provider's server-side endpoint, get back a verdict — but the verdict's *extra* fields differ. The framework models the shared verdict as a base contract and pushes provider-only fields onto derived types, then composes providers through one keyed builder.

### The pass/fail abstraction

`ICaptchaVerifier` is the single application-facing contract. `CaptchaVerifyResult` carries only the fields every provider returns: `Success`, `HostName`, `ChallengeTimestamp`, `Action`, and `ErrorCodes`. This is deliberate — reCAPTCHA v3's numeric score and Turnstile's `cdata` have no cross-provider equivalent, so putting them on the base would force every consumer and every provider to model fields most of them cannot supply. Code that only needs "did this token pass?" depends on the base and stays provider-agnostic. See [The pass/fail abstraction](#design-notes) under the Abstractions package.

### Default vs. named providers

The setup builder has two slots. A *default* provider is registered with the no-name `Use*` overload: it contributes an **unkeyed** `ICaptchaVerifier` (so `ICaptchaVerifier` can be injected directly) **plus** a keyed alias under its canonical `CaptchaConstants` key. At most one default is allowed. A *named* provider is registered with the name-taking overload and is **keyed-only** — it has no unkeyed registration and must be resolved by name. This lets an app inject a single primary verifier directly while still composing additional providers (for example, a per-form provider choice) behind `ICaptchaProvider`.

### Keyed resolution

`ICaptchaProvider` resolves an `ICaptchaVerifier` by the name it was registered under: a named provider's name, or a default provider's canonical `CaptchaConstants` key. `GetVerifier(name)` throws `InvalidOperationException` when nothing is registered under the name; `GetVerifierOrNull(name)` returns `null`. Use it when the provider is chosen at runtime (for example, from a request field). When the provider is fixed and is the default, inject `ICaptchaVerifier` (or the concrete interface) directly instead.

### Provider-only data

Each provider's concrete interface extends `ICaptchaVerifier` with a typed `VerifyAsync` returning a derived result. `IReCaptchaV3Verifier.VerifyAsync` returns `ReCaptchaV3VerifyResult` (adds `Score`). `ITurnstileVerifier.VerifyAsync` returns `TurnstileVerifyResult` (adds `CData` and the Enterprise `Metadata`) and accepts `TurnstileVerifyRequest` (adds `IdempotencyKey`). A consumer reading any of these is, by definition, writing provider-specific code, so it should resolve/inject the concrete interface — not the base. reCAPTCHA v2 has no provider-only data and implements the plain `ICaptchaVerifier`.

## Choosing a Provider

Pick by the verdict shape you need and the privacy/UX trade-off you accept.

| Provider | Use when | Avoid when | Trade-off |
| --- | --- | --- | --- |
| `Headless.Captcha.ReCaptcha` (v2) | You want an explicit, visible "I'm not a robot" checkbox or image challenge the user actively solves. | You need a frictionless, invisible flow, or you require a numeric risk score. | A visible challenge with user friction, but an unambiguous human-solved signal. Pass/fail only. |
| `Headless.Captcha.ReCaptcha` (v3) | You want an invisible, score-based risk signal you act on yourself (e.g. block below a threshold, step up above it). | You need a hard pass/fail challenge with no policy logic, or you cannot accept Google as a dependency. | A numeric `Score` (0.0–1.0) with no user friction, but you own the threshold policy and the score is reCAPTCHA-only (not portable to other providers). |
| `Headless.Captcha.Turnstile` | You want a privacy-friendly, mostly invisible challenge with native token re-verification (`idempotency_key`) and optional `cdata`. | You specifically need a numeric risk score for your own policy logic. | Privacy-friendly pass/fail with `idempotency_key` and `cdata`, but no score — the verdict is the policy. |

`Headless.Captcha.Abstractions` is not a provider; it defines `ICaptchaVerifier`, the request/result contracts, the `AddHeadlessCaptcha` builder, and `ICaptchaProvider`. Reference it from application code so call sites stay provider-agnostic, and add at least one provider package.

reCAPTCHA v2 and v3 ship in the **same** `Headless.Captcha.ReCaptcha` package — they are two builder entry points (`UseReCaptchaV2` / `UseReCaptchaV3`), two canonical keys, and two sets of tag helpers, sharing one `ReCaptchaOptions` and one options section family. Turnstile ships separately in `Headless.Captcha.Turnstile`.

---

## Headless.Captcha.Abstractions

The provider-agnostic CAPTCHA contracts and the unified registration builder; referenced by application code and by every provider package.

### Problem Solved

Provides a single pass/fail verification API (`ICaptchaVerifier`) and one composition entry point (`AddHeadlessCaptcha`) so applications can verify CAPTCHA tokens without binding their call sites to a specific vendor (Google reCAPTCHA, Cloudflare Turnstile), and can compose more than one provider behind a keyed resolver.

### Key Features

- `ICaptchaVerifier` - the shared contract: `Task<CaptchaVerifyResult> VerifyAsync(CaptchaVerifyRequest request, CancellationToken cancellationToken = default)`. Throws `HttpRequestException` on an unsuccessful siteverify HTTP response and `InvalidOperationException` when the body cannot be deserialized.
- `CaptchaVerifyRequest` - the request inputs: `required string Response` (the client widget token) and optional `string? RemoteIp`. Providers with extra inputs extend it (Turnstile's `TurnstileVerifyRequest`).
- `CaptchaVerifyResult` - the normalized outcome: `bool Success`, `DateTimeOffset? ChallengeTimestamp`, `string? HostName`, `string? Action`, `string[]? ErrorCodes`. Strictly pass/fail; provider-only fields live on derived result types.
- `HeadlessCaptchaSetupBuilder` - the root builder for `AddHeadlessCaptcha`, with two slots (one optional default, unlimited named). `RegisterDefault(providerKey, action)` and `RegisterNamed(name, action)` are the hooks provider packages call from their `Use*` extensions.
- `ICaptchaProvider` - keyed resolver: `ICaptchaVerifier GetVerifier(string name)` (throws when missing) and `ICaptchaVerifier? GetVerifierOrNull(string name)` (returns `null`). Resolves both named instances and a default provider's canonical key.
- `CaptchaConstants` - the canonical keyed-DI keys: `ReCaptchaV2Provider = "Headless.Captcha:ReCaptchaV2"`, `ReCaptchaV3Provider = "Headless.Captcha:ReCaptchaV3"`, `TurnstileProvider = "Headless.Captcha:Turnstile"`, plus `bool IsReservedProviderKey(string name)` (true for any name under the `Headless.Captcha:` namespace).
- `IServiceCollection.AddHeadlessCaptcha(Action<HeadlessCaptchaSetupBuilder> configure)` - the single registration entry point. Requires at least one provider and rejects a second call on the same service collection.

### Design Notes

- The base contract stays pass/fail. `CaptchaVerifyResult` carries only fields every provider returns; reCAPTCHA v3's `Score` and Turnstile's `CData` / `Metadata` live on derived result types. A consumer reading provider-only data is writing provider-specific code, so it resolves the provider's concrete verifier/result — keeping `ICaptchaVerifier` consumers vendor-portable.
- At most one default provider. `RegisterDefault` throws `InvalidOperationException` on a second default (and on a duplicate canonical key). A default registers both an unkeyed verifier and a keyed alias under its canonical key, so it is reachable directly and through `ICaptchaProvider`. Register every additional provider with the name-taking overload.
- Names under `Headless.Captcha:` are reserved. `RegisterNamed` rejects a reserved name with `ArgumentException` and a duplicate name with `InvalidOperationException`, so consumer-chosen names cannot collide with the framework's canonical keys.
- Registration is deferred and all-or-nothing. Provider `Use*` extensions queue their contributions on the builder; `AddHeadlessCaptcha` runs them only after the at-least-one-provider gate passes, so a throwing setup leaves the service collection unchanged. A second `AddHeadlessCaptcha` call on the same collection throws.

### Installation

```bash
dotnet add package Headless.Captcha.Abstractions
```

### Quick Start

```csharp
using Headless.Captcha;

// Verify a token through the default provider, provider-agnostically.
public sealed class SignupService(ICaptchaVerifier verifier)
{
    public async Task<bool> IsHumanAsync(string token, string? remoteIp, CancellationToken ct)
    {
        var result = await verifier.VerifyAsync(
            new CaptchaVerifyRequest { Response = token, RemoteIp = remoteIp },
            ct
        );

        return result.Success;
    }
}

// Resolve a provider chosen at runtime through the keyed resolver.
public sealed class MultiProviderService(ICaptchaProvider captchaProvider)
{
    public async Task<bool> IsHumanAsync(string providerName, string token, CancellationToken ct)
    {
        var verifier = captchaProvider.GetVerifier(providerName); // throws if not registered
        var result = await verifier.VerifyAsync(new CaptchaVerifyRequest { Response = token }, ct);

        return result.Success;
    }
}
```

### Configuration

None. This is an abstractions package; provider options (`ReCaptchaOptions`, `TurnstileOptions`) are configured on the provider packages through the builder.

### Dependencies

- `Headless.Checks`
- `Microsoft.Extensions.DependencyInjection.Abstractions`

### Side Effects

- `AddHeadlessCaptcha` registers an internal singleton marker (used to reject a second call) and `ICaptchaProvider` as a singleton (`KeyedServiceCaptchaProvider`). Provider registrations are contributed by the provider packages' `Use*` extensions. No background services, no file system or network effects at registration time.

---

## Headless.Captcha.ReCaptcha

Google reCAPTCHA v2 (visible checkbox) and v3 (invisible score) verification plus Razor tag helpers, contributed to the `AddHeadlessCaptcha` builder.

### Problem Solved

Provides server-side verification for both Google reCAPTCHA v2 and v3 against Google's `recaptcha/api/siteverify` endpoint, exposes v3's numeric risk score through a typed interface, and ships Razor tag helpers that render the client-side script and widget — all composed through the shared captcha builder.

### Key Features

- `setup.UseReCaptchaV2(...)` / `setup.UseReCaptchaV3(...)` - builder entry points (default and name-taking variants), each with the overload trio: `Action<ReCaptchaOptions>`, `Action<ReCaptchaOptions, IServiceProvider>`, and `IConfiguration`. Bind config section `Headless:Captcha:ReCaptchaV2` / `Headless:Captcha:ReCaptchaV3`.
- `IReCaptchaV3Verifier : ICaptchaVerifier` - adds `new Task<ReCaptchaV3VerifyResult> VerifyAsync(CaptchaVerifyRequest, CancellationToken)`. Inject it to read the score; the base `ICaptchaVerifier` view returns pass/fail only.
- reCAPTCHA v2 implements the plain `ICaptchaVerifier` (no provider-only data) — resolve it as `ICaptchaVerifier` (default unkeyed, or keyed by name / `CaptchaConstants.ReCaptchaV2Provider`).
- `ReCaptchaV3VerifyResult : CaptchaVerifyResult` - adds `float Score` (0.0–1.0 on a successful verify; defaults to `0f` on failure — `Success` is authoritative for pass/fail).
- `ReCaptchaError` enum and `ReCaptchaResultExtensions.ToReCaptchaErrors(this IReCaptchaVerifyResult)` - parse the raw `ErrorCodes` into the typed reCAPTCHA error enum.
- `IReCaptchaLanguageCodeProvider` (+ `CultureInfoReCaptchaLanguageCodeProvider`) - supplies the `?hl=` language code appended to the script URL; defaults to the current UI culture.
- Tag helpers: `<recaptcha-script-v2>`, `<recaptcha-div-v2>`, the `recaptcha-v2-*` attribute element helper, `<recaptcha-script-v3>`, and `<recaptcha-script-v3-js>`. They read the default provider's options under the matching canonical key.

### Design Notes

- v2 and v3 are one package, two entry points. They share `ReCaptchaOptions` and the `recaptcha/api/siteverify` endpoint but register under distinct canonical keys (`ReCaptchaV2Provider` / `ReCaptchaV3Provider`) and ship distinct tag helpers, because v2 is a visible challenge and v3 is a score signal.
- The score is on the v3 result only. v3 returns a per-request score with no user friction; that score has no equivalent in v2 or Turnstile, so it lives on `ReCaptchaV3VerifyResult` behind `IReCaptchaV3Verifier`. You own the threshold policy (for example, treat `Score < 0.5f` as suspicious).
- Breaking rename from `Headless.ReCaptcha`. The standalone `AddReCaptchaV2` / `AddReCaptchaV3` `IServiceCollection` entry points and the old `IReCaptchaSiteVerifyV2` / `IReCaptchaSiteVerifyV3` interfaces were removed (greenfield, no compat shim). Migrate to `AddHeadlessCaptcha(b => b.UseReCaptchaV2/V3(...))` and `IReCaptchaV3Verifier` / `ICaptchaVerifier`.

### Installation

```bash
dotnet add package Headless.Captcha.ReCaptcha
```

### Quick Start

```csharp
using Headless.Captcha;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHeadlessCaptcha(captcha =>
    captcha.UseReCaptchaV3(options =>
    {
        options.SiteKey = builder.Configuration["Headless:Captcha:ReCaptchaV3:SiteKey"]!;
        options.SiteSecret = builder.Configuration["Headless:Captcha:ReCaptchaV3:SiteSecret"]!;
    })
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

### Configuration

```csharp
options.VerifyBaseUrl = "https://www.google.com/"; // base URL of the reCAPTCHA API (default)
options.SiteKey = "your-site-key";                 // required — rendered into the client widget/script
options.SiteSecret = "your-secret-key";            // required — used for server-side verification
```

Bound from `Headless:Captcha:ReCaptchaV2` / `Headless:Captcha:ReCaptchaV3` when using the `IConfiguration` overload. `ReCaptchaOptionsValidator` requires `SiteKey` and `SiteSecret` to be non-empty and `VerifyBaseUrl` to be an HTTP(S) URL, validated at startup.

### Dependencies

- `Headless.Captcha.Abstractions`
- `Headless.Hosting`
- `Microsoft.AspNetCore.App` (framework reference, for the Razor tag helpers — SDK `Headless.NET.Sdk.Razor`)
- `Microsoft.Extensions.Http.Resilience`

### Side Effects

- Each `UseReCaptchaV2` / `UseReCaptchaV3` registration adds a named `HttpClient` (with the standard resilience handler) pointed at `VerifyBaseUrl`, configures `ReCaptchaOptions` (with FluentValidation, validated on start), and registers a keyed `ICaptchaVerifier` (v2) / keyed `IReCaptchaV3Verifier` + `ICaptchaVerifier` (v3) under the registration name. A default registration also adds the unkeyed verifier(s).
- Registers `IReCaptchaLanguageCodeProvider` (transient, `TryAdd`) the first time a reCAPTCHA provider is added.
- Tag helpers emit HTML/script output during Razor rendering. No background services.

---

## Headless.Captcha.Turnstile

Cloudflare Turnstile verification (pass/fail, with `idempotency_key` and `cdata`) plus Razor tag helpers, contributed to the `AddHeadlessCaptcha` builder.

### Problem Solved

Provides server-side verification for Cloudflare Turnstile against the `turnstile/v0/siteverify` endpoint, surfaces Turnstile's provider-only data (`cdata`, Enterprise `metadata`) and its `idempotency_key` re-verification through a typed interface, and ships Razor tag helpers that render the Turnstile client script and widget — composed through the shared captcha builder.

### Key Features

- `setup.UseTurnstile(...)` - builder entry point (default and name-taking variants), each with the overload trio: `Action<TurnstileOptions>`, `Action<TurnstileOptions, IServiceProvider>`, and `IConfiguration`. Binds config section `Headless:Captcha:Turnstile`.
- `ITurnstileVerifier : ICaptchaVerifier` - adds `new Task<TurnstileVerifyResult> VerifyAsync(TurnstileVerifyRequest, CancellationToken)`. Inject it to read Turnstile-only data; the base `ICaptchaVerifier` view returns pass/fail only.
- `TurnstileVerifyRequest : CaptchaVerifyRequest` - adds `string? IdempotencyKey`, so the same token can be safely re-verified without a duplicate-token error.
- `TurnstileVerifyResult : CaptchaVerifyResult` - adds `string? CData` (the `cdata` echoed back from the widget) and `JsonElement? Metadata` (the Enterprise `metadata` object when present). Turnstile returns no score.
- `ITurnstileLanguageCodeProvider` (+ `CultureInfoTurnstileLanguageCodeProvider`) - supplies the `data-language` value on the widget; defaults to the current UI culture.
- Tag helpers: `<turnstile-script>` (props `ScriptAsync`, `ScriptDefer`, `ExplicitRender` → `?render=explicit`, `Onload`) rendering `turnstile/v0/api.js`; `<turnstile-widget>` (props `Theme`, `Size`, `Callback`, `ErrorCallback`, `ExpiredCallback`, `Action`, `CData`, `Language`) rendering `<div class="cf-turnstile" data-sitekey="…" …>`. They read the default Turnstile provider's options.

### Design Notes

- Turnstile is pass/fail with no score. The verdict is the policy — there is no numeric risk signal to threshold (unlike reCAPTCHA v3), so `TurnstileVerifyResult` adds only `CData` and `Metadata`, not a score.
- Native re-verification via `idempotency_key`. Set `TurnstileVerifyRequest.IdempotencyKey` to re-verify the same token without Cloudflare returning a duplicate-token error — useful for retried requests. The base `ICaptchaVerifier` path forwards the key only when the request is actually a `TurnstileVerifyRequest`.
- `VerifyBaseUrl` doubles as the script base. The same option drives both the siteverify endpoint and the `<turnstile-script>` API script URL, so a test stub or a self-hosted gateway is configured in one place.

### Installation

```bash
dotnet add package Headless.Captcha.Turnstile
```

### Quick Start

```csharp
using Headless.Captcha;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHeadlessCaptcha(captcha =>
    captcha.UseTurnstile(options =>
    {
        options.SiteKey = builder.Configuration["Headless:Captcha:Turnstile:SiteKey"]!;
        options.SiteSecret = builder.Configuration["Headless:Captcha:Turnstile:SiteSecret"]!;
    })
);

// Verify and read Turnstile-only data by injecting the concrete interface.
public sealed class ContactFormService(ITurnstileVerifier verifier)
{
    public async Task<bool> IsHumanAsync(string token, string? remoteIp, CancellationToken ct)
    {
        var result = await verifier.VerifyAsync(
            new TurnstileVerifyRequest { Response = token, RemoteIp = remoteIp, IdempotencyKey = Guid.NewGuid().ToString() },
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
        .UseTurnstile(o =>
        {
            o.SiteKey = builder.Configuration["Headless:Captcha:Turnstile:SiteKey"]!;
            o.SiteSecret = builder.Configuration["Headless:Captcha:Turnstile:SiteSecret"]!;
        })
        .UseReCaptchaV3("recaptcha", o =>
        {
            o.SiteKey = builder.Configuration["Headless:Captcha:ReCaptchaV3:SiteKey"]!;
            o.SiteSecret = builder.Configuration["Headless:Captcha:ReCaptchaV3:SiteSecret"]!;
        })
);

// Default (Turnstile) injects directly; the named "recaptcha" resolves through ICaptchaProvider.
```

Razor tag helpers (the default provider's widget/script):

```html
<turnstile-script explicit-render="false" />
<turnstile-widget theme="auto" action="login" />
```

### Configuration

```csharp
options.VerifyBaseUrl = "https://challenges.cloudflare.com/"; // base URL for siteverify and the client script (default)
options.SiteKey = "your-site-key";                            // required — rendered into the client widget
options.SiteSecret = "your-secret-key";                       // required — used for server-side verification
```

Bound from `Headless:Captcha:Turnstile` when using the `IConfiguration` overload. `TurnstileOptionsValidator` requires `SiteKey` and `SiteSecret` to be non-empty and `VerifyBaseUrl` to be an HTTP(S) URL, validated at startup.

### Dependencies

- `Headless.Captcha.Abstractions`
- `Headless.Hosting`
- `Microsoft.AspNetCore.App` (framework reference, for the Razor tag helpers — SDK `Headless.NET.Sdk.Razor`)
- `Microsoft.Extensions.Http.Resilience`

### Side Effects

- Each `UseTurnstile` registration adds a named `HttpClient` (with the standard resilience handler) pointed at `VerifyBaseUrl`, configures `TurnstileOptions` (with FluentValidation, validated on start), and registers a keyed `ITurnstileVerifier` + `ICaptchaVerifier` under the registration name. A default registration also adds the unkeyed `ITurnstileVerifier` / `ICaptchaVerifier`.
- Registers `ITurnstileLanguageCodeProvider` (transient, `TryAdd`) the first time a Turnstile provider is added.
- Tag helpers emit HTML/script output during Razor rendering. No background services.
