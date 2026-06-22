# Headless.Emails.Core

Setup builder, MimeKit integration, and shared utilities for email implementations.

## Problem Solved

Owns the unified email setup builder (`AddHeadlessEmails`) plus shared conversion logic that bridges the framework email contracts with MimeKit, eliminating duplication across the provider implementations.

## Key Features

- `AddHeadlessEmails(Action<HeadlessEmailsSetupBuilder>)` — the single provider-agnostic registration entry point, with an exactly-one-provider gate
- `HeadlessEmailsSetupBuilder` — receives `Use*` provider selections; `IEmailProviderOptionsExtension` — the hook each provider implements
- `EmailAttachmentContentType.Resolve(fileName)` — derives an attachment MIME type from its file name (MimeKit lookup, `application/octet-stream` fallback)
- `EmailToMimMessageConverter.ConvertToMimeMessageAsync()` — converts `SendSingleEmailRequest` to a MimeKit `MimeMessage` (extension method on `SendSingleEmailRequest`)
- `EmailRequestAddress.MapToMailboxAddress()` — maps to MimeKit `MailboxAddress`

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

// Shared helpers used internally by providers — not called from application code:
var contentType = EmailAttachmentContentType.Resolve("invoice.pdf"); // "application/pdf"
using var mimeMessage = await request.ConvertToMimeMessageAsync(cancellationToken);
```

## Configuration

No configuration required.

## Dependencies

- `Headless.Emails.Abstractions`
- `Headless.Hosting`
- `MailKit`

## Side Effects

None directly. `AddHeadlessEmails` registers a provider-registration marker and delegates all `IEmailSender` wiring to the selected provider's extension.
