# Framework.Emails.Mailkit

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
dotnet add package Framework.Emails.Mailkit
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

| Property | Description |
|----------|-------------|
| `Server` | SMTP server hostname (required) |
| `Port` | SMTP port (default: 25) |
| `User` | Authentication username |
| `Password` | Authentication password |
| `SocketOptions` | `SecureSocketOptions` (StartTls, SslOnConnect) |

## Dependencies

- `Framework.Emails.Core`

## Side Effects

- Registers `IEmailSender` as singleton
