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

None.
