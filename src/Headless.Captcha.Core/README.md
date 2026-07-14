# Headless.Captcha.Core

Setup builder, registration gates, and the keyed captcha resolver for the CAPTCHA abstraction.

## Problem Solved

Owns the unified captcha setup builder (`AddHeadlessCaptcha`) and the `ICaptchaProvider` implementation, giving every provider one registration grammar (an optional default slot plus named instances over keyed DI) instead of each package hand-rolling its own `IServiceCollection` extension.

## Key Features

- `AddHeadlessCaptcha(Action<HeadlessCaptchaSetupBuilder>)` — the single provider-agnostic registration entry point, with an at-least-one-provider gate and a once-per-collection guard.
- `HeadlessCaptchaSetupBuilder` — the root builder with two slots (at most one default, unlimited named); a default is selected by a provider's `Use*` member, and `AddNamed(name, configure)` adds a named instance through a nested `HeadlessCaptchaInstanceBuilder`. The `RegisterDefault(providerKey, action)` / `RegisterProvider(action)` plumbing that provider `Use*` members build on is `[EditorBrowsable(Never)]`.
- `ICaptchaProvider` — registered automatically by the gate (keyed-service-backed via `KeyedServiceCaptchaProvider`); resolves named instances and a default provider's canonical key, and exposes `RegisteredNames`.
- Deferred registration: provider contributions are queued and run only after the gates pass — the default first, then each named instance — so a setup that fails a gate leaves the `IServiceCollection` unchanged.

## Design Notes

- The builder carries no shared, cross-provider feature options — it is provider-selection-only; each provider binds its own options inside its `Use*` member. The gate requires at least one provider (default or named), allows at most one default (rejecting a second default or a duplicate canonical key), and rejects a repeated `AddHeadlessCaptcha` on the same `IServiceCollection` (a marker service enforces the single-call rule).
- A default provider must register under a framework-reserved `providerKey` (under the `Headless.Captcha:` namespace) so its canonical alias cannot collide with a consumer-owned keyed service; a default is registered both unkeyed and under that canonical key, so it is reachable directly and through `ICaptchaProvider`. Named instances are keyed-only and must not use a reserved name.

## Installation

```bash
dotnet add package Headless.Captcha.Core
```

## Quick Start

```csharp
// Provider-agnostic registration entry point (a provider package supplies the Use* member):
builder.Services.AddHeadlessCaptcha(captcha =>
{
    // default (optional, at most one) — call the Use* member on the setup builder
    captcha.UseTurnstile(builder.Configuration.GetSection("Headless:Captcha:Turnstile"));

    // named instance, keyed "recaptcha" — one provider per AddNamed call
    captcha.AddNamed("recaptcha", i => i.UseReCaptchaV3(builder.Configuration.GetSection("Headless:Captcha:ReCaptchaV3")));
});

// Resolve a named verifier:
var verifier = serviceProvider.GetRequiredService<ICaptchaProvider>().GetVerifier("recaptcha");
```

## Configuration

No configuration required.

## Dependencies

- `Headless.Captcha.Abstractions`
- `Headless.Checks`
- `Microsoft.Extensions.DependencyInjection.Abstractions`

## Side Effects

`AddHeadlessCaptcha` registers a provider-registration marker and `ICaptchaProvider` (keyed-service-backed), then runs the default provider's wiring (an unkeyed verifier plus its canonical-key alias) followed by each named instance's wiring (keyed under the instance name). The marker enforces the single-call rule.
