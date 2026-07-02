---
domain: SMS
packages: Sms.Abstractions, Sms.Core, Sms.Aws, Sms.Cequens, Sms.Connekio, Sms.Dev, Sms.Infobip, Sms.Twilio, Sms.VictoryLink, Sms.Vodafone
---

# SMS

## Table of Contents

- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [Core Concepts](#core-concepts)
    - [Default and named clients](#default-and-named-clients)
    - [Message model](#message-model)
    - [Result model](#result-model)
    - [Retry safety](#retry-safety)
- [Choosing a Provider](#choosing-a-provider)
- [Headless.Sms.Abstractions](#headlesssmsabstractions)
    - [Problem Solved](#problem-solved)
    - [Key Features](#key-features)
    - [Installation](#installation)
    - [Quick Start](#quick-start)
    - [Configuration](#configuration)
    - [Dependencies](#dependencies)
    - [Side Effects](#side-effects)
- [Headless.Sms.Core](#headlesssmscore)
    - [Problem Solved](#problem-solved-1)
    - [Key Features](#key-features-1)
    - [Design Notes](#design-notes)
    - [Installation](#installation-1)
    - [Quick Start](#quick-start-1)
    - [Configuration](#configuration-1)
    - [Dependencies](#dependencies-1)
    - [Side Effects](#side-effects-1)
- [Headless.Sms.Aws](#headlesssmsaws)
    - [Problem Solved](#problem-solved-2)
    - [Key Features](#key-features-2)
    - [Installation](#installation-2)
    - [Quick Start](#quick-start-2)
    - [Configuration](#configuration-2)
    - [Dependencies](#dependencies-2)
    - [Side Effects](#side-effects-2)
- [Headless.Sms.Cequens](#headlesssmscequens)
    - [Problem Solved](#problem-solved-3)
    - [Key Features](#key-features-3)
    - [Design Notes](#design-notes-1)
    - [Installation](#installation-3)
    - [Quick Start](#quick-start-3)
    - [Configuration](#configuration-3)
    - [Dependencies](#dependencies-3)
    - [Side Effects](#side-effects-3)
- [Headless.Sms.Connekio](#headlesssmsconnekio)
    - [Problem Solved](#problem-solved-4)
    - [Key Features](#key-features-4)
    - [Design Notes](#design-notes-2)
    - [Installation](#installation-4)
    - [Quick Start](#quick-start-4)
    - [Configuration](#configuration-4)
    - [Dependencies](#dependencies-4)
    - [Side Effects](#side-effects-4)
- [Headless.Sms.Dev](#headlesssmsdev)
    - [Problem Solved](#problem-solved-5)
    - [Key Features](#key-features-5)
    - [Installation](#installation-5)
    - [Quick Start](#quick-start-5)
    - [Configuration](#configuration-5)
    - [Dependencies](#dependencies-5)
    - [Side Effects](#side-effects-5)
- [Headless.Sms.Infobip](#headlesssmsinfobip)
    - [Problem Solved](#problem-solved-6)
    - [Key Features](#key-features-6)
    - [Installation](#installation-6)
    - [Quick Start](#quick-start-6)
    - [Configuration](#configuration-6)
    - [Dependencies](#dependencies-6)
    - [Side Effects](#side-effects-6)
- [Headless.Sms.Twilio](#headlesssmstwilio)
    - [Problem Solved](#problem-solved-7)
    - [Key Features](#key-features-7)
    - [Installation](#installation-7)
    - [Quick Start](#quick-start-7)
    - [Configuration](#configuration-7)
    - [Dependencies](#dependencies-7)
    - [Side Effects](#side-effects-7)
- [Headless.Sms.VictoryLink](#headlesssmsvictorylink)
    - [Problem Solved](#problem-solved-8)
    - [Key Features](#key-features-8)
    - [Installation](#installation-8)
    - [Quick Start](#quick-start-8)
    - [Configuration](#configuration-8)
    - [Dependencies](#dependencies-8)
    - [Side Effects](#side-effects-8)
- [Headless.Sms.Vodafone](#headlesssmsvodafone)
    - [Problem Solved](#problem-solved-9)
    - [Key Features](#key-features-9)
    - [Design Notes](#design-notes-3)
    - [Installation](#installation-9)
    - [Quick Start](#quick-start-9)
    - [Configuration](#configuration-9)
    - [Dependencies](#dependencies-9)
    - [Side Effects](#side-effects-9)

> Provider-agnostic SMS sending with pluggable backends for international (Twilio, AWS SNS, Infobip) and regional MENA providers (Cequens, Connekio, VictoryLink, Vodafone).

## Quick Orientation

Install `Headless.Sms.Abstractions` plus one provider package. Register with `AddHeadlessSms(setup => setup.Use…())` — at most one **default** `Use*` provider per call (the default is optional; a named-only host is supported), plus any number of **named** senders via `setup.AddNamed(name, i => i.Use…())`. Code against `ISmsSender` (single recipient) or `IBulkSmsSender` (multi-recipient, where supported) for the default; resolve named senders with `ISmsSenderProvider.GetSender("name")` or `[FromKeyedServices("name")] ISmsSender`. Never reference provider-specific sender types in application code — swap providers by changing DI registration only.

- **Development/testing**: `Headless.Sms.Dev` — `UseDevelopment(path)` appends messages to a local file or `UseNoop()` discards them. No external calls.
- **International**: `Headless.Sms.Twilio` (most popular), `Headless.Sms.Aws` (AWS SNS), `Headless.Sms.Infobip` (global platform).
- **MENA regional**: `Headless.Sms.Cequens`, `Headless.Sms.Connekio`, `Headless.Sms.VictoryLink`, `Headless.Sms.Vodafone`.

`Headless.Sms.Core` owns registration (`AddHeadlessSms`, `HeadlessSmsSetupBuilder`, `HeadlessSmsInstanceBuilder`) and the `ISmsSenderProvider` implementation over keyed DI. Providers pull it transitively — you rarely install it directly. `Headless.Sms.Abstractions` holds contracts only (`ISmsSender`, `IBulkSmsSender`, `ISmsSenderProvider`, request/response types, `SmsFailureKinds`).

Register additional **named** senders alongside an optional default: `setup.AddNamed("otp", i => i.UseTwilio(…))`. Resolve them with `ISmsSenderProvider.GetSender("otp")` or `[FromKeyedServices("otp")] ISmsSender`. The default sender is optional; when configured it resolves as the unkeyed `ISmsSender` (with no default, the unkeyed `ISmsSender` is simply not registered). Each named sender is keyed under its name and isolates its own provider, options, HttpClient (and resilience pipeline), and backend state.

## Agent Instructions

- Register at most one **default** provider per container: `services.AddHeadlessSms(setup => setup.Use…())`. The default is optional — zero defaults is allowed (a named-only host). Multiple default providers in one delegate, or a repeated `AddHeadlessSms` on the same `IServiceCollection`, throws `InvalidOperationException` at registration time. The available default `Use*` calls are `UseTwilio`, `UseAwsSns`, `UseInfobip`, `UseCequens`, `UseConnekio`, `UseVictoryLink`, `UseVodafone`, `UseDevelopment`, `UseNoop` — the same set is available on each named instance.
- Add **named** senders in the same call: `setup.AddNamed("name", i => i.Use…())`. Names must be non-whitespace and ordinal-unique within the call, and each named instance must select exactly one provider — a duplicate name, whitespace name, or zero/multiple providers throws at registration time. The default sender is optional; a named-only host (no default) is supported — the unkeyed `ISmsSender` is simply not registered when no default is configured.
- Resolve a named sender with `ISmsSenderProvider.GetSender("name")` (throws `InvalidOperationException` naming `AddNamed` when unregistered) / `GetSenderOrNull("name")` (returns `null`), or raw keyed DI (`[FromKeyedServices("name")] ISmsSender`, `GetRequiredKeyedService<ISmsSender>(name)`). Both `GetSender` and `GetSenderOrNull` throw `ArgumentException` on a null/whitespace name. The default (unkeyed) `ISmsSender` is **not** exposed through `ISmsSenderProvider`. To validate an externally supplied name before resolving, check `ISmsSenderProvider.RegisteredNames` (the registered named-instance names, an `IReadOnlySet<string>`; the default is excluded) instead of probing `GetSenderOrNull` and handling `null`.
- Registration is deferred: provider contributions are queued and nothing touches the `IServiceCollection` until the gates pass, so a setup that throws leaves the collection unchanged. The same provider can back two different names with fully independent options.
- Each named instance isolates its own options (validated per name via FluentValidation + `ValidateOnStart`), its own HttpClient (`Headless:{Provider}Sms:{name}`) with its own resilience pipeline (HTTP providers), and its own backend state. Keyed DI does not cascade the key to constructor dependencies, so named senders never read the default configuration.
- Code against `ISmsSender` from `Headless.Sms.Abstractions` — never against provider-specific sender types (`TwilioSmsSender`, `AwsSnsSmsSender`, `CequensSmsSender`, etc.).
- Always use `Headless.Sms.Dev` in development/test environments to avoid sending real SMS messages. Use `AddHeadlessSms(setup => setup.UseDevelopment("sms-log.txt"))` for file logging or `AddHeadlessSms(setup => setup.UseNoop())` for silent discard.
- `SendSingleSmsRequest` targets exactly one recipient via `Destination` (a single `SmsRequestDestination`) plus `Text` — not `To`/`Message`. A destination requires a country calling code (`Code`) and the local number (`Number`) as separate fields.
- Bulk capability is per instance: `IBulkSmsSender.SendBulkAsync(SendBulkSmsRequest)` is implemented by Cequens, Connekio, Infobip, VictoryLink, Vodafone, and the Dev/Noop senders; Twilio and AWS SNS do not (one recipient per API call), so resolving `IBulkSmsSender` for them fails — loop `SendAsync` instead. For a bulk-capable **named** instance, resolve the keyed `IBulkSmsSender` (`[FromKeyedServices("name")] IBulkSmsSender`); it forwards to the same instance as the keyed `ISmsSender`.
- `SendSingleSmsResponse` exposes `Success` (bool), optional `ProviderMessageId` (string? — the backend's message id on success when it returns one), `FailureError` (string? — non-null when `Success` is false), and `FailureKind` (the `SmsFailureKind` enum classifying failures). It does NOT have `IsSuccess` or `ErrorMessage`.
- `ISmsSender.SendAsync` returns `ValueTask<SendSingleSmsResponse>` and never throws for provider/transport failures — only `OperationCanceledException` (on cancellation) and argument-validation exceptions (for a null request, a null destination, or an empty body) propagate. Always check `response.Success`.
- HTTP-backed providers (Cequens, Connekio, Infobip, Twilio, VictoryLink, Vodafone) disable auto-retry by default to prevent duplicate SMS delivery. Opt back in per instance via the `configureResilience` parameter if the provider supports idempotency keys.
- For Twilio, the option property for the sender number is `PhoneNumber` (not `From`) and the account identifier is `Sid` (not `AccountSid`). There is no `MessagingServiceSid` option in the current API.
- For Infobip, the base URL option is `BasePath` (not `BaseUrl`) and the sender name option is `Sender` (not `SenderName`).
- For Vodafone, authentication uses `AccountId` + `Password` + `SecureHash` — not OAuth2/ClientId/ClientSecret.
- Do not call provider SDKs (Twilio REST API, AWS SNS, etc.) directly from application code; use `ISmsSender`.

## Core Concepts

### Default and named clients

A host registers an optional **default** sender plus any number of **named** senders in a single `AddHeadlessSms` call. This mirrors the Emails feature's named-instance pattern (`IEmailSenderProvider` + `AddNamed` + keyed registrations).

```csharp
builder.Services.AddHeadlessSms(setup =>
{
    setup.UseTwilio(builder.Configuration.GetSection("Sms:Twilio"));                  // default (optional)
    setup.AddNamed("otp", i => i.UseCequens(builder.Configuration.GetSection("Sms:Otp")));        // named, keyed "otp"
    setup.AddNamed("marketing", i => i.UseInfobip(builder.Configuration.GetSection("Sms:Bulk"))); // named, keyed "marketing"
});
```

- **Default is optional, named is additive.** The gate rejects more than one default provider but allows zero; named instances are unbounded and exempt from the default gate. The unkeyed `ISmsSender` resolves only when a default is configured — a named-only host is supported.
- **Names are validated at registration time.** Each name must be non-whitespace and ordinal-unique within the call; each named instance must select exactly one provider. Violations throw `ArgumentException` / `InvalidOperationException`.
- **Resolution.** Named senders resolve two ways — as keyed services (`[FromKeyedServices("otp")] ISmsSender`) and through `ISmsSenderProvider.GetSender(name)` (throws when unregistered) / `GetSenderOrNull(name)` (returns `null`). `ISmsSenderProvider.RegisteredNames` enumerates the registered named instances (default excluded) for validating a name before resolving. The default sender resolves as the unkeyed `ISmsSender` and is **not** exposed through `ISmsSenderProvider`.
- **Isolation.** Each named instance keys its options and backend under its name: per-name options (`IOptionsMonitor<TOptions>.Get(name)`, validated on start), a per-name HttpClient named `Headless:{Provider}Sms:{name}` with its own resilience pipeline (HTTP providers), and any keyed backend client (Twilio's `ITwilioRestClient`, AWS SNS's `IAmazonSimpleNotificationService`, Cequens' per-instance token cache). .NET keyed registrations do not cascade the key to a type's constructor dependencies, so every keyed sender/client is an explicit factory — named SMS never flows through the default configuration.
- **Bulk per instance.** For bulk-capable providers each named instance also registers a keyed `IBulkSmsSender` forwarding to the same keyed sender; resolve it with `[FromKeyedServices("name")] IBulkSmsSender`. Twilio and AWS SNS register no bulk forward (default or named).

### Message model

`SendSingleSmsRequest` is the single-recipient message type used by `ISmsSender`:

```csharp
new SendSingleSmsRequest
{
    MessageId = "optional-idempotency-id",          // optional, provider-specific use
    Destination = new SmsRequestDestination(20, "1234567890"), // Code = country calling code
    Text = "Your OTP is 123456",
    Properties = null                               // optional, provider-specific metadata
}
```

`SmsRequestDestination(int Code, string Number)` models a phone number split into country calling code and subscriber number. `ToString()` returns `"{Code}{Number}"` without a plus sign; `ToString(hasPlusPrefix: true)` returns `"+{Code}{Number}"`.

For multi-recipient sends, `SendBulkSmsRequest` carries a `Destinations` list (plus `Text` and optional `MessageId`/`Properties`) and is sent via `IBulkSmsSender.SendBulkAsync`, returning a `SendBulkSmsResponse`. Bulk support varies by provider (see the capability note above).

### Result model

`SendSingleSmsResponse` is returned by every provider. It is a closed type constructed only via factory methods:

```csharp
SendSingleSmsResponse.Succeeded()                                 // Success = true, FailureKind = None
SendSingleSmsResponse.Succeeded("provider-message-id")            // carries the backend's message id
SendSingleSmsResponse.Failed("reason")                            // Success = false, FailureKind = Unknown
SendSingleSmsResponse.Failed("reason", SmsFailureKind.Transient)  // classified failure
SendSingleSmsResponse.FromException(exception)                    // failure with a non-empty message, classified via SmsFailureKinds.FromException
```

Check `response.Success` after every send. `FailureError` is guaranteed non-null when `Success` is false (enforced by `[MemberNotNullWhen(false, nameof(FailureError))]`); `Failed` throws on a null/empty reason. `ProviderMessageId` carries the backend's message id on success when the provider returns one (Twilio SID, AWS SNS message id, Infobip bulk id), and is `null` otherwise. `FailureKind` (`SmsFailureKind`: `None`, `Unknown`, `Transient`, `RateLimited`, `InvalidRecipient`, `AuthFailure`, `OutOfCredit`) classifies failures so callers can decide whether to retry or switch providers — transport/network faults — including the standard resilience pipeline's timeout, open-circuit, and rate-limiter rejections — are reported as `Transient` via the shared `SmsFailureKinds.FromException` helper, and `SendSingleSmsResponse.FromException` pairs that classification with a guaranteed non-empty message (an overload takes an explicit kind). Provider-specific failures are classified from each provider's own contract — AWS SNS from its typed SDK exceptions, Infobip from its delivery status group — and stay `Unknown` when the backend documents no machine-readable signal (kinds are never inferred from generic HTTP status semantics).

`IBulkSmsSender.SendBulkAsync` returns a `SendBulkSmsResponse` with one `SmsRecipientResult` (`Destination` + a per-recipient `SendSingleSmsResponse`) per recipient, plus `AllSucceeded`/`AnySucceeded` and an optional `ProviderBatchId`. Providers that return per-recipient detail (Infobip) populate each result individually; providers whose API reports a single batch status (Cequens, Connekio, VictoryLink, Vodafone) apply that one outcome to every recipient.

### Retry safety

SMS sends are not idempotent by default. Re-sending on transient failure can cause the recipient to receive the same message twice. All HTTP-backed providers therefore disable the standard resilience-handler retry pipeline. If a provider issues idempotency keys, pass `configureResilience` (per default or named instance) to opt back in selectively.

---

## Choosing a Provider

| Provider | Use When | Avoid When | Trade-offs |
|---|---|---|---|
| **Twilio** | Global coverage needed, mature REST API, rich ecosystem | Cost is a primary concern at high volume | Highest per-message cost; excellent deliverability and support |
| **Aws (SNS)** | Already on AWS, want unified billing | Need a dedicated sender ID in all regions (SNS sender ID support varies) | Tightly coupled to AWS credentials model; `MaxPrice` cap prevents accidental overruns |
| **Infobip** | Global platform with delivery reporting via `BasePath` URL | No need for delivery receipts | API-key auth; `BasePath` must be the Infobip-assigned URL for your account |
| **Cequens** | MENA market, JWT token-based auth preferred | Outside MENA; need guaranteed idempotent resend | Auto-refreshes JWT tokens; disables retry by default |
| **Connekio** | MENA market, username/password/accountId auth | Outside MENA; need delivery receipts | Supports single and batch sends; disables retry by default |
| **VictoryLink** | Middle East market, simple username/password | Outside Middle East | Minimal auth model; response-code-based error handling |
| **Vodafone** | Egypt/Vodafone enterprise SMS | Outside Vodafone coverage area | Requires `SecureHash` in addition to credentials; fixed endpoint |
| **Dev** | Development and testing | Production | No external calls; file-based logging or silent discard |

---

## Headless.Sms.Abstractions

Defines the unified interface and message contract for SMS sending.

### Problem Solved

Provides a provider-agnostic SMS sending API so application code stays decoupled from the underlying gateway (Twilio, AWS SNS, Cequens, etc.). Provider selection is a DI registration concern only.

### Key Features

- `ISmsSender` — single-recipient send: `SendAsync(SendSingleSmsRequest, CancellationToken) : ValueTask<SendSingleSmsResponse>`.
- `IBulkSmsSender` — optional capability for multi-recipient sends: `SendBulkAsync(SendBulkSmsRequest, CancellationToken) : ValueTask<SendBulkSmsResponse>`. Only implemented by providers with native bulk support.
- `ISmsSenderProvider` — resolves named senders by name: `GetSender(name)` (throws when unregistered) and `GetSenderOrNull(name)` (returns `null`), plus `RegisteredNames` (`IReadOnlySet<string>`) listing the registered named instances (the default is excluded) so an externally supplied name can be validated before resolving. Backed by the container's keyed `ISmsSender` registrations; the concrete implementation lives in `Headless.Sms.Core`.
- `SendSingleSmsRequest` — single-recipient message contract with `Destination` (one `SmsRequestDestination`), `Text`, optional `MessageId`, and optional `Properties`.
- `SendBulkSmsRequest` — bulk message contract with `Destinations` (list), `Text`, optional `MessageId`/`Properties`.
- `SmsRequestDestination(int Code, string Number)` — phone number with separate country calling code and subscriber number.
- `SendSingleSmsResponse` — closed result type; `Success` (bool), optional `ProviderMessageId`, `FailureError` (string? non-null on failure), and `FailureKind` (`SmsFailureKind`). Built via `Succeeded`, `Failed`, or `FromException` (which guarantees a non-empty message and classifies the failure).
- `SendBulkSmsResponse` — per-recipient bulk result; `Results` (one `SmsRecipientResult` each), `AllSucceeded`/`AnySucceeded`, optional `ProviderBatchId`. Built via `FromResults` or `FromAggregate`.
- `SmsFailureKinds` — shared transport classifier (`FromException`) so every provider maps network faults to the same `SmsFailureKind`; provider-specific signals are classified per provider from its own contract.
- Never throws for provider errors — only `OperationCanceledException` and argument-validation exceptions (malformed request) propagate.

### Installation

```bash
dotnet add package Headless.Sms.Abstractions
```

### Quick Start

```csharp
public sealed class OtpService(ISmsSender smsSender)
{
    public async Task SendOtpAsync(string phoneNumber, string code, CancellationToken ct)
    {
        var request = new SendSingleSmsRequest
        {
            Destination = new SmsRequestDestination(20, phoneNumber), // 20 = Egypt calling code
            Text = $"Your verification code is: {code}",
        };

        var response = await smsSender.SendAsync(request, ct);

        if (!response.Success)
        {
            throw new InvalidOperationException($"SMS failed: {response.FailureError}");
        }
    }
}
```

### Configuration

No configuration required. This is an abstractions-only package.

### Dependencies

- `Headless.Checks`
- `Polly.Core`
- `Polly.RateLimiting`

`SmsFailureKinds.FromException` classifies the standard resilience pipeline's timeout, open-circuit, and rate-limiter rejections, so the abstraction references the Polly exception types directly.

### Side Effects

None. This is an abstractions package. Registration lives in `Headless.Sms.Core`.

---

## Headless.Sms.Core

Setup builder, registration gates, and the named-sender provider for the SMS abstraction.

### Problem Solved

Owns the unified SMS setup builder (`AddHeadlessSms`) and the `ISmsSenderProvider` implementation, giving every provider one registration grammar (a default slot plus named instances over keyed DI) instead of each package hand-rolling its own `IServiceCollection` extension.

### Key Features

- `AddHeadlessSms(Action<HeadlessSmsSetupBuilder>)` — the single provider-agnostic registration entry point, with an at-most-one-default-provider gate and a once-per-collection guard.
- `HeadlessSmsSetupBuilder` — receives the default `Use*` selection plus `AddNamed(name, …)` named instances; `HeadlessSmsInstanceBuilder` — the per-named-instance builder that providers extend with their `Use*` members.
- `ISmsSenderProvider` — registered automatically by the gate (keyed-service-backed via `KeyedServiceSmsSenderProvider`); resolves named senders by name.
- Deferred registration: provider contributions are queued and run only after the gates pass — the default first, then each named instance — so a setup that fails a gate leaves the `IServiceCollection` unchanged.

### Design Notes

The builder carries no shared, cross-provider feature options — it is provider-selection-only; each provider binds its own options inside its `Use*` member. The gate is **per-slot**: it allows at most one default provider (rejecting a second, but permitting zero for a named-only host) while allowing unbounded ordinal-unique named instances, and rejects a repeated `AddHeadlessSms` on the same `IServiceCollection` (a marker service enforces the single-call rule). Providers contribute deferred `Action<IServiceCollection>` registrations (`RegisterDefaultProvider` for the default, `instance.RegisterProvider` for a named instance) rather than implementing a provider interface, keeping the default and named paths symmetric. `ISmsSenderProvider` resolves only named (keyed) senders — the default sender, when configured, is the unkeyed `ISmsSender`, reachable directly and never by name — and `ISmsSenderProvider.RegisteredNames` enumerates the named instances.

### Installation

```bash
dotnet add package Headless.Sms.Core
```

### Quick Start

```csharp
// Provider-agnostic registration entry point (a provider package supplies the Use* member):
builder.Services.AddHeadlessSms(setup =>
{
    setup.UseNoop();                             // default (optional)
    setup.AddNamed("otp", i => i.UseNoop());     // optional named sender, keyed "otp"
});

// Resolve a named sender:
var otp = serviceProvider.GetRequiredService<ISmsSenderProvider>().GetSender("otp");
```

### Configuration

No configuration required.

### Dependencies

- `Headless.Sms.Abstractions`
- `Headless.Checks`
- `Microsoft.Extensions.DependencyInjection.Abstractions`

### Side Effects

`AddHeadlessSms` registers a provider-registration marker and `ISmsSenderProvider` (keyed-service-backed), then runs the default provider's wiring (the unkeyed `ISmsSender`) when a default is configured, followed by each named instance's wiring (keyed under the instance name). The marker enforces the single-call rule.

---

## Headless.Sms.Aws

AWS SNS SMS implementation of `ISmsSender`.

### Problem Solved

Provides SMS sending via Amazon Simple Notification Service (SNS), reusing existing AWS SDK credentials and IAM-based access control already present in AWS-hosted applications.

### Key Features

- `AwsSnsSmsSender` — `ISmsSender` implementation backed by AWS SNS. Single recipient per send; does not implement `IBulkSmsSender` (SNS publishes to one phone number per call).
- `SenderId` — alphanumeric sender ID displayed to recipients (support varies by country).
- `MaxPrice` — optional per-message USD price cap; SNS rejects sends that would exceed it.
- Accepts any AWS credential source: environment, instance metadata, `appsettings.json` via `AWSOptions`, or explicit `BasicAWSCredentials`.

### Installation

```bash
dotnet add package Headless.Sms.Aws
```

### Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Option 1: bind from appsettings (recommended)
var awsOptions = builder.Configuration.GetAWSOptions();
builder.Services.AddHeadlessSms(setup => setup.UseAwsSns(builder.Configuration.GetSection("Sms:Aws"), awsOptions));

// Option 2: configure in code
builder.Services.AddHeadlessSms(setup =>
    setup.UseAwsSns(
        options =>
        {
            options.SenderId = "MyApp";
            // options.MaxPrice = 0.05m; // optional per-message USD cap
        },
        awsOptions
    )
);

// Named instance (keyed ISmsSender + keyed IAmazonSimpleNotificationService, resolvable via ISmsSenderProvider):
builder.Services.AddHeadlessSms(setup =>
{
    setup.UseNoop(); // default (optional)
    setup.AddNamed("sns", i => i.UseAwsSns(builder.Configuration.GetSection("Sms:Aws"), awsOptions)); // keyed "sns"
});
```

### Configuration

#### appsettings.json

```json
{
  "Sms": {
    "Aws": {
      "SenderId": "MyApp",
      "MaxPrice": null
    }
  },
  "AWS": {
    "Region": "us-east-1"
  }
}
```

#### Options

| Option | Type | Required | Description |
|---|---|---|---|
| `SenderId` | `string` | Yes | Alphanumeric sender ID shown to recipients (country support varies). |
| `MaxPrice` | `decimal?` | No | Maximum USD price per message. SNS rejects if exceeded. |

AWS credentials are sourced from `AWSOptions` passed to the registration method (or the default credential chain if `null`).

### Dependencies

- `Headless.Sms.Core`
- `AWSSDK.SimpleNotificationService`
- `AWSSDK.Extensions.NETCore.Setup`

### Side Effects

- Default: registers `IAmazonSimpleNotificationService` via `TryAddAWSService` (no-op if already registered) and `ISmsSender` (`AwsSnsSmsSender`) as an unkeyed singleton. No `IBulkSmsSender` — SNS publishes to one recipient per call.
- Named (`AddNamed(name, i => i.UseAwsSns(…))`): registers a keyed `IAmazonSimpleNotificationService` (built via `AWSOptions.CreateServiceClient<T>` from the supplied options, the ambient `AWSOptions` in DI, `IConfiguration` `AWS:*` via `GetAWSOptions()`, or SDK defaults — mirroring `TryAddAWSService(null)`, which has no keyed overload) and a keyed `ISmsSender`, both under the instance name.

---

## Headless.Sms.Cequens

Cequens SMS gateway implementation of `ISmsSender`.

### Problem Solved

Provides SMS sending via the Cequens API, a regional MENA gateway that authenticates via JWT token (obtained from API key + username).

### Key Features

- `CequensSmsSender` — implements `ISmsSender` (single recipient) and `IBulkSmsSender` (multi-recipient bulk), backed by the Cequens REST API.
- JWT token-based auth with automatic token acquisition from `TokenEndpoint`.
- Optional pre-configured `Token` to skip the sign-in flow.
- Configurable `SingleSmsEndpoint` and `TokenEndpoint` (defaults point to the Cequens production API).
- Standard resilience pipeline with auto-retry **disabled** by default to prevent duplicate SMS.
- Optional `configureClient` and `configureResilience` hooks for fine-grained HttpClient control.

### Design Notes

The HTTP resilience handler is wired with `options.Retry.ShouldHandle = static _ => PredicateResult.False()` — no retries by default. SMS sends are not idempotent, and retrying a failed send without an idempotency key can deliver duplicate messages. Pass `configureResilience` to opt back in if Cequens provides idempotency support for your account. Each instance owns its own JWT token cache (an instance field on the sender), so a named instance never shares a token with the default sender or another name.

### Installation

```bash
dotnet add package Headless.Sms.Cequens
```

### Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHeadlessSms(setup => setup.UseCequens(builder.Configuration.GetSection("Sms:Cequens")));

// Or in code:
builder.Services.AddHeadlessSms(setup =>
    setup.UseCequens(options =>
    {
        options.ApiKey = "your-api-key";
        options.UserName = "your-username";
        options.SenderName = "MyApp";
    })
);

// Named instance — an isolated HttpClient, token cache, and options (keyed "otp"):
builder.Services.AddHeadlessSms(setup =>
{
    setup.UseCequens(builder.Configuration.GetSection("Sms:Cequens")); // default (optional)
    setup.AddNamed("otp", i => i.UseCequens(builder.Configuration.GetSection("Sms:CequensOtp")));
});
```

### Configuration

#### appsettings.json

```json
{
  "Sms": {
    "Cequens": {
      "ApiKey": "your-api-key",
      "UserName": "your-username",
      "SenderName": "MyApp",
      "SingleSmsEndpoint": "https://apis.cequens.com/sms/v1/messages",
      "TokenEndpoint": "https://apis.cequens.com/auth/v1/tokens"
    }
  }
}
```

#### Options

| Option | Type | Required | Default | Description |
|---|---|---|---|---|
| `ApiKey` | `string` | Yes | — | Cequens API key for token acquisition. |
| `UserName` | `string` | Yes | — | Cequens account username. |
| `SenderName` | `string` | Yes | — | Sender name shown to recipients. |
| `SingleSmsEndpoint` | `string` | No | `https://apis.cequens.com/sms/v1/messages` | Override for non-default environments. |
| `TokenEndpoint` | `string` | No | `https://apis.cequens.com/auth/v1/tokens` | Override for non-default environments. |
| `Token` | `string?` | No | `null` | Pre-issued JWT; skips sign-in if set. |

### Dependencies

- `Headless.Sms.Core`
- `Microsoft.Extensions.Http.Resilience`

### Side Effects

- Default: registers `ISmsSender` (`CequensSmsSender`) and `IBulkSmsSender` (forwarding to the same instance) as unkeyed singletons, plus a named `HttpClient` (`Headless:CequensSms`) with a standard resilience handler (retry disabled).
- Named (`AddNamed(name, i => i.UseCequens(…))`): registers a keyed `ISmsSender` and keyed `IBulkSmsSender` (same instance), named options, and a per-name `HttpClient` (`Headless:CequensSms:{name}`) with its own resilience pipeline — so each named sender owns an isolated token cache and never reads another instance's settings.

---

## Headless.Sms.Connekio

Connekio SMS gateway implementation of `ISmsSender`.

### Problem Solved

Provides SMS sending via the Connekio API using basic username/password/accountId authentication, supporting both single-message and batch delivery.

### Key Features

- `ConnekioSmsSender` — implements `ISmsSender` (single recipient) and `IBulkSmsSender` (multi-recipient bulk).
- Separate `SingleSmsEndpoint` (used by `SendAsync`) and `BatchSmsEndpoint` (used by `IBulkSmsSender.SendBulkAsync`).
- Basic auth: `UserName` + `Password` + `AccountId`.
- Standard resilience pipeline with auto-retry **disabled** by default.
- Optional `configureClient` and `configureResilience` hooks.

### Design Notes

Retry is disabled by default for the same reason as all HTTP SMS providers: sending the same message twice can cause duplicate delivery. Pass `configureResilience` to opt back in if Connekio assigns idempotency keys for your account tier.

### Installation

```bash
dotnet add package Headless.Sms.Connekio
```

### Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHeadlessSms(setup => setup.UseConnekio(builder.Configuration.GetSection("Sms:Connekio")));

// Or in code:
builder.Services.AddHeadlessSms(setup =>
    setup.UseConnekio(options =>
    {
        options.UserName = "your-username";
        options.Password = "your-password";
        options.AccountId = "your-account-id";
        options.Sender = "MyApp";
    })
);

// Named instance — an isolated HttpClient and options (keyed "marketing"):
builder.Services.AddHeadlessSms(setup =>
{
    setup.UseConnekio(builder.Configuration.GetSection("Sms:Connekio")); // default (optional)
    setup.AddNamed("marketing", i => i.UseConnekio(builder.Configuration.GetSection("Sms:ConnekioBulk")));
});
```

### Configuration

#### appsettings.json

```json
{
  "Sms": {
    "Connekio": {
      "UserName": "your-username",
      "Password": "your-password",
      "AccountId": "your-account-id",
      "Sender": "MyApp",
      "SingleSmsEndpoint": "https://api.connekio.com/sms/single",
      "BatchSmsEndpoint": "https://api.connekio.com/sms/batch"
    }
  }
}
```

#### Options

| Option | Type | Required | Default | Description |
|---|---|---|---|---|
| `UserName` | `string` | Yes | — | Connekio account username. |
| `Password` | `string` | Yes | — | Connekio account password. |
| `AccountId` | `string` | Yes | — | Connekio account identifier. |
| `Sender` | `string` | Yes | — | Sender name shown to recipients. |
| `SingleSmsEndpoint` | `string` | No | `https://api.connekio.com/sms/single` | Override for non-default environments. |
| `BatchSmsEndpoint` | `string` | No | `https://api.connekio.com/sms/batch` | Override for non-default environments. |

### Dependencies

- `Headless.Sms.Core`
- `Microsoft.Extensions.Http.Resilience`

### Side Effects

- Default: registers `ISmsSender` (`ConnekioSmsSender`) and `IBulkSmsSender` (forwarding to the same instance) as unkeyed singletons, plus a named `HttpClient` (`Headless:ConnekioSms`) with a standard resilience handler (retry disabled).
- Named (`AddNamed(name, i => i.UseConnekio(…))`): registers a keyed `ISmsSender` and keyed `IBulkSmsSender` (same instance), named options, and a per-name `HttpClient` (`Headless:ConnekioSms:{name}`) with its own resilience pipeline.

---

## Headless.Sms.Dev

Development SMS implementations that avoid real sends.

### Problem Solved

Provides no-op and file-logging SMS senders for development and test environments, enabling full SMS workflow testing without requiring vendor credentials or sending actual messages.

### Key Features

- `DevSmsSender` — implements `ISmsSender` and `IBulkSmsSender`; appends formatted SMS details to a local file for inspection.
- `NoopSmsSender` — implements `ISmsSender` and `IBulkSmsSender`; silently discards all messages and returns `SendSingleSmsResponse.Succeeded()`.
- No external dependencies, no HTTP calls, no API credentials needed.

### Installation

```bash
dotnet add package Headless.Sms.Dev
```

### Quick Start

#### File-based logging

```csharp
var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddHeadlessSms(setup => setup.UseDevelopment("sms-log.txt"));
}
```

#### Silent discard

```csharp
var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddHeadlessSms(setup => setup.UseNoop());
}
```

#### As a named instance alongside a real default sender

```csharp
// Keyed ISmsSender "audit" writes to a file while the default sends for real:
builder.Services.AddHeadlessSms(setup =>
{
    setup.UseTwilio(builder.Configuration.GetSection("Sms:Twilio")); // default (optional)
    setup.AddNamed("audit", i => i.UseDevelopment("audit-sms.txt"));
});
```

### Configuration

No configuration required. The file path is passed directly to `UseDevelopment`.

### Dependencies

- `Headless.Sms.Core`

### Side Effects

- Default: registers `ISmsSender` and `IBulkSmsSender` (the bulk sender forwards to the same instance) as unkeyed singletons. `DevSmsSender` appends to the specified file on each send; `NoopSmsSender` discards silently.
- Named (`AddNamed(name, i => i.UseDevelopment(path))` / `i.UseNoop()`): registers the same sender as a keyed `ISmsSender` (and keyed `IBulkSmsSender`) under the instance name.

---

## Headless.Sms.Infobip

Infobip global SMS platform implementation of `ISmsSender`.

### Problem Solved

Provides SMS sending via Infobip's REST API, a global messaging platform with delivery reporting and per-account regional base paths.

### Key Features

- `InfobipSmsSender` — implements `ISmsSender` (single recipient) and `IBulkSmsSender` (multi-recipient bulk, with per-recipient message ids), backed by the Infobip REST API.
- API key authentication via HTTP `Authorization` header.
- `BasePath` — Infobip-assigned base URL for your account (not a generic endpoint; varies per account).
- `Sender` — alphanumeric or numeric sender shown to recipients.
- Standard resilience pipeline with auto-retry **disabled** by default.
- Optional `configureClient` and `configureResilience` hooks.

### Installation

```bash
dotnet add package Headless.Sms.Infobip
```

### Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHeadlessSms(setup => setup.UseInfobip(builder.Configuration.GetSection("Sms:Infobip")));

// Or in code:
builder.Services.AddHeadlessSms(setup =>
    setup.UseInfobip(options =>
    {
        options.ApiKey = "your-api-key";
        options.BasePath = "https://XXXXXXXX.api.infobip.com"; // account-specific URL
        options.Sender = "MyApp";
    })
);

// Named instance — an isolated HttpClient and options (keyed "marketing"):
builder.Services.AddHeadlessSms(setup =>
{
    setup.UseInfobip(builder.Configuration.GetSection("Sms:Infobip")); // default (optional)
    setup.AddNamed("marketing", i => i.UseInfobip(builder.Configuration.GetSection("Sms:InfobipBulk")));
});
```

### Configuration

#### appsettings.json

```json
{
  "Sms": {
    "Infobip": {
      "ApiKey": "your-api-key",
      "BasePath": "https://XXXXXXXX.api.infobip.com",
      "Sender": "MyApp"
    }
  }
}
```

#### Options

| Option | Type | Required | Description |
|---|---|---|---|
| `ApiKey` | `string` | Yes | Infobip API key for bearer authentication. |
| `BasePath` | `string` | Yes | Account-specific Infobip base URL (must be HTTPS). |
| `Sender` | `string` | Yes | Sender name or number shown to recipients. |

### Dependencies

- `Headless.Sms.Core`
- `Microsoft.Extensions.Http.Resilience`

### Side Effects

- Default: registers `ISmsSender` (`InfobipSmsSender`) and `IBulkSmsSender` (forwarding to the same instance) as unkeyed singletons, plus a named `HttpClient` (`Headless:InfobipSms`) with a standard resilience handler (retry disabled).
- Named (`AddNamed(name, i => i.UseInfobip(…))`): registers a keyed `ISmsSender` and keyed `IBulkSmsSender` (same instance), named options, and a per-name `HttpClient` (`Headless:InfobipSms:{name}`) with its own resilience pipeline.

---

## Headless.Sms.Twilio

Twilio SMS implementation of `ISmsSender`.

### Problem Solved

Provides SMS sending via Twilio's REST API, the most widely supported international SMS platform, with configurable sender number and optional per-message price cap.

### Key Features

- `TwilioSmsSender` — `ISmsSender` implementation using `ITwilioRestClient`. Single recipient per send; does not implement `IBulkSmsSender` (Twilio creates one message per recipient).
- `Sid` + `AuthToken` — Twilio account credentials.
- `PhoneNumber` — E.164 sender number validated by `InternationalPhoneNumber` rule.
- `MaxPrice` — optional per-message USD price cap.
- `Region` + `Edge` — optional Twilio region/edge node selection for data residency or latency.
- Standard resilience pipeline with auto-retry **disabled** by default.
- Optional `configureClient` and `configureResilience` hooks.
- Cancellation is honored up to the point of dispatch only: the Twilio SDK (7.x) does not accept a `CancellationToken` on its send path, so an already-cancelled token throws before the call, but cancellation mid-flight cannot interrupt the in-progress request.

### Installation

```bash
dotnet add package Headless.Sms.Twilio
```

### Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHeadlessSms(setup => setup.UseTwilio(builder.Configuration.GetSection("Sms:Twilio")));

// Or in code:
builder.Services.AddHeadlessSms(setup =>
    setup.UseTwilio(options =>
    {
        options.Sid = "ACxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx";
        options.AuthToken = "your-auth-token";
        options.PhoneNumber = "+12025551234";
    })
);

// Named instance — a keyed ISmsSender plus a keyed ITwilioRestClient (resolvable via ISmsSenderProvider):
builder.Services.AddHeadlessSms(setup =>
{
    setup.UseTwilio(builder.Configuration.GetSection("Sms:Twilio")); // default (optional)
    setup.AddNamed("marketing", i => i.UseTwilio(builder.Configuration.GetSection("Sms:TwilioMarketing")));
});
```

### Configuration

#### appsettings.json

```json
{
  "Sms": {
    "Twilio": {
      "Sid": "ACxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx",
      "AuthToken": "your-auth-token",
      "PhoneNumber": "+12025551234",
      "MaxPrice": null,
      "Region": null,
      "Edge": null
    }
  }
}
```

#### Options

| Option | Type | Required | Description |
|---|---|---|---|
| `Sid` | `string` | Yes | Twilio Account SID (`AC...`). |
| `AuthToken` | `string` | Yes | Twilio Auth Token. |
| `PhoneNumber` | `string` | Yes | E.164 sender number (e.g. `+12025551234`). |
| `MaxPrice` | `decimal?` | No | Maximum USD price per message. Twilio rejects if exceeded. |
| `Region` | `string?` | No | Twilio region for data residency (e.g. `au1`, `ie1`). |
| `Edge` | `string?` | No | Twilio edge node (e.g. `sydney`, `dublin`). |

### Dependencies

- `Headless.Sms.Core`
- `Twilio`
- `Microsoft.Extensions.Http.Resilience`

### Side Effects

- Default: registers `ITwilioRestClient` via `TryAddSingleton` (a host-supplied client wins), `ISmsSender` (`TwilioSmsSender`) as an unkeyed singleton, and a named `HttpClient` (`Headless:TwilioSms`) with a standard resilience handler (retry disabled). No `IBulkSmsSender` — Twilio creates one message per recipient.
- Named (`AddNamed(name, i => i.UseTwilio(…))`): registers a keyed `ITwilioRestClient` (built from that name's options and per-name HttpClient), a keyed `ISmsSender`, named options, and a per-name `HttpClient` (`Headless:TwilioSms:{name}`) with its own resilience pipeline.

---

## Headless.Sms.VictoryLink

VictoryLink SMS gateway implementation of `ISmsSender`.

### Problem Solved

Provides SMS sending via the VictoryLink API, a regional gateway serving the Middle East market with username/password authentication.

### Key Features

- `VictoryLinkSmsSender` — implements `ISmsSender` (single recipient) and `IBulkSmsSender` (multi-recipient bulk), backed by the VictoryLink REST API.
- Username + password authentication.
- Configurable `Sender` name and `Endpoint` URL.
- Response-code-based error detection.
- Standard resilience pipeline with auto-retry **disabled** by default.
- Optional `configureClient` and `configureResilience` hooks.

### Installation

```bash
dotnet add package Headless.Sms.VictoryLink
```

### Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHeadlessSms(setup => setup.UseVictoryLink(builder.Configuration.GetSection("Sms:VictoryLink")));

// Or in code:
builder.Services.AddHeadlessSms(setup =>
    setup.UseVictoryLink(options =>
    {
        options.UserName = "your-username";
        options.Password = "your-password";
        options.Sender = "MyApp";
    })
);

// Named instance — an isolated HttpClient and options (keyed "otp"):
builder.Services.AddHeadlessSms(setup =>
{
    setup.UseVictoryLink(builder.Configuration.GetSection("Sms:VictoryLink")); // default (optional)
    setup.AddNamed("otp", i => i.UseVictoryLink(builder.Configuration.GetSection("Sms:VictoryLinkOtp")));
});
```

### Configuration

#### appsettings.json

```json
{
  "Sms": {
    "VictoryLink": {
      "UserName": "your-username",
      "Password": "your-password",
      "Sender": "MyApp",
      "Endpoint": "https://smsvas.vlserv.com/VLSMSPlatformResellerAPI/NewSendingAPI/api/SMSSender/SendSMS"
    }
  }
}
```

#### Options

| Option | Type | Required | Default | Description |
|---|---|---|---|---|
| `UserName` | `string` | Yes | — | VictoryLink account username. |
| `Password` | `string` | Yes | — | VictoryLink account password. |
| `Sender` | `string` | Yes | — | Sender name shown to recipients. |
| `Endpoint` | `string` | No | VictoryLink production URL | Override for non-default environments. |

### Dependencies

- `Headless.Sms.Core`
- `Microsoft.Extensions.Http.Resilience`

### Side Effects

- Default: registers `ISmsSender` (`VictoryLinkSmsSender`) and `IBulkSmsSender` (forwarding to the same instance) as unkeyed singletons, plus a named `HttpClient` (`Headless:VictoryLinkSms`) with a standard resilience handler (retry disabled).
- Named (`AddNamed(name, i => i.UseVictoryLink(…))`): registers a keyed `ISmsSender` and keyed `IBulkSmsSender` (same instance), named options, and a per-name `HttpClient` (`Headless:VictoryLinkSms:{name}`) with its own resilience pipeline.

---

## Headless.Sms.Vodafone

Vodafone Egypt enterprise SMS gateway implementation of `ISmsSender`.

### Problem Solved

Provides SMS sending via the Vodafone Egypt enterprise messaging API, which uses a shared-secret (`SecureHash`) authentication model alongside account credentials.

### Key Features

- `VodafoneSmsSender` — implements `ISmsSender` (single recipient) and `IBulkSmsSender` (multi-recipient bulk), backed by the Vodafone Egypt REST API.
- Account credentials: `AccountId` + `Password` + `SecureHash`.
- Configurable `Sender` name and `SendSmsEndpoint`.
- Standard resilience pipeline with auto-retry **disabled** by default.
- Optional `configureClient` and `configureResilience` hooks.

### Design Notes

Vodafone Egypt's API requires a `SecureHash` in addition to account credentials — this is not an OAuth2 or JWT flow. The hash is issued by Vodafone at account provisioning and must be stored as a secret. Do not confuse this provider with a generic Vodafone API; the endpoint defaults to `https://e3len.vodafone.com.eg/web2sms/sms/submit/`.

### Installation

```bash
dotnet add package Headless.Sms.Vodafone
```

### Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHeadlessSms(setup => setup.UseVodafone(builder.Configuration.GetSection("Sms:Vodafone")));

// Or in code:
builder.Services.AddHeadlessSms(setup =>
    setup.UseVodafone(options =>
    {
        options.AccountId = "your-account-id";
        options.Password = "your-password";
        options.SecureHash = "your-secure-hash";
        options.Sender = "MyApp";
    })
);

// Named instance — an isolated HttpClient and options (keyed "promo"):
builder.Services.AddHeadlessSms(setup =>
{
    setup.UseVodafone(builder.Configuration.GetSection("Sms:Vodafone")); // default (optional)
    setup.AddNamed("promo", i => i.UseVodafone(builder.Configuration.GetSection("Sms:VodafonePromo")));
});
```

### Configuration

#### appsettings.json

```json
{
  "Sms": {
    "Vodafone": {
      "AccountId": "your-account-id",
      "Password": "your-password",
      "SecureHash": "your-secure-hash",
      "Sender": "MyApp",
      "SendSmsEndpoint": "https://e3len.vodafone.com.eg/web2sms/sms/submit/"
    }
  }
}
```

#### Options

| Option | Type | Required | Default | Description |
|---|---|---|---|---|
| `AccountId` | `string` | Yes | — | Vodafone Egypt account identifier. |
| `Password` | `string` | Yes | — | Vodafone Egypt account password. |
| `SecureHash` | `string` | Yes | — | Shared secret issued at provisioning. |
| `Sender` | `string` | Yes | — | Sender name shown to recipients. |
| `SendSmsEndpoint` | `string` | No | `https://e3len.vodafone.com.eg/web2sms/sms/submit/` | Override for non-default environments. |

### Dependencies

- `Headless.Sms.Core`
- `Microsoft.Extensions.Http.Resilience`

### Side Effects

- Default: registers `ISmsSender` (`VodafoneSmsSender`) and `IBulkSmsSender` (forwarding to the same instance) as unkeyed singletons, plus a named `HttpClient` (`Headless:VodafoneSms`) with a standard resilience handler (retry disabled).
- Named (`AddNamed(name, i => i.UseVodafone(…))`): registers a keyed `ISmsSender` and keyed `IBulkSmsSender` (same instance), named options, and a per-name `HttpClient` (`Headless:VodafoneSms:{name}`) with its own resilience pipeline.
