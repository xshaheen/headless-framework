---
domain: SMS
packages: Sms.Abstractions, Sms, Sms.Aws, Sms.Cequens, Sms.Connekio, Sms.Dev, Sms.Infobip, Sms.Twilio, Sms.VictoryLink, Sms.Vodafone
---

# SMS

> Provider-agnostic SMS sending through `ISmsSender` and `IBulkSmsSender`, with international providers (Twilio, AWS SNS, Infobip), MENA providers (Cequens, Connekio, VictoryLink, Vodafone Egypt), and development senders.

## Orientation

Install `Headless.Sms.Abstractions` and one provider package. Each provider package brings `Headless.Sms`, which owns `AddHeadlessSms`.

- **Development and tests:** `Headless.Sms.Dev`. `UseDevelopment(path)` appends each message to a file. `UseNoop()` discards it.
- **International:** `Headless.Sms.Twilio`, `Headless.Sms.Aws` (AWS SNS), `Headless.Sms.Infobip`.
- **MENA:** `Headless.Sms.Cequens`, `Headless.Sms.Connekio`, `Headless.Sms.VictoryLink`, `Headless.Sms.Vodafone`.

Register every sender in one `AddHeadlessSms` call: an optional **default** sender, chosen by a `Use*` call on the setup builder, plus any number of **named** senders added with `setup.AddNamed(name, i => i.Use…())`. Inject `ISmsSender` for one recipient, or `IBulkSmsSender` for many where the provider supports it. Resolve a named sender with `[FromKeyedServices("name")] ISmsSender` or `ISmsSenderProvider.GetSender("name")`.

```csharp
builder.Services.AddHeadlessSms(setup =>
{
    if (builder.Environment.IsDevelopment())
    {
        setup.UseDevelopment("sms-log.txt");
    }
    else
    {
        setup.UseTwilio(builder.Configuration.GetSection("Sms:Twilio"));
    }
});
```

**Verify:** start the host. Missing or invalid provider options fail the start with `OptionsValidationException`. Under `RunHeadlessAsync`, `dotnet MyApp.dll check` prints `Host check failed: ...` and exits 1 for the same mistake. Validation proves the options are present, not that the credentials work: send one message to a test number and assert `response.Success`. A rejected credential returns a failed response, with `FailureKind` set to `AuthFailure` where the provider reports it.

## Agent Rules

- Call `AddHeadlessSms` once per service collection. A second call, or more than one default `Use*` call in the delegate, throws `InvalidOperationException` at registration. Zero defaults is allowed: the unkeyed `ISmsSender` is then not registered, and the host resolves senders by name only.
- The `Use*` calls are `UseTwilio`, `UseAwsSns`, `UseInfobip`, `UseCequens`, `UseConnekio`, `UseVictoryLink`, `UseVodafone`, `UseDevelopment`, and `UseNoop`. Each one is available on the setup builder (default) and on each named instance.
- Give each named sender a non-blank name that is unique in the call (ordinal comparison), and exactly one provider. A blank name throws `ArgumentException`. A duplicate name, or zero or several providers in one named instance, throws `InvalidOperationException`.
- Resolve a named sender with `ISmsSenderProvider.GetSender(name)`, which throws `InvalidOperationException` for an unregistered name, or `GetSenderOrNull(name)`, which returns `null`. Both throw `ArgumentException` for a blank name. To validate a name from outside input, check `ISmsSenderProvider.RegisteredNames` first. `ISmsSenderProvider` never returns the default sender, and `RegisteredNames` does not list it.
- Use `UseDevelopment` or `UseNoop` in development and in tests, so no real SMS leaves the machine.
- Address one recipient with `SendSingleSmsRequest.Destination` and the body with `Text`. The request has no `To` or `Message` property. `SmsRequestDestination` takes the country calling code (`Code`) and the local number (`Number`) as separate values.
- Check `response.Success` after every send. A provider or transport failure returns a failed `SendSingleSmsResponse`. Only cancellation and argument validation (a null request, a null destination, empty text) throw. The response has `Success` and `FailureError`, not `IsSuccess` or `ErrorMessage`.
- Never retry an `Unknown` failure automatically: a timeout or reset after sending is `Unknown`, because the provider may have delivered the message, and a retry can send it twice. A `Transient` failure means the request never left the process, so a retry is safe.
- Handle `SmsFailureKind.Unknown` and a `default` branch whenever you `switch` on `SmsFailureKind`. The enum can gain members in a minor version.
- Twilio and AWS SNS do not support bulk sends. Resolving `IBulkSmsSender` for them fails, so loop `SendAsync` instead. For a bulk-capable named sender, resolve `[FromKeyedServices("name")] IBulkSmsSender`.
- HTTP providers never retry a send automatically, because a retried POST can deliver the message twice. Pass `configureResilience` to opt back in only when the provider deduplicates by an idempotency key.
- Use the exact option names. Twilio: `Sid`, `AuthToken`, and `PhoneNumber` (not `AccountSid` or `From`; there is no `MessagingServiceSid`). Infobip: `BasePath` and `Sender` (not `BaseUrl` or `SenderName`). Vodafone: `AccountId`, `Password`, and `SecureHash` (not OAuth client credentials).
- Call SMS only through `ISmsSender`. Do not call a provider SDK, such as the Twilio REST client or AWS SNS, from application code.

---

## Core Concepts

### Default and named senders

```csharp
builder.Services.AddHeadlessSms(setup =>
{
    setup.UseTwilio(builder.Configuration.GetSection("Sms:Twilio")); // default (optional)
    setup.AddNamed("otp", i => i.UseCequens(builder.Configuration.GetSection("Sms:Otp"))); // keyed "otp"
    setup.AddNamed("marketing", i => i.UseInfobip(builder.Configuration.GetSection("Sms:Bulk"))); // keyed "marketing"
});
```

- The default resolves as the unkeyed `ISmsSender`, and as the unkeyed `IBulkSmsSender` when the provider supports bulk. Every sender is a singleton.
- A named sender resolves as a keyed `ISmsSender` under its name, and through `ISmsSenderProvider`. A bulk-capable named sender also registers a keyed `IBulkSmsSender` that forwards to the same instance.
- Each named sender owns its options (validated at startup per name), its `HttpClient` and resilience pipeline, and its backend state: the Twilio client, the SNS client, or the Cequens token cache. The same provider can back two names with different options, and no named sender reads the default's options.
- A setup that throws leaves the service collection unchanged.

### Message model

```csharp
var request = new SendSingleSmsRequest
{
    MessageId = "optional-id", // optional, provider-specific use
    Destination = new SmsRequestDestination(20, "1234567890"), // Code = country calling code
    Text = "Your OTP is 123456",
    Properties = null, // optional, provider-specific metadata
};
```

`SmsRequestDestination(int Code, string Number).ToString()` returns `"{Code}{Number}"` with no plus sign. `ToString(hasPlusPrefix: true)` returns `"+{Code}{Number}"`.

For several recipients, `SendBulkSmsRequest` has `Destinations` (a list), `Text`, and optional `MessageId` and `Properties`. Send it with `IBulkSmsSender.SendBulkAsync`.

### Result model

`SendSingleSmsResponse` has these properties:

- `Success`.
- `FailureError`: non-null when `Success` is `false`. The compiler knows this after `if (!response.Success)`.
- `ProviderMessageId`: the backend's id on success when it returns one (the Twilio SID, the SNS message id, or the Infobip bulk id), otherwise `null`.
- `FailureKind`: an `SmsFailureKind` value. `None` on success. On failure, one of `Unknown`, `Transient`, `RateLimited`, `InvalidRecipient`, `AuthFailure`, or `OutOfCredit`.

`FailureKind` is `Transient` only for a failure before the request left the process: a connection, name-resolution, or TLS error, a refused or unreachable socket, an open circuit, or a rate-limiter rejection. Any other transport fault is `Unknown`. A provider sets a finer kind only from its own documented signals ([Choosing a Provider](#choosing-a-provider) lists them) and never guesses from a generic HTTP status. No provider reports `OutOfCredit` yet.

Build responses in a test double with `SendSingleSmsResponse.Succeeded(providerMessageId)`, `Failed(reason, kind)` (a blank reason throws), or `FromException(exception, kind)`.

`IBulkSmsSender.SendBulkAsync` returns a `SendBulkSmsResponse`: one `SmsRecipientResult` (`Destination` and a `SendSingleSmsResponse`) per recipient in `Results`, plus `AllSucceeded`, `AnySucceeded`, and an optional `ProviderBatchId`. Infobip reports each recipient separately. Cequens, Connekio, VictoryLink, and Vodafone report one batch status, applied to every recipient. In a test double, build one with `SendBulkSmsResponse.FromResults(...)` or `FromAggregate(destinations, response)`.

### Retry safety

SMS sends are not idempotent: resending after a failure can deliver the same message twice. Every HTTP provider (Cequens, Connekio, Infobip, Twilio, VictoryLink, Vodafone) builds its `HttpClient` pipeline with no automatic retry for POST, PUT, PATCH, DELETE, or CONNECT, which covers every send. The rule holds in a host that adds a resilience handler to every client, such as `AddHeadless()` service defaults: the provider removes that handler from its own client, in either registration order.

To opt back in, pass `configureResilience` to the `Use*` call. It runs after the defaults. Do it only when the provider deduplicates by an idempotency key for your account.

Each HTTP provider's client is named `Headless:{Provider}Sms` for the default sender and `Headless:{Provider}Sms:{name}` for a named sender: `Headless:CequensSms`, `Headless:ConnekioSms`, `Headless:InfobipSms`, `Headless:TwilioSms`, `Headless:VictoryLinkSms`, and `Headless:VodafoneSms`. `configureClient` takes only an `Action<HttpClient>`. To add a proxy or a `DelegatingHandler`, configure the named client: `services.AddHttpClient("Headless:TwilioSms").ConfigurePrimaryHttpMessageHandler(...)`. The client's HTTP logs use the category `System.Net.Http.HttpClient.{client name}`.

## Choosing a Provider

| Provider | Region | Credentials | Bulk | Price cap | `FailureKind` beyond `Transient` and `Unknown` |
| --- | --- | --- | --- | --- | --- |
| Twilio | Global | `Sid`, `AuthToken` | No | `MaxPrice` | None |
| AWS SNS | Global | AWS credential chain | No | `MaxPrice` | `AuthFailure`, `RateLimited`, `InvalidRecipient` (opted out) |
| Infobip | Global | `ApiKey`, account `BasePath` | Yes, per-recipient results | No | `InvalidRecipient` (undeliverable, expired, rejected) |
| Cequens | MENA | `ApiKey`, `UserName` (token fetched for you) | Yes | No | `AuthFailure` |
| Connekio | MENA | `UserName`, `Password`, `AccountId` | Yes | No | None |
| VictoryLink | Middle East | `UserName`, `Password` | Yes | No | None |
| Vodafone | Egypt | `AccountId`, `Password`, `SecureHash` | Yes | No | None |
| Dev | None | None | Yes | No | None; never fails |

---

## Headless.Sms.Abstractions

The `ISmsSender` and `IBulkSmsSender` contracts, the request and response types, and `ISmsSenderProvider`.

### Setup

```bash
dotnet add package Headless.Sms.Abstractions
```

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

**Verify:** in a test, register `UseDevelopment(path)`, call your code, and assert that `response.Success` is `true` and the file contains the text.

### Design and runtime behavior

- `ISmsSender.SendAsync(SendSingleSmsRequest, CancellationToken)` returns `ValueTask<SendSingleSmsResponse>`.
- `IBulkSmsSender.SendBulkAsync(SendBulkSmsRequest, CancellationToken)` returns `ValueTask<SendBulkSmsResponse>`.
- `ISmsSenderProvider` has `GetSender(name)`, `GetSenderOrNull(name)`, and `RegisteredNames` (`IReadOnlySet<string>`).

---

## Headless.Sms

The registration entry point, `AddHeadlessSms`. Each provider package references it.

### Setup

```bash
dotnet add package Headless.Sms
```

```csharp
builder.Services.AddHeadlessSms(setup =>
{
    setup.UseNoop(); // default (optional)
    setup.AddNamed("otp", i => i.UseNoop()); // keyed "otp"
});

var otp = provider.GetRequiredService<ISmsSenderProvider>().GetSender("otp");
```

**Verify:** a registration mistake throws from `AddHeadlessSms`, before the host builds. In a test, assert that `ISmsSenderProvider.RegisteredNames` contains each named sender.

### Design and runtime behavior

- `AddHeadlessSms(Action<HeadlessSmsSetupBuilder>)` registers `ISmsSenderProvider`, then the default sender when one is configured, then each named sender.
- `HeadlessSmsSetupBuilder` carries no shared options. Each provider binds its own options in its `Use*` call.
- `SmsFailureKinds.FromException(exception)` returns the `SmsFailureKind` the providers use for a caught exception. Use it in your own `ISmsSender` implementation to classify failures the same way.

---

## Headless.Sms.Aws

The AWS SNS provider. One recipient per send; no bulk sender.

### Setup

```bash
dotnet add package Headless.Sms.Aws
```

`UseAwsSns` takes the provider options (`IConfiguration`, `Action<AwsSnsSmsOptions>`, or `Action<AwsSnsSmsOptions, IServiceProvider>`) and an optional `AWSOptions`. Without `AWSOptions`, the AWS SDK's default configuration and credential chain apply.

```csharp
var builder = WebApplication.CreateBuilder(args);
var awsOptions = builder.Configuration.GetAWSOptions();

builder.Services.AddHeadlessSms(setup => setup.UseAwsSns(builder.Configuration.GetSection("Sms:Aws"), awsOptions));

// Or in code:
// setup.UseAwsSns(options => { options.SenderId = "MyApp"; options.MaxPrice = 0.05m; }, awsOptions);

// As a named sender, keyed "sns":
// setup.AddNamed("sns", i => i.UseAwsSns(builder.Configuration.GetSection("Sms:Aws"), awsOptions));
```

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

**Verify:** an empty `SenderId` or a negative `MaxPrice` fails the start with `OptionsValidationException`. Send one message and assert `response.Success`. A rejected credential returns `FailureKind.AuthFailure`.

### Configuration

| Option | Required | Notes |
| --- | --- | --- |
| `SenderId` | Yes | Alphanumeric sender ID shown to recipients. Country support varies. |
| `MaxPrice` | No | Maximum USD price per message. SNS rejects a send that would cost more. At least 0. |

### Design and runtime behavior

- **Failure kinds.** An authorization or security error returns `AuthFailure`, throttling returns `RateLimited`, an opted-out number returns `InvalidRecipient`, and an SNS internal error returns `Transient`.
- **Success.** `ProviderMessageId` is the SNS message id.
- **Client.** The default sender registers `IAmazonSimpleNotificationService` only if none is registered yet, so a client you register first wins. A named sender builds its own keyed client, from the `AWSOptions` you pass, else the `AWSOptions` in DI, else the `AWS` configuration section, else the SDK defaults.

---

## Headless.Sms.Cequens

The Cequens provider, with single and bulk sends.

### Setup

```bash
dotnet add package Headless.Sms.Cequens
```

Every HTTP provider's `Use*` call takes the options (`IConfiguration`, `Action<TOptions>`, or `Action<TOptions, IServiceProvider>`), then optional `configureClient` (`Action<HttpClient>`) and `configureResilience` (`Action<HttpStandardResilienceOptions>`).

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHeadlessSms(setup => setup.UseCequens(builder.Configuration.GetSection("Sms:Cequens")));

// Or in code:
// setup.UseCequens(options =>
// {
//     options.ApiKey = builder.Configuration["Sms:Cequens:ApiKey"]!;
//     options.UserName = "your-username";
//     options.SenderName = "MyApp";
// });

// As a named sender with its own token cache, keyed "otp":
// setup.AddNamed("otp", i => i.UseCequens(builder.Configuration.GetSection("Sms:CequensOtp")));
```

```json
{
  "Sms": {
    "Cequens": {
      "ApiKey": "your-api-key",
      "UserName": "your-username",
      "SenderName": "MyApp"
    }
  }
}
```

**Verify:** a missing required option or a non-HTTPS endpoint fails the start with `OptionsValidationException`. Send one message and assert `response.Success`. A rejected API key returns `FailureKind.AuthFailure`.

### Configuration

| Option | Required | Default | Notes |
| --- | --- | --- | --- |
| `ApiKey` | Yes | | Used to fetch the access token. |
| `UserName` | Yes | | Cequens account user name. |
| `SenderName` | Yes | | Sender name shown to recipients. |
| `SingleSmsEndpoint` | No | `https://apis.cequens.com/sms/v1/messages` | HTTPS only. |
| `TokenEndpoint` | No | `https://apis.cequens.com/auth/v1/tokens` | HTTPS only. |
| `Token` | No | `null` | A pre-issued token. When set, the sender skips sign-in. |

### Design and runtime behavior

- **Token.** The sender fetches a token and refreshes it automatically. Each sender instance, default or named, keeps its own token. A failed token fetch, or a `401` on the send, returns `FailureKind.AuthFailure`.
- **Other failures.** Any other error response returns `Unknown`, with the response body in `FailureError`.
- **Bulk.** A bulk send applies the one batch status to every recipient.

---

## Headless.Sms.Connekio

The Connekio provider, with single and bulk sends.

### Setup

```bash
dotnet add package Headless.Sms.Connekio
```

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHeadlessSms(setup => setup.UseConnekio(builder.Configuration.GetSection("Sms:Connekio")));

// As a named sender, keyed "marketing":
// setup.AddNamed("marketing", i => i.UseConnekio(builder.Configuration.GetSection("Sms:ConnekioBulk")));
```

```json
{
  "Sms": {
    "Connekio": {
      "UserName": "your-username",
      "Password": "your-password",
      "AccountId": "your-account-id",
      "Sender": "MyApp"
    }
  }
}
```

**Verify:** a missing required option or a non-HTTPS endpoint fails the start with `OptionsValidationException`. Send one message and assert `response.Success`.

### Configuration

| Option | Required | Default | Notes |
| --- | --- | --- | --- |
| `UserName` | Yes | | Account user name. |
| `Password` | Yes | | Account password. |
| `AccountId` | Yes | | Account identifier. |
| `Sender` | Yes | | Sender name shown to recipients. |
| `SingleSmsEndpoint` | No | `https://api.connekio.com/sms/single` | Used by `SendAsync`. HTTPS only. |
| `BatchSmsEndpoint` | No | `https://api.connekio.com/sms/batch` | Used by `SendBulkAsync`. HTTPS only. |

### Design and runtime behavior

- A bulk send applies the one batch status to every recipient.

---

## Headless.Sms.Dev

Development senders that deliver nothing.

### Setup

```bash
dotnet add package Headless.Sms.Dev
```

```csharp
var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddHeadlessSms(setup => setup.UseDevelopment("sms-log.txt"));
    // Or discard: setup.UseNoop()
}
```

A file-writing named sender beside a real default:

```csharp
builder.Services.AddHeadlessSms(setup =>
{
    setup.UseTwilio(builder.Configuration.GetSection("Sms:Twilio"));
    setup.AddNamed("audit", i => i.UseDevelopment("audit-sms.txt"));
});
```

**Verify:** send one message and read the file. It contains the destination and the text.

### Design and runtime behavior

- `UseDevelopment(path)` appends each message to the file. `UseNoop()` discards it and returns `Succeeded()`.
- Both senders implement `ISmsSender` and `IBulkSmsSender`, and both validate the request: a null destination or empty text throws.

---

## Headless.Sms.Infobip

The Infobip provider, with single and bulk sends and per-recipient results.

### Setup

```bash
dotnet add package Headless.Sms.Infobip
```

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHeadlessSms(setup => setup.UseInfobip(builder.Configuration.GetSection("Sms:Infobip")));

// As a named sender, keyed "marketing":
// setup.AddNamed("marketing", i => i.UseInfobip(builder.Configuration.GetSection("Sms:InfobipBulk")));
```

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

**Verify:** a missing option or a non-HTTPS `BasePath` fails the start with `OptionsValidationException`. Send one message and assert `response.Success`.

### Configuration

| Option | Required | Notes |
| --- | --- | --- |
| `ApiKey` | Yes | Sent in the `Authorization` header. |
| `BasePath` | Yes | The base URL Infobip assigned to your account. It differs per account. HTTPS only. |
| `Sender` | Yes | Alphanumeric or numeric sender shown to recipients. |

### Design and runtime behavior

- **Failure kinds.** A message whose status group is undeliverable, expired, or rejected returns `InvalidRecipient`.
- **Success.** A single send's `ProviderMessageId` is the Infobip bulk id. A bulk send reports each recipient's message id separately.

---

## Headless.Sms.Twilio

The Twilio provider. One recipient per send; no bulk sender.

### Setup

```bash
dotnet add package Headless.Sms.Twilio
```

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHeadlessSms(setup => setup.UseTwilio(builder.Configuration.GetSection("Sms:Twilio")));

// As a named sender with its own Twilio client, keyed "marketing":
// setup.AddNamed("marketing", i => i.UseTwilio(builder.Configuration.GetSection("Sms:TwilioMarketing")));
```

```json
{
  "Sms": {
    "Twilio": {
      "Sid": "ACxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx",
      "AuthToken": "your-auth-token",
      "PhoneNumber": "+12025551234"
    }
  }
}
```

**Verify:** a missing `Sid` or `AuthToken`, or a `PhoneNumber` that is not a valid international number, fails the start with `OptionsValidationException`. Send one message and assert `response.Success`.

### Configuration

| Option | Required | Notes |
| --- | --- | --- |
| `Sid` | Yes | Twilio Account SID (`AC...`). |
| `AuthToken` | Yes | Twilio auth token. |
| `PhoneNumber` | Yes | E.164 sender number, such as `+12025551234`. |
| `MaxPrice` | No | Maximum USD price per message. Twilio rejects a send that would cost more. At least 0. |
| `Region` | No | Twilio region for data residency, such as `au1` or `ie1`. |
| `Edge` | No | Twilio edge location, such as `sydney` or `dublin`. |

### Design and runtime behavior

- **Cancellation.** The sender does not pass your token to the Twilio send call. A token already cancelled throws before the call. Cancelling during the call does not stop it.
- **Success.** `ProviderMessageId` is the Twilio message SID.
- **Client.** The default sender registers `ITwilioRestClient` only if none is registered yet, so a client you register first wins. A named sender builds its own keyed client from its own options.

---

## Headless.Sms.VictoryLink

The VictoryLink provider, with single and bulk sends.

### Setup

```bash
dotnet add package Headless.Sms.VictoryLink
```

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHeadlessSms(setup => setup.UseVictoryLink(builder.Configuration.GetSection("Sms:VictoryLink")));

// As a named sender, keyed "otp":
// setup.AddNamed("otp", i => i.UseVictoryLink(builder.Configuration.GetSection("Sms:VictoryLinkOtp")));
```

```json
{
  "Sms": {
    "VictoryLink": {
      "UserName": "your-username",
      "Password": "your-password",
      "Sender": "MyApp"
    }
  }
}
```

**Verify:** a missing required option or a non-HTTPS endpoint fails the start with `OptionsValidationException`. Send one message and assert `response.Success`.

### Configuration

| Option | Required | Default | Notes |
| --- | --- | --- | --- |
| `UserName` | Yes | | Account user name. |
| `Password` | Yes | | Account password. |
| `Sender` | Yes | | Sender name shown to recipients. |
| `Endpoint` | No | `https://smsvas.vlserv.com/VLSMSPlatformResellerAPI/NewSendingAPI/api/SMSSender/SendSMS` | HTTPS only. |

### Design and runtime behavior

- The sender reads VictoryLink's response code to decide success. A bulk send applies the one batch status to every recipient.

---

## Headless.Sms.Vodafone

The Vodafone Egypt enterprise SMS provider (web2sms), with single and bulk sends.

### Setup

```bash
dotnet add package Headless.Sms.Vodafone
```

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHeadlessSms(setup => setup.UseVodafone(builder.Configuration.GetSection("Sms:Vodafone")));

// As a named sender, keyed "promo":
// setup.AddNamed("promo", i => i.UseVodafone(builder.Configuration.GetSection("Sms:VodafonePromo")));
```

```json
{
  "Sms": {
    "Vodafone": {
      "AccountId": "your-account-id",
      "Password": "your-password",
      "SecureHash": "your-secure-hash",
      "Sender": "MyApp"
    }
  }
}
```

**Verify:** a missing required option or a non-HTTPS endpoint fails the start with `OptionsValidationException`. Send one message and assert `response.Success`.

### Configuration

| Option | Required | Default | Notes |
| --- | --- | --- | --- |
| `AccountId` | Yes | | Vodafone Egypt account identifier. |
| `Password` | Yes | | Account password. |
| `SecureHash` | Yes | | Shared secret issued with the account. Store it as a secret. |
| `Sender` | Yes | | Sender name shown to recipients. |
| `SendSmsEndpoint` | No | `https://e3len.vodafone.com.eg/web2sms/sms/submit/` | HTTPS only. |

### Design and runtime behavior

- Authentication uses the `SecureHash` with the account credentials. It is not an OAuth or JWT flow.
- A bulk send applies the one batch status to every recipient.
