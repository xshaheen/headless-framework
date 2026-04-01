---
domain: Email
packages: Emails.Abstractions, Emails.Core, Emails.Aws, Emails.Dev, Emails.Mailkit
---

# Email

## Table of Contents

- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [Headless.Emails.Abstractions](#headlessemailsabstractions)
    - [Problem Solved](#problem-solved)
    - [Key Features](#key-features)
    - [Installation](#installation)
    - [Usage](#usage)
    - [Configuration](#configuration)
    - [Dependencies](#dependencies)
    - [Side Effects](#side-effects)
- [Headless.Emails.Core](#headlessemailscore)
    - [Problem Solved](#problem-solved-1)
    - [Key Features](#key-features-1)
    - [Installation](#installation-1)
    - [Usage](#usage-1)
    - [Configuration](#configuration-1)
    - [Dependencies](#dependencies-1)
    - [Side Effects](#side-effects-1)
- [Headless.Emails.Aws](#headlessemailsaws)
    - [Problem Solved](#problem-solved-2)
    - [Key Features](#key-features-2)
    - [Installation](#installation-2)
    - [Quick Start](#quick-start)
    - [Configuration](#configuration-2)
        - [appsettings.json](#appsettingsjson)
    - [Dependencies](#dependencies-2)
    - [Side Effects](#side-effects-2)
- [Headless.Emails.Dev](#headlessemailsdev)
    - [Problem Solved](#problem-solved-3)
    - [Key Features](#key-features-3)
    - [Installation](#installation-3)
    - [Quick Start](#quick-start-1)
    - [Output Format](#output-format)
    - [Configuration](#configuration-3)
        - [Options](#options)
    - [Dependencies](#dependencies-3)
    - [Side Effects](#side-effects-3)
- [Headless.Emails.Mailkit](#headlessemailsmailkit)
    - [Problem Solved](#problem-solved-4)
    - [Key Features](#key-features-4)
    - [Installation](#installation-4)
    - [Quick Start](#quick-start-2)
    - [Configuration](#configuration-4)
        - [appsettings.json](#appsettingsjson-1)
        - [Options (`MailkitSmtpOptions`)](#options-mailkitsmtpoptions)
    - [Dependencies](#dependencies-4)
    - [Side Effects](#side-effects-4)

> Provider-agnostic email sending with implementations for AWS SES, SMTP (MailKit), and development/testing modes.

## Quick Orientation

Install `Headless.Emails.Abstractions` + `Headless.Emails.Core` + one provider. Code against `IEmailSender`.

- **Development/testing**: `Headless.Emails.Dev` — writes emails to a local file (`DevEmailSender`) or discards them (`NoopEmailSender`). No network calls.
- **AWS production**: `Headless.Emails.Aws` — sends via AWS SES v2. Call `AddAwsSesEmailSender()`.
- **SMTP (any server)**: `Headless.Emails.Mailkit` — sends via SMTP using MailKit. Supports SSL/TLS, authentication, and works with Gmail, Outlook, SendGrid, on-premises servers. Call `AddMailKitEmailSender()`.

`Headless.Emails.Core` provides shared MimeKit conversion utilities (`ConvertToMimeMessageAsync()`) used internally by Aws and Mailkit providers. Install it when using either provider.

Send emails via `IEmailSender.SendAsync(SendSingleEmailRequest)` which returns `SendSingleEmailResponse` with `IsSuccess` and `MessageId`.

## Agent Instructions

- Always use `Emails.Dev` (`AddDevEmailSender()` or `NoopEmailSender`) in development environments to prevent sending real emails. Gate with `builder.Environment.IsDevelopment()`.
- Use `IEmailSender` from `Headless.Emails.Abstractions` — never reference `AwsSesEmailSender`, `MailKitEmailSender`, or other concrete types in application code.
- `Emails.Core` is a utility package — it provides `ConvertToMimeMessageAsync()` extension for converting `SendSingleEmailRequest` to MimeKit `MimeMessage`. It is a dependency of `Emails.Aws` and `Emails.Mailkit`, not used directly in application code.
- `Emails.Aws` uses AWS SES **v2** (`AWSSDK.SimpleEmailV2`), not v1. Use `AddAwsSesEmailSender(awsOptions)` with `AWSOptions` from configuration.
- For SMTP via MailKit, configure `MailkitSmtpOptions`: `Server` (required), `Port` (default 25), `User`, `Password`, `SocketOptions` (StartTls, SslOnConnect).
- Check `SendSingleEmailResponse.IsSuccess` after sending — do not assume success. Log `response.Error` on failure.
- `SendSingleEmailRequest` supports `To`, `Cc`, `Bcc`, `Subject`, `HtmlBody`, `TextBody`, and attachments.
- All providers register `IEmailSender` as singleton.
- `DevEmailSender` appends to a file with separators — useful for inspecting all sent emails in development.

---

# Headless.Emails.Abstractions

Defines the unified interface for sending emails across different providers (AWS SES, SMTP/MailKit, development).

## Problem Solved

Provides a provider-agnostic email sending API, enabling seamless switching between email providers without changing application code.

## Key Features

- `IEmailSender` - Core interface for sending emails
- `SendSingleEmailRequest` - Request model with recipients, subject, body, attachments
- `SendSingleEmailResponse` - Response with success status and message ID

## Installation

```bash
dotnet add package Headless.Emails.Abstractions
```

## Usage

```csharp
public sealed class NotificationService(IEmailSender emailSender)
{
    public async Task SendWelcomeEmailAsync(string to, string name, CancellationToken ct)
    {
        var response = await emailSender.SendAsync(new SendSingleEmailRequest
        {
            To = [to],
            Subject = "Welcome!",
            HtmlBody = $"<h1>Hello {name}!</h1>",
            TextBody = $"Hello {name}!"
        }, ct).ConfigureAwait(false);

        if (!response.IsSuccess)
            _logger.LogError("Failed to send email: {Error}", response.Error);
    }
}
```

## Configuration

No configuration required. This is an abstractions-only package.

## Dependencies

None.

## Side Effects

## None.

# Headless.Emails.Core

Core utilities and MimeKit integration for email implementations.

## Problem Solved

Provides shared conversion logic to bridge framework email contracts with MimeKit, eliminating duplication across email provider implementations.

## Key Features

- `MimeMessage` conversion from `SendSingleEmailRequest`
- Address mapping (To, From, Cc, Bcc)
- Body building (Text/HTML)
- Attachment handling

## Installation

```bash
dotnet add package Headless.Emails.Core
```

## Usage

```csharp
// Convert framework request to MimeKit message
MimeMessage message = await request.ConvertToMimeMessageAsync(cancellationToken);
```

## Configuration

No configuration required.

## Dependencies

- `Headless.Emails.Abstractions`
- `Headless.Hosting`
- `MailKit`

## Side Effects

## None. This is a utility package.

# Headless.Emails.Aws

AWS SES (Simple Email Service) v2 implementation of the email sending abstraction.

## Problem Solved

Provides email sending via AWS SES using the unified `IEmailSender` abstraction, ideal for production deployments on AWS.

## Key Features

- Full `IEmailSender` implementation using AWS SES v2
- High deliverability and scalability
- AWS SDK configuration integration
- Attachment support

## Installation

```bash
dotnet add package Headless.Emails.Aws
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Using configuration
var awsOptions = builder.Configuration.GetAWSOptions();
builder.Services.AddAwsSesEmailSender(awsOptions);

// Or using explicit options
builder.Services.AddAwsSesEmailSender(new AWSOptions
{
    Region = RegionEndpoint.USEast1,
    Credentials = new BasicAWSCredentials("accessKey", "secretKey")
});
```

## Configuration

### appsettings.json

```json
{
    "AWS": {
        "Region": "us-east-1"
    }
}
```

## Dependencies

- `Headless.Emails.Core`
- `AWSSDK.SimpleEmailV2`
- `AWSSDK.Extensions.NETCore.Setup`

## Side Effects

- Registers `IAmazonSimpleEmailServiceV2` if not already registered
- Registers `IEmailSender` as singleton

---

# Headless.Emails.Dev

Development email implementations for local testing and debugging.

## Problem Solved

Provides safe email implementations for development/testing that don't send real emails, preventing accidental sends and enabling easy inspection of email content.

## Key Features

- `DevEmailSender` - Writes full email content to a local file
- `NoopEmailSender` - Discards emails silently
- No network calls required
- Easy inspection of email content

## Installation

```bash
dotnet add package Headless.Emails.Dev
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddDevEmailSender("path/to/emails.txt");
}
```

## Output Format

The `DevEmailSender` appends to the file with a separator:

```text
From: sender@example.com
To: recipient@example.com
Subject: Test Email
Message:
Hello World!
--------------------
```

## Configuration

### Options

```csharp
services.AddDevEmailSender("emails.txt"); // Path to output file
```

## Dependencies

- `Headless.Emails.Abstractions`
- `Headless.Hosting`

## Side Effects

- Registers `IEmailSender` as singleton
- Writes emails to specified file

---

# Headless.Emails.Mailkit

SMTP implementation of the email abstraction using MailKit.

## Problem Solved

Provides email sending via standard SMTP protocol using MailKit, supporting any SMTP server (Gmail, Outlook, SendGrid, on-premises, etc.).

## Key Features

- Full `IEmailSender` implementation using MailKit
- SSL/TLS support (StartTls, SslOnConnect)
- Authentication support
- Attachment support
- Works with any SMTP server

## Installation

```bash
dotnet add package Headless.Emails.Mailkit
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Using IConfiguration
builder.Services.AddMailKitEmailSender(builder.Configuration.GetSection("Smtp"));

// Or using action
builder.Services.AddMailKitEmailSender(options =>
{
    options.Server = "smtp.example.com";
    options.Port = 587;
    options.User = "user@example.com";
    options.Password = "securepassword";
    options.SocketOptions = SecureSocketOptions.StartTls;
});
```

## Configuration

### appsettings.json

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

### Options (`MailkitSmtpOptions`)

| Property        | Description                                    |
| --------------- | ---------------------------------------------- |
| `Server`        | SMTP server hostname (required)                |
| `Port`          | SMTP port (default: 25)                        |
| `User`          | Authentication username                        |
| `Password`      | Authentication password                        |
| `SocketOptions` | `SecureSocketOptions` (StartTls, SslOnConnect) |

## Dependencies

- `Headless.Emails.Core`

## Side Effects

- Registers `IEmailSender` as singleton
