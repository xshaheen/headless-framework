# Headless.Captcha.Abstractions

The provider-agnostic CAPTCHA contracts and the unified registration builder; referenced by application code and by every provider package.

## Problem Solved

Provides a single pass/fail verification API (`ICaptchaVerifier`) and one composition entry point (`AddHeadlessCaptcha`) so applications can verify CAPTCHA tokens without binding their call sites to a specific vendor (Google reCAPTCHA, Cloudflare Turnstile), and can compose more than one provider behind a keyed resolver.

## Key Features

- `ICaptchaVerifier` - the shared contract: `Task<CaptchaVerifyResult> VerifyAsync(CaptchaVerifyRequest request, CancellationToken cancellationToken = default)`. Throws `HttpRequestException` on an unsuccessful siteverify HTTP response and `InvalidOperationException` when the body cannot be deserialized.
- `CaptchaVerifyRequest` - the request inputs: `required string Response` (the client widget token) and optional `string? RemoteIp`. Providers with extra inputs extend it (Turnstile's `TurnstileVerifyRequest`).
- `CaptchaVerifyResult` - the normalized outcome: `bool Success`, `DateTimeOffset? ChallengeTimestamp`, `string? HostName`, `string? Action`, `string[]? ErrorCodes`. Strictly pass/fail; provider-only fields live on derived result types.
- `HeadlessCaptchaSetupBuilder` - the root builder for `AddHeadlessCaptcha`, with two slots (one optional default, unlimited named). `RegisterDefault(providerKey, action)` and `RegisterNamed(name, action)` are the hooks provider packages call from their `Use*` extensions.
- `ICaptchaProvider` - keyed resolver: `ICaptchaVerifier GetVerifier(string name)` (throws when missing, listing the registered names), `ICaptchaVerifier? GetVerifierOrNull(string name)` (returns `null`), and `IReadOnlySet<string> RegisteredNames` (every named instance plus a default provider's canonical key — use it to validate an externally supplied name before resolving). Resolves both named instances and a default provider's canonical key.
- `CaptchaConstants` - the canonical keyed-DI keys: `ReCaptchaV2Provider = "Headless.Captcha:ReCaptchaV2"`, `ReCaptchaV3Provider = "Headless.Captcha:ReCaptchaV3"`, `TurnstileProvider = "Headless.Captcha:Turnstile"`, plus `bool IsReservedProviderKey(string name)` (true for any name under the `Headless.Captcha:` namespace).
- `IServiceCollection.AddHeadlessCaptcha(Action<HeadlessCaptchaSetupBuilder> configure)` - the single registration entry point. Requires at least one provider and rejects a second call on the same service collection.

## Design Notes

- The base contract stays pass/fail. `CaptchaVerifyResult` carries only fields every provider returns; reCAPTCHA v3's `Score` and Turnstile's `CData` / `Metadata` live on derived result types. A consumer reading provider-only data is writing provider-specific code, so it resolves the provider's concrete verifier/result — keeping `ICaptchaVerifier` consumers vendor-portable.
- At most one default provider. `RegisterDefault` requires a framework-reserved `providerKey` (under the `Headless.Captcha:` namespace) so the default's canonical alias cannot collide with a consumer-owned keyed service; it throws `ArgumentException` for a non-reserved key and `InvalidOperationException` on a second default (or a duplicate canonical key). A default registers both an unkeyed verifier and a keyed alias under its canonical key, so it is reachable directly and through `ICaptchaProvider`. Register every additional provider with the name-taking overload.
- Names under `Headless.Captcha:` are reserved. `RegisterNamed` rejects a reserved name with `ArgumentException` and a duplicate name with `InvalidOperationException`, so consumer-chosen names cannot collide with the framework's canonical keys.
- Registration is deferred and all-or-nothing. Provider `Use*` extensions queue their contributions on the builder; `AddHeadlessCaptcha` runs them only after the at-least-one-provider gate passes, so a throwing setup leaves the service collection unchanged. A second `AddHeadlessCaptcha` call on the same collection throws.

## Installation

```bash
dotnet add package Headless.Captcha.Abstractions
```

## Quick Start

```csharp
using Headless.Captcha;

// Verify a token through the default provider, provider-agnostically.
public sealed class SignupService(ICaptchaVerifier verifier)
{
    public async Task<bool> IsHumanAsync(string token, string? remoteIp, CancellationToken ct)
    {
        var result = await verifier.VerifyAsync(new CaptchaVerifyRequest { Response = token, RemoteIp = remoteIp }, ct);

        return result.Success;
    }
}

// Resolve a provider chosen at runtime through the keyed resolver.
public sealed class MultiProviderService(ICaptchaProvider captchaProvider)
{
    public async Task<bool> IsHumanAsync(string providerName, string token, CancellationToken ct)
    {
        // For an untrusted (request-supplied) name, probe with GetVerifierOrNull rather than the throwing
        // GetVerifier; ICaptchaProvider.RegisteredNames lists the valid names if you prefer to validate up front.
        var verifier = captchaProvider.GetVerifierOrNull(providerName);

        if (verifier is null)
        {
            return false; // unknown provider name
        }

        var result = await verifier.VerifyAsync(new CaptchaVerifyRequest { Response = token }, ct);

        return result.Success;
    }
}
```

Add at least one provider package and compose every provider in one call. At most one provider may be the default (the no-name overload); register the rest by name:

```csharp
builder.Services.AddHeadlessCaptcha(captcha =>
    captcha
        .UseTurnstile(o =>
        {
            o.SiteKey = "…";
            o.SiteSecret = "…";
        }) // default
        .UseReCaptchaV3(
            "recaptcha",
            o =>
            {
                o.SiteKey = "…";
                o.SiteSecret = "…";
            }
        ) // named
);
```

## Configuration

None. This is an abstractions package; provider options (`ReCaptchaOptions`, `TurnstileOptions`) are configured on the provider packages through the builder.

## Dependencies

- `Headless.Checks`
- `Microsoft.Extensions.DependencyInjection.Abstractions`

## Side Effects

- `AddHeadlessCaptcha` registers an internal singleton marker (used to reject a second call) and `ICaptchaProvider` as a singleton (`KeyedServiceCaptchaProvider`). Provider registrations are contributed by the provider packages' `Use*` extensions. No background services, no file system or network effects at registration time.
