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

The pool (`MaxPoolSize`, default 10) amortizes TCP connect + TLS handshake across concurrent sends. Each `SmtpClient` is reconnected (and authenticated if credentials are set) lazily when retrieved from the pool in a disconnected or unauthenticated state; the connect/authenticate phase is bounded by `Timeout` (which otherwise governs only read/write). Authentication failures (`AuthenticationException`) are intentionally re-thrown rather than returned as `Failed()` — they represent configuration errors, not transient delivery failures, and must be surfaced at startup or on first send. A client left connected-but-unauthenticated by such a failure is disposed on return instead of being pooled, so it is never reused with authentication skipped.

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
builder.Services.AddHeadlessEmails(setup =>
    setup.UseMailkit(options =>
    {
        options.Server = "smtp.example.com";
        options.Port = 587;
        options.User = "user@example.com";
        options.Password = "securepassword";
        options.SocketOptions = SecureSocketOptions.StartTls;
    })
);

// Option 3: action with IServiceProvider
builder.Services.AddHeadlessEmails(setup =>
    setup.UseMailkit(
        (options, sp) =>
        {
            var cfg = sp.GetRequiredService<IConfiguration>();
            options.Server = cfg["Smtp:Server"]!;
            options.Port = int.Parse(cfg["Smtp:Port"]!);
        }
    )
);

// Named instance — each named SMTP sender owns an isolated connection pool (keyed "marketing"):
builder.Services.AddHeadlessEmails(setup =>
{
    setup.UseMailkit(builder.Configuration.GetSection("Smtp")); // default (required)
    setup.AddNamed("marketing", i => i.UseMailkit(builder.Configuration.GetSection("MarketingSmtp")));
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
| `Timeout` | `30s` | Connect/authenticate and per-read/write timeout |
| `MaxPoolSize` | `10` | Max pooled SMTP connections; `0` retains at most one (the pool always keeps a fast-path slot) |

## Dependencies

- `Headless.Emails.Core`
- `Headless.Hosting` (options binding + FluentValidation via `Configure<TOptions, TValidator>`)
- `MailKit`

## Side Effects

- Default: registers `IPooledObjectPolicy<SmtpClient>`, `ObjectPool<SmtpClient>`, and `IEmailSender` as unkeyed singletons
- Named (`AddNamed(name, i => i.UseMailkit(…))`): registers a keyed policy, pool, and `IEmailSender` plus named options under the instance name, so each named SMTP sender owns an isolated pool and never reads another instance's settings
