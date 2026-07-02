# Headless.Emails.Abstractions

Defines the unified interface for sending emails across different providers (Azure Communication Services, AWS SES, SMTP/MailKit, development).

## Problem Solved

Provides a provider-agnostic email sending API for switching email providers without changing application code.

## Key Features

- `IEmailSender` — core interface with a single `SendAsync(SendSingleEmailRequest, CancellationToken)` method returning `ValueTask<SendSingleEmailResponse>`
- `IEmailSenderProvider` — resolves named senders by name: `GetSender(name)` (throws when unregistered) and `GetSenderOrNull(name)`, plus `RegisteredNames` (`IReadOnlySet<string>`) listing the registered named instances (the default is excluded) so an externally supplied name can be validated before resolving. Backed by the container's keyed `IEmailSender` registrations
- `SendSingleEmailRequest` — immutable record with required `From`, `Destination`, `Subject`; optional `MessageHtml`, `MessageText`, `Attachments`. `EnsureHasBody()` throws `InvalidOperationException` when neither body is set (called by every sender)
- `EmailRequestAddress` — sealed record wrapping email address + optional display name; supports implicit conversion from `string`
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
        var response = await emailSender
            .SendAsync(
                new SendSingleEmailRequest
                {
                    From = "noreply@example.com",
                    Destination = new EmailRequestDestination { ToAddresses = [new EmailRequestAddress(to)] },
                    Subject = "Welcome!",
                    MessageHtml = $"<h1>Hello {name}!</h1>",
                    MessageText = $"Hello {name}!",
                },
                ct
            )
            .ConfigureAwait(false);

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
