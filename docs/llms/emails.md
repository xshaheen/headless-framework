---
domain: Email
packages: Emails.Abstractions, Emails.Core, Emails.Aws, Emails.Azure, Emails.Dev, Emails.Mailkit
---

# Email

## Table of Contents

- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [Core Concepts](#core-concepts)
- [Choosing a Provider](#choosing-a-provider)
- [Headless.Emails.Abstractions](#headlessemailsabstractions)
    - [Problem Solved](#problem-solved)
    - [Key Features](#key-features)
    - [Installation](#installation)
    - [Quick Start](#quick-start)
    - [Configuration](#configuration)
    - [Dependencies](#dependencies)
    - [Side Effects](#side-effects)
- [Headless.Emails.Core](#headlessemailscore)
    - [Problem Solved](#problem-solved-1)
    - [Key Features](#key-features-1)
    - [Installation](#installation-1)
    - [Quick Start](#quick-start-1)
    - [Configuration](#configuration-1)
    - [Dependencies](#dependencies-1)
    - [Side Effects](#side-effects-1)
- [Headless.Emails.Aws](#headlessemailsaws)
    - [Problem Solved](#problem-solved-2)
    - [Key Features](#key-features-2)
    - [Installation](#installation-2)
    - [Quick Start](#quick-start-2)
    - [Configuration](#configuration-2)
    - [Dependencies](#dependencies-2)
    - [Side Effects](#side-effects-2)
- [Headless.Emails.Azure](#headlessemailsazure)
    - [Problem Solved](#problem-solved-3)
    - [Key Features](#key-features-3)
    - [Installation](#installation-3)
    - [Quick Start](#quick-start-3)
    - [Configuration](#configuration-3)
    - [Dependencies](#dependencies-3)
    - [Side Effects](#side-effects-3)
- [Headless.Emails.Dev](#headlessemailsdev)
    - [Problem Solved](#problem-solved-4)
    - [Key Features](#key-features-4)
    - [Installation](#installation-4)
    - [Quick Start](#quick-start-4)
    - [Configuration](#configuration-4)
    - [Dependencies](#dependencies-4)
    - [Side Effects](#side-effects-4)
- [Headless.Emails.Mailkit](#headlessemailsmailkit)
    - [Problem Solved](#problem-solved-5)
    - [Key Features](#key-features-5)
    - [Installation](#installation-5)
    - [Quick Start](#quick-start-5)
    - [Configuration](#configuration-5)
    - [Dependencies](#dependencies-5)
    - [Side Effects](#side-effects-5)

> Provider-agnostic email sending with implementations for Azure Communication Services, AWS SES, SMTP (MailKit), and development/testing modes.

## Quick Orientation

Install `Headless.Emails.Abstractions` + one provider. Register with `AddHeadlessEmails(setup => setup.Use…())` — exactly one `Use*` provider per call. Code against `IEmailSender`.

- **Development/testing**: `Headless.Emails.Dev` — `UseDevelopment(path)` writes emails to a local file (`DevEmailSender`) or `UseNoop()` discards them silently (`NoopEmailSender`). No network calls.
- **Azure production**: `Headless.Emails.Azure` — sends via Azure Communication Services (ACS) Email. Call `UseAzure(...)`. Three auth modes: connection string, endpoint + access key, endpoint + managed-identity `TokenCredential`.
- **AWS production**: `Headless.Emails.Aws` — sends via AWS SES v2. Call `UseAwsSes(awsOptions)`.
- **SMTP (any server)**: `Headless.Emails.Mailkit` — sends via SMTP using MailKit with connection pooling. Supports SSL/TLS, authentication, and works with Gmail, Outlook, SendGrid, on-premises servers. Call `UseMailkit(...)`.

`Headless.Emails.Core` owns the setup builder (`AddHeadlessEmails`, `HeadlessEmailsSetupBuilder`, `IEmailProviderOptionsExtension`) plus internal MimeKit conversion (`ConvertToMimeMessageAsync()`) and attachment content-type derivation (`EmailAttachmentContentType.Resolve()`). Providers pull it transitively — you rarely install it directly.

Send emails via `IEmailSender.SendAsync(SendSingleEmailRequest)` which returns `SendSingleEmailResponse` with `Success` and `FailureError`.

## Agent Instructions

- Register exactly one provider per container: `services.AddHeadlessEmails(setup => setup.Use…())`. Zero, multiple, or a repeated `AddHeadlessEmails` on the same `IServiceCollection` throws `InvalidOperationException` at registration time. The available `Use*` calls are `UseAzure`, `UseAwsSes`, `UseMailkit`, `UseDevelopment`, `UseNoop`.
- Use `IEmailSender` from `Headless.Emails.Abstractions` — never reference `AzureCommunicationEmailSender`, `AwsSesEmailSender`, `MailkitEmailSender`, or other concrete types in application code.
- Always use `Emails.Dev` (`UseDevelopment(path)` or `UseNoop()`) in development environments to prevent sending real emails. Gate with `builder.Environment.IsDevelopment()`.
- `SendSingleEmailRequest` requires both `From` (an `EmailRequestAddress`) and `Destination` (an `EmailRequestDestination`) — they are `required` properties. Provide at least one of `MessageHtml` or `MessageText`; every sender calls `SendSingleEmailRequest.EnsureHasBody()` and throws `InvalidOperationException` when both are null or whitespace (`NoopEmailSender` is the exception — it discards everything and never validates).
- Check `response.Success` after calling `SendAsync` — do not assume success. Read `response.FailureError` (non-null when `Success` is false) on failure.
- `Emails.Azure` requires a sender domain that is **verified and linked** in the Communication Services resource. A misconfigured sender surfaces as a failed `SendSingleEmailResponse`, not an exception you can pre-validate. `UseAzure` requires exactly one auth mode (connection string, endpoint + access key, or endpoint + `TokenCredential`); the `IConfiguration` overload binds only the string/key modes (a `TokenCredential` cannot be bound from configuration). ACS ignores the sender's display name (`senderAddress` is a bare string).
- `Emails.Aws` uses AWS SES **v2** (`AWSSDK.SimpleEmailV2`), not v1. Pass `AWSOptions?` (nullable — `null` uses the default `AWSOptions` registered in the DI container) to `UseAwsSes(...)`.
- For SMTP via MailKit, configure `MailkitSmtpOptions`: `Server` (required), `Port` (default 587), `SocketOptions` (default `StartTls`), `Timeout` (default 30s), `MaxPoolSize` (default 10; the pool always keeps one fast-path slot, so `0` retains at most one connection rather than disabling pooling).
- `MailkitEmailSender` uses an `ObjectPool<SmtpClient>` — SMTP connections are pooled and reused. Authentication happens on reconnect, bounded by `Timeout` (which otherwise governs only read/write). If `AuthenticationException` is thrown (wrong credentials), it propagates — it is not swallowed like SMTP command/protocol errors — and the connection is discarded rather than returned to the pool, so a later send never reuses an unauthenticated client.
- All providers register `IEmailSender` as singleton.
- `DevEmailSender` appends to a file with separators — it writes `MessageText` preferring it over `MessageHtml` for readability.
- `Emails.Core` provides the builder and shared utilities consumed by providers — it is not used directly in application code (except the `AddHeadlessEmails` entry point, which is the provider-agnostic registration call).

## Core Concepts

### Request and Response model

`SendSingleEmailRequest` is an immutable record with `required` properties:

| Property | Type | Description |
|---|---|---|
| `From` | `EmailRequestAddress` | Sender address — `string` implicitly converts via `EmailRequestAddress.FromString()` |
| `Destination` | `EmailRequestDestination` | Recipients: `ToAddresses` (required), `CcAddresses`, `BccAddresses` |
| `Subject` | `string` | Email subject line |
| `MessageHtml` | `string?` | HTML body (optional, but provide at least one body) |
| `MessageText` | `string?` | Plain-text body (optional, but provide at least one body) |
| `Attachments` | `IReadOnlyList<EmailRequestAttachment>` | File attachments (default empty); each carries a `Name` + `File` (bytes) only |

`EmailRequestAddress` accepts a display name: `new EmailRequestAddress("addr@ex.com", "Alice")` or bare string `"addr@ex.com"` (implicit conversion).

All contract value types (`SendSingleEmailRequest`, `EmailRequestAddress`, `EmailRequestDestination`, `EmailRequestAttachment`) are immutable `sealed record`s. `EmailRequestAttachment` carries `Name`, `File` (`ReadOnlyMemory<byte>`), and an optional `ContentType` (inferred from `Name` when null).

`SendSingleEmailResponse` is a closed type (private constructor). Use the static factory pair that providers call:
- `SendSingleEmailResponse.Succeeded()` — `Success = true`
- `SendSingleEmailResponse.Failed(string failureError)` — `Success = false`, `FailureError` non-null

The `[MemberNotNullWhen(false, nameof(FailureError))]` attribute enables null-safe access: if `response.Success` is false, the compiler knows `FailureError` is not null.

### Provider wiring (unified setup builder)

Email uses the framework's unified provider setup-builder grammar. `AddHeadlessEmails(Action<HeadlessEmailsSetupBuilder>)` (in `Headless.Emails.Core`) is the single registration entry point. Inside the delegate, each provider package contributes a `Use*` extension member on `HeadlessEmailsSetupBuilder` that registers an `IEmailProviderOptionsExtension`; the core gate then enforces **exactly one provider** and runs that extension's wiring.

Zero providers, multiple providers in one delegate, or a repeated `AddHeadlessEmails` on the same `IServiceCollection` all throw `InvalidOperationException` at registration time — a host resolves a single `IEmailSender`, so exactly one provider is required.

Since the attachment contract (`EmailRequestAttachment`) carries only a name and bytes, providers whose transport needs an explicit MIME type derive it from the file name via `EmailAttachmentContentType.Resolve()` (MimeKit lookup, `application/octet-stream` fallback). The `Emails.Core` conversion utilities (`ConvertToMimeMessageAsync()`) remain an internal detail shared by Aws and Mailkit; consumers never call them directly.

## Choosing a Provider

| Provider | Use when | Avoid when | Trade-offs |
|---|---|---|---|
| `Headless.Emails.Azure` | Running on Azure, want ACS Email with managed-identity auth and a verified sender domain | No Azure Communication Services resource; want SMTP portability | ACS REST API (not SMTP); blocks until ACS reaches a terminal status (`WaitUntil.Completed`); managed-domain send limit is low (5/min); sender display name not honored; `Azure.Communication.Email` dependency |
| `Headless.Emails.Aws` | Running on AWS, need SES deliverability, compliance, and send-rate guarantees | No AWS account; want SMTP portability | SES API (not SMTP); simple sends use a REST path, attachments fall back to raw MIME via SES; AWS SDK dependency |
| `Headless.Emails.Mailkit` | Need SMTP portability (SendGrid, Gmail, Outlook, on-premises); want connection pooling | SES/ACS API required; not running SMTP server | SMTP is synchronous per-connection; pooling amortizes connect cost; `AuthenticationException` propagates (not returned as failure) |
| `Headless.Emails.Dev` | Local development, CI, integration tests | Production traffic | No real delivery; `DevEmailSender` writes to disk; `NoopEmailSender` silently discards |

---

# Headless.Emails.Abstractions

Defines the unified interface for sending emails across different providers (Azure Communication Services, AWS SES, SMTP/MailKit, development).

## Problem Solved

Provides a provider-agnostic email sending API for switching email providers without changing application code.

## Key Features

- `IEmailSender` — core interface with a single `SendAsync(SendSingleEmailRequest, CancellationToken)` method returning `ValueTask<SendSingleEmailResponse>`
- `SendSingleEmailRequest` — immutable record with required `From`, `Destination`, `Subject`; optional `MessageHtml`, `MessageText`, `Attachments`
- `EmailRequestAddress` — wraps email address + optional display name; supports implicit conversion from `string`
- `EmailRequestDestination` — sealed record grouping `ToAddresses` (required), `CcAddresses`, `BccAddresses`
- `EmailRequestAttachment` — sealed record: `Name` + `File` (`ReadOnlyMemory<byte>`) + optional `ContentType`
- `SendSingleEmailResponse` — closed result type with `Success` bool and nullable `FailureError` string

## Installation

```bash
dotnet add package Headless.Emails.Abstractions
```

## Quick Start

```csharp
public sealed class NotificationService(IEmailSender emailSender)
{
    public async Task SendWelcomeEmailAsync(string to, string name, CancellationToken ct)
    {
        var response = await emailSender.SendAsync(new SendSingleEmailRequest
        {
            From = "noreply@example.com",
            Destination = new EmailRequestDestination
            {
                ToAddresses = [new EmailRequestAddress(to)],
            },
            Subject = "Welcome!",
            MessageHtml = $"<h1>Hello {name}!</h1>",
            MessageText = $"Hello {name}!",
        }, ct).ConfigureAwait(false);

        if (!response.Success)
        {
            logger.LogError("Failed to send email: {Error}", response.FailureError);
        }
    }
}
```

## Configuration

No configuration required. This is an abstractions-only package.

## Dependencies

None.

## Side Effects

None.

---

# Headless.Emails.Core

Setup builder, MimeKit integration, and shared utilities for email implementations.

## Problem Solved

Owns the unified email setup builder (`AddHeadlessEmails`) plus shared conversion logic that bridges the framework email contracts with MimeKit, eliminating duplication across the provider implementations.

## Key Features

- `AddHeadlessEmails(Action<HeadlessEmailsSetupBuilder>)` — the single provider-agnostic registration entry point, with an exactly-one-provider gate
- `HeadlessEmailsSetupBuilder` — receives `Use*` provider selections; `IEmailProviderOptionsExtension` — the hook each provider implements
- `EmailAttachmentContentType.Resolve(fileName)` — derives an attachment MIME type from its file name (MimeKit lookup, `application/octet-stream` fallback)
- `EmailToMimeMessageConverter.ConvertToMimeMessageAsync()` — converts `SendSingleEmailRequest` to a MimeKit `MimeMessage` (internal extension; visible to the Aws/Mailkit providers via `InternalsVisibleTo`)
- `MapToMailboxAddress()` — maps an `EmailRequestAddress` to a MimeKit `MailboxAddress` (internal)
- Full address mapping (From, To, Cc, Bcc), body building (text + HTML via `BodyBuilder`), and attachment streaming

## Design Notes

The builder carries no shared, cross-provider feature options — it is provider-selection-only; each provider binds its own options inside its `Use*` member. The gate counts registered extensions and rejects zero, multiple, or a repeated `AddHeadlessEmails` on the same `IServiceCollection` (a host resolves a single `IEmailSender`). The MimeKit converter returns a `MimeMessage` that callers dispose; the implementation disposes the message if an exception occurs during construction, preventing a resource leak.

## Installation

```bash
dotnet add package Headless.Emails.Core
```

## Quick Start

```csharp
// Provider-agnostic registration entry point (a provider package supplies the Use* member):
builder.Services.AddHeadlessEmails(setup => setup.UseNoop());

// Public content-type helper:
var contentType = EmailAttachmentContentType.Resolve("invoice.pdf"); // "application/pdf"
```

## Configuration

No configuration required.

## Dependencies

- `Headless.Emails.Abstractions`
- `Headless.Hosting`
- `MailKit`

## Side Effects

None directly. `AddHeadlessEmails` registers a provider-registration marker and delegates all `IEmailSender` wiring to the selected provider's extension.

---

# Headless.Emails.Aws

AWS SES (Simple Email Service) v2 implementation of the email sending abstraction.

## Problem Solved

Provides email sending via AWS SES v2 using the unified `IEmailSender` abstraction, ideal for production deployments on AWS.

## Key Features

- Full `IEmailSender` implementation using AWS SES v2 (`AWSSDK.SimpleEmailV2`)
- Simple sends (no attachments) use the SES structured API path — no MIME serialization
- Attachment sends serialize to raw MIME and use the SES raw message path
- AWS SDK configuration integration (`AWSOptions` from `AWSSDK.Extensions.NETCore.Setup`)
- SES-specific exceptions (`MessageRejectedException`, `BadRequestException`, `NotFoundException`, `AccountSuspendedException`, `MailFromDomainNotVerifiedException`, `LimitExceededException`, `TooManyRequestsException`, `SendingPausedException`) propagate — not wrapped in `Failed()`
- BCC is delivered via the SES envelope (`Destination`) and the `Bcc` header is hidden from the serialized MIME on the raw (attachment) path, so BCC recipients are never disclosed
- Non-PII logging on non-success HTTP responses (status code, request ID, message ID — no recipient/sender addresses)

## Installation

```bash
dotnet add package Headless.Emails.Aws
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Option 1: from configuration (reads AWS:Region, credentials from environment/profile)
var awsOptions = builder.Configuration.GetAWSOptions();
builder.Services.AddHeadlessEmails(setup => setup.UseAwsSes(awsOptions));

// Option 2: explicit credentials
builder.Services.AddHeadlessEmails(setup => setup.UseAwsSes(new AWSOptions
{
    Region = RegionEndpoint.USEast1,
    Credentials = new BasicAWSCredentials("accessKey", "secretKey"),
}));

// Option 3: use default AWSOptions already registered in DI (pass null)
builder.Services.AddHeadlessEmails(setup => setup.UseAwsSes(null));
```

## Configuration

```json
{
    "AWS": {
        "Region": "us-east-1"
    }
}
```

Credentials are resolved from the standard AWS credential chain (environment variables, `~/.aws/credentials`, IAM role) when not passed explicitly.

## Dependencies

- `Headless.Emails.Core`
- `Headless.Extensions`
- `AWSSDK.SimpleEmailV2`
- `AWSSDK.Extensions.NETCore.Setup`

## Side Effects

- Registers `IAmazonSimpleEmailServiceV2` via `TryAddAWSService` (no-op if already registered)
- Registers `IEmailSender` as singleton

---

# Headless.Emails.Azure

Azure Communication Services (ACS) Email implementation of the email sending abstraction.

## Problem Solved

Provides email sending via Azure Communication Services using the unified `IEmailSender` abstraction — the intended cloud-email backend for Azure-hosted consumers, with managed-identity support.

## Key Features

- Full `IEmailSender` implementation over `Azure.Communication.Email`
- Three authentication modes: connection string, endpoint + access key, and endpoint + managed-identity `TokenCredential`
- Maps `SendSingleEmailRequest` (From, To/Cc/Bcc, Subject, HTML + plain-text bodies, attachments) to an ACS `EmailMessage`
- Attachment content type derived from the file name via `EmailAttachmentContentType.Resolve()` (`application/octet-stream` fallback)
- Both a thrown `RequestFailedException` and a completed-but-failed terminal status map to `SendSingleEmailResponse.Failed(...)`
- Non-PII logging on failure (operation id, status, error code — no recipient/sender addresses)

## Design Notes

The send uses `EmailClient.SendAsync(WaitUntil.Completed, …)`, so the call blocks until ACS reaches a terminal state — matching the contract's "accepted for delivery" success semantics. ACS can complete a long-running send with a non-`Succeeded` status **without throwing**, so the sender inspects `operation.Value.Status` and treats any terminal non-`Succeeded` state as a failure (an exception-only check would report rejected mail as delivered). Only `Succeeded` returns `Succeeded()`; unrelated exceptions (cancellation, argument errors) propagate.

The package depends on `Azure.Core` (not `Azure.Identity`): supply your own `DefaultAzureCredential` through the delegate overload to keep the dependency surface narrow. The `IConfiguration` overload binds only the connection-string and endpoint + access-key modes. ACS's `senderAddress` is a bare string, so the sender's display name is not honored. No custom retry loop is added — `Azure.Core`'s pipeline already retries 429/5xx honoring `Retry-After`. The sender domain must be verified and linked in the Communication Services resource; managed-domain send limits are low (5/min; custom domains 30/min).

## Installation

```bash
dotnet add package Headless.Emails.Azure
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Option 1: connection string or endpoint + access key, bound from configuration
builder.Services.AddHeadlessEmails(setup =>
    setup.UseAzure(builder.Configuration.GetSection("AzureEmail")));

// Option 2: endpoint + access key (delegate)
builder.Services.AddHeadlessEmails(setup => setup.UseAzure(options =>
{
    options.Endpoint = new Uri("https://my-resource.communication.azure.com/");
    options.AccessKey = builder.Configuration["AzureEmail:AccessKey"]!;
}));

// Option 3: managed identity (TokenCredential — delegate only; requires the Azure.Identity package)
builder.Services.AddHeadlessEmails(setup => setup.UseAzure(options =>
{
    options.Endpoint = new Uri("https://my-resource.communication.azure.com/");
    options.TokenCredential = new DefaultAzureCredential();
}));
```

## Configuration

```json
{
    "AzureEmail": {
        "ConnectionString": "endpoint=https://my-resource.communication.azure.com/;accesskey=<key>"
    }
}
```

`AzureCommunicationEmailOptions` properties — exactly one auth mode must be configured:

| Property | Type | Description |
|---|---|---|
| `ConnectionString` | `string?` | Resource connection string (connection-string mode) |
| `Endpoint` | `Uri?` | Resource endpoint; pair with `AccessKey` or `TokenCredential` |
| `AccessKey` | `string?` | Resource access key (access-key mode) |
| `TokenCredential` | `TokenCredential?` | Managed-identity credential (delegate overload only — not bindable from configuration) |

## Dependencies

- `Headless.Emails.Abstractions`
- `Headless.Emails.Core`
- `Azure.Communication.Email`

## Side Effects

- Binds and validates `AzureCommunicationEmailOptions` (exactly one auth mode required)
- Registers `EmailClient` as singleton (constructed from the configured auth mode)
- Registers `IEmailSender` as singleton

---

# Headless.Emails.Dev

Development email implementations for local testing and debugging.

## Problem Solved

Provides safe email implementations for development and testing that do not send real emails, preventing accidental sends and enabling easy inspection of email content locally.

## Key Features

- `DevEmailSender` — writes full email content to a local file; appends with a `--------------------` separator per message; prefers `MessageText` over `MessageHtml` for readability. Writes are serialized so concurrent sends do not interleave, and a body-less request throws `InvalidOperationException` (same `EnsureHasBody()` guard as the real providers)
- `NoopEmailSender` — silently discards all emails, always returns `Succeeded()` (never validates — the explicit "disable email" sender)
- No network calls, no external dependencies beyond the abstractions and core packages

## Installation

```bash
dotnet add package Headless.Emails.Dev
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    // Write emails to a local file for inspection
    builder.Services.AddHeadlessEmails(setup => setup.UseDevelopment("path/to/emails.txt"));

    // Or discard silently (useful in automated tests)
    // builder.Services.AddHeadlessEmails(setup => setup.UseNoop());
}
```

Example output written to the file:

```text
From: sender@example.com
To: recipient@example.com
Subject: Test Email
Message:

Hello World!
--------------------
```

## Configuration

`UseDevelopment(string filePath)` — the file path is the only parameter. The file is created if it does not exist; emails are appended.

`UseNoop()` — no parameters. Useful in test projects where you want `IEmailSender` resolved but do not want file I/O.

## Dependencies

- `Headless.Emails.Abstractions`
- `Headless.Emails.Core`

## Side Effects

- `UseDevelopment` registers `IEmailSender` as singleton (instance of `DevEmailSender`); appends to the specified file on each `SendAsync` call
- `UseNoop` registers `IEmailSender` as singleton (instance of `NoopEmailSender`); no I/O

---

# Headless.Emails.Mailkit

SMTP implementation of the email abstraction using MailKit with connection pooling.

## Problem Solved

Provides email sending via standard SMTP protocol using MailKit, supporting any SMTP server (Gmail, Outlook, SendGrid, on-premises, etc.) with connection pooling to amortize reconnect cost.

## Key Features

- Full `IEmailSender` implementation using MailKit
- SMTP connection pool (`ObjectPool<SmtpClient>`) — connections are retained and reused across sends
- SSL/TLS support: `SecureSocketOptions.StartTls` (default), `SslOnConnect`, `None`, `Auto`
- Optional authentication (username + password); anonymous SMTP when credentials are omitted
- Three `UseMailkit` overloads: `IConfiguration`, `Action<MailkitSmtpOptions>`, `Action<MailkitSmtpOptions, IServiceProvider>`
- `SmtpCommandException` and `SmtpProtocolException` are caught and returned as `Failed()` responses; `AuthenticationException` propagates

## Design Notes

The pool (`MaxPoolSize`, default 10) amortizes TCP connect + TLS handshake across concurrent sends. Each `SmtpClient` is reconnected (and authenticated if credentials are set) lazily when retrieved from the pool in a disconnected or unauthenticated state; the connect/authenticate phase is bounded by `Timeout`. Authentication failures (`AuthenticationException`) are intentionally re-thrown rather than returned as `Failed()` — they represent configuration errors, not transient delivery failures, and must be surfaced at startup or on first send. A client left connected-but-unauthenticated by such a failure is disposed on return instead of being pooled, so it is never reused with authentication skipped.

## Installation

```bash
dotnet add package Headless.Emails.Mailkit
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Option 1: from configuration section
builder.Services.AddHeadlessEmails(setup => setup.UseMailkit(builder.Configuration.GetSection("Smtp")));

// Option 2: action
builder.Services.AddHeadlessEmails(setup => setup.UseMailkit(options =>
{
    options.Server = "smtp.example.com";
    options.Port = 587;
    options.User = "user@example.com";
    options.Password = "securepassword";
    options.SocketOptions = SecureSocketOptions.StartTls;
}));

// Option 3: action with IServiceProvider
builder.Services.AddHeadlessEmails(setup => setup.UseMailkit((options, sp) =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    options.Server = cfg["Smtp:Server"]!;
    options.Port = int.Parse(cfg["Smtp:Port"]!);
}));
```

## Configuration

```json
{
    "Smtp": {
        "Server": "smtp.example.com",
        "Port": 587,
        "User": "user@example.com",
        "Password": "securepassword",
        "SocketOptions": "StartTls"
    }
}
```

`MailkitSmtpOptions` properties:

| Property | Default | Description |
|---|---|---|
| `Server` | *(required)* | SMTP server hostname |
| `Port` | `587` | SMTP port |
| `User` | `null` | Authentication username; omit for anonymous SMTP |
| `Password` | `null` | Authentication password; use user-secrets or key vault in production |
| `SocketOptions` | `StartTls` | `SecureSocketOptions`: `None`, `Auto`, `StartTls`, `StartTlsWhenAvailable`, `SslOnConnect` |
| `Timeout` | `30s` | Per-connection timeout |
| `MaxPoolSize` | `10` | Max pooled SMTP connections; `0` retains at most one (the pool always keeps a fast-path slot) |

## Dependencies

- `Headless.Emails.Core`
- `MailKit`

## Side Effects

- Registers `IPooledObjectPolicy<SmtpClient>` as singleton
- Registers `ObjectPool<SmtpClient>` as singleton
- Registers `IEmailSender` as singleton
