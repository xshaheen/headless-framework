---
domain: Email
packages: Emails.Abstractions, Emails.Core, Emails.Aws, Emails.Dev, Emails.Mailkit
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
- [Headless.Emails.Dev](#headlessemailsdev)
    - [Problem Solved](#problem-solved-3)
    - [Key Features](#key-features-3)
    - [Installation](#installation-3)
    - [Quick Start](#quick-start-3)
    - [Configuration](#configuration-3)
    - [Dependencies](#dependencies-3)
    - [Side Effects](#side-effects-3)
- [Headless.Emails.Mailkit](#headlessemailsmailkit)
    - [Problem Solved](#problem-solved-4)
    - [Key Features](#key-features-4)
    - [Installation](#installation-4)
    - [Quick Start](#quick-start-4)
    - [Configuration](#configuration-4)
    - [Dependencies](#dependencies-4)
    - [Side Effects](#side-effects-4)

> Provider-agnostic email sending with implementations for AWS SES, SMTP (MailKit), and development/testing modes.

## Quick Orientation

Install `Headless.Emails.Abstractions` + one provider. Code against `IEmailSender`.

- **Development/testing**: `Headless.Emails.Dev` — writes emails to a local file (`DevEmailSender`) or discards them silently (`NoopEmailSender`). No network calls.
- **AWS production**: `Headless.Emails.Aws` — sends via AWS SES v2. Call `AddAwsSesEmailSender()`.
- **SMTP (any server)**: `Headless.Emails.Mailkit` — sends via SMTP using MailKit with connection pooling. Supports SSL/TLS, authentication, and works with Gmail, Outlook, SendGrid, on-premises servers. Call `AddMailKitEmailSender()`.

`Headless.Emails.Core` provides shared MimeKit conversion utilities (internal API) used by the Aws and Mailkit providers. Both providers pull it transitively; do not install `Headless.Emails.Core` directly.

Send emails via `IEmailSender.SendAsync(SendSingleEmailRequest)` which returns `SendSingleEmailResponse` with `Success` and `FailureError`.

## Agent Instructions

- Always use `Emails.Dev` (`AddDevEmailSender()` or `AddNoopEmailSender()`) in development environments to prevent sending real emails. Gate with `builder.Environment.IsDevelopment()`.
- Use `IEmailSender` from `Headless.Emails.Abstractions` — never reference `AwsSesEmailSender`, `MailkitEmailSender`, or other concrete types in application code.
- `SendSingleEmailRequest` requires both `From` (an `EmailRequestAddress`) and `Destination` (an `EmailRequestDestination`) — they are `required` properties. Provide at least one of `MessageHtml` or `MessageText`; every sender calls `SendSingleEmailRequest.EnsureHasBody()` and throws `InvalidOperationException` when both are null or whitespace (`NoopEmailSender` is the exception — it discards everything and never validates).
- Check `response.Success` after calling `SendAsync` — do not assume success. Read `response.FailureError` (non-null when `Success` is false) on failure.
- `Emails.Aws` uses AWS SES **v2** (`AWSSDK.SimpleEmailV2`), not v1. Pass `AWSOptions?` (nullable — `null` uses the default `AWSOptions` registered in the DI container) to `AddAwsSesEmailSender()`.
- For SMTP via MailKit, configure `MailkitSmtpOptions`: `Server` (required), `Port` (default 587), `SocketOptions` (default `StartTls`), `Timeout` (default 30s), `MaxPoolSize` (default 10; the pool always keeps one fast-path slot, so `0` retains at most one connection rather than disabling pooling).
- `MailkitEmailSender` uses an `ObjectPool<SmtpClient>` — SMTP connections are pooled and reused. Authentication happens on reconnect, bounded by `Timeout` (which otherwise governs only read/write). If `AuthenticationException` is thrown (wrong credentials), it propagates — it is not swallowed like SMTP command/protocol errors — and the connection is discarded rather than returned to the pool, so a later send never reuses an unauthenticated client.
- All providers register `IEmailSender` as singleton.
- `DevEmailSender` appends to a file with separators — it writes `MessageText` preferring it over `MessageHtml` for readability.
- `Emails.Core` is a utility package consumed by providers — it is not used directly in application code.

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
| `Attachments` | `IReadOnlyList<EmailRequestAttachment>` | File attachments (default empty) |

`EmailRequestAddress` accepts a display name: `new EmailRequestAddress("addr@ex.com", "Alice")` or bare string `"addr@ex.com"` (implicit conversion).

All contract value types (`SendSingleEmailRequest`, `EmailRequestAddress`, `EmailRequestDestination`, `EmailRequestAttachment`) are immutable `sealed record`s. `EmailRequestAttachment` carries `Name`, `File` (`ReadOnlyMemory<byte>`), and an optional `ContentType` (inferred from `Name` when null).

`SendSingleEmailResponse` is a closed type (private constructor). Use the static factory pair that providers call:
- `SendSingleEmailResponse.Succeeded()` — `Success = true`
- `SendSingleEmailResponse.Failed(string failureError)` — `Success = false`, `FailureError` non-null

The `[MemberNotNullWhen(false, nameof(FailureError))]` attribute enables null-safe access: if `response.Success` is false, the compiler knows `FailureError` is not null.

### Provider wiring

Each provider registers `IEmailSender` as a singleton. Only one provider may be wired per DI container. The `Emails.Core` conversion utilities are an internal implementation detail shared by Aws and Mailkit; consumers never call `ConvertToMimeMessageAsync()` directly.

## Choosing a Provider

| Provider | Use when | Avoid when | Trade-offs |
|---|---|---|---|
| `Headless.Emails.Aws` | Running on AWS, need SES deliverability, compliance, and send-rate guarantees | No AWS account; want SMTP portability | SES API (not SMTP); simple sends use a REST path, attachments fall back to raw MIME via SES; AWS SDK dependency |
| `Headless.Emails.Mailkit` | Need SMTP portability (SendGrid, Gmail, Outlook, on-premises); want connection pooling | SES API required; not running SMTP server | SMTP is synchronous per-connection; pooling amortizes connect cost; `AuthenticationException` propagates (not returned as failure) |
| `Headless.Emails.Dev` | Local development, CI, integration tests | Production traffic | No real delivery; `DevEmailSender` writes to disk; `NoopEmailSender` silently discards |

---

# Headless.Emails.Abstractions

Defines the unified interface for sending emails across different providers (AWS SES, SMTP/MailKit, development).

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

Core utilities and MimeKit integration for email implementations.

## Problem Solved

Provides shared conversion logic to bridge the framework email contracts with MimeKit, eliminating duplication across the Aws and Mailkit provider implementations.

## Key Features

- `EmailToMimeMessageConverter.ConvertToMimeMessageAsync()` — converts `SendSingleEmailRequest` to a MimeKit `MimeMessage` (internal extension; visible to the Aws/Mailkit providers via `InternalsVisibleTo`)
- `MapToMailboxAddress()` — maps an `EmailRequestAddress` to a MimeKit `MailboxAddress` (internal)
- Full address mapping (From, To, Cc, Bcc), body building (text + HTML via `BodyBuilder`), and attachment streaming

## Design Notes

This package is consumed transitively by `Headless.Emails.Aws` and `Headless.Emails.Mailkit` — application code does not reference it directly. The converter returns a `MimeMessage` that callers are responsible for disposing; the implementation disposes the message if an exception occurs during construction, preventing a resource leak.

## Installation

```bash
dotnet add package Headless.Emails.Core
```

## Quick Start

```csharp
// Internal to the Aws/Mailkit providers (internal API, not reachable from application code).
// Illustrative of what a provider does internally:
using var mimeMessage = await request.ConvertToMimeMessageAsync(cancellationToken);
```

## Configuration

No configuration required.

## Dependencies

- `Headless.Emails.Abstractions`
- `MimeKit`

## Side Effects

None. This is a utility package with no DI registrations.

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
- Non-PII logging on non-success HTTP responses (status code, request ID, message ID — no recipient/sender addresses)

## Installation

```bash
dotnet add package Headless.Emails.Aws
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Option 1: from configuration (reads AWS:Region, AWS credentials from environment/profile)
var awsOptions = builder.Configuration.GetAWSOptions();
builder.Services.AddAwsSesEmailSender(awsOptions);

// Option 2: explicit credentials
builder.Services.AddAwsSesEmailSender(new AWSOptions
{
    Region = RegionEndpoint.USEast1,
    Credentials = new BasicAWSCredentials("accessKey", "secretKey"),
});

// Option 3: use default AWSOptions already registered in DI (pass null)
builder.Services.AddAwsSesEmailSender(null);
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
- `AWSSDK.SimpleEmailV2`
- `AWSSDK.Extensions.NETCore.Setup`

## Side Effects

- Registers `IAmazonSimpleEmailServiceV2` via `TryAddAWSService` (no-op if already registered)
- Registers `IEmailSender` as singleton

---

# Headless.Emails.Dev

Development email implementations for local testing and debugging.

## Problem Solved

Provides safe email implementations for development and testing that do not send real emails, preventing accidental sends and enabling easy inspection of email content locally.

## Key Features

- `DevEmailSender` — writes full email content to a local file; appends with a `--------------------` separator per message; prefers `MessageText` over `MessageHtml` for readability
- `NoopEmailSender` — silently discards all emails, always returns `Succeeded()`
- No network calls, no external dependencies beyond the abstractions package

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
    builder.Services.AddDevEmailSender("path/to/emails.txt");

    // Or discard silently (useful in automated tests)
    // builder.Services.AddNoopEmailSender();
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

`AddDevEmailSender(string filePath)` — the file path is the only parameter. The file is created if it does not exist; emails are appended.

`AddNoopEmailSender()` — no parameters. Useful in test projects where you want `IEmailSender` resolved but do not want file I/O.

## Dependencies

- `Headless.Emails.Abstractions`

## Side Effects

- `AddDevEmailSender` registers `IEmailSender` as singleton (instance of `DevEmailSender`); appends to the specified file on each `SendAsync` call
- `AddNoopEmailSender` registers `IEmailSender` as singleton (instance of `NoopEmailSender`); no I/O

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
- Three registration overloads: `IConfiguration`, `Action<MailkitSmtpOptions>`, `Action<MailkitSmtpOptions, IServiceProvider>`
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
builder.Services.AddMailKitEmailSender(builder.Configuration.GetSection("Smtp"));

// Option 2: action
builder.Services.AddMailKitEmailSender(options =>
{
    options.Server = "smtp.example.com";
    options.Port = 587;
    options.User = "user@example.com";
    options.Password = "securepassword";
    options.SocketOptions = SecureSocketOptions.StartTls;
});

// Option 3: action with IServiceProvider
builder.Services.AddMailKitEmailSender((options, sp) =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    options.Server = cfg["Smtp:Server"]!;
    options.Port = int.Parse(cfg["Smtp:Port"]!);
});
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
