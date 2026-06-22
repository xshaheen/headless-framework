# Headless.Emails.Core

Setup builder, MimeKit integration, and shared utilities for email implementations.

## Problem Solved

Owns the unified email setup builder (`AddHeadlessEmails`) plus shared conversion logic that bridges the framework email contracts with MimeKit, eliminating duplication across the provider implementations.

## Key Features

- `AddHeadlessEmails(Action<HeadlessEmailsSetupBuilder>)` — the single provider-agnostic registration entry point, with an exactly-one-provider gate
- `HeadlessEmailsSetupBuilder` — receives `Use*` provider selections; `IEmailProviderOptionsExtension` — the hook each provider implements
- `EmailAttachmentContentType.Resolve(fileName)` — derives an attachment MIME type from its file name (MimeKit lookup, `application/octet-stream` fallback)
- `EmailToMimeMessageConverter.ConvertToMimeMessageAsync()` — converts `SendSingleEmailRequest` to a MimeKit `MimeMessage` (internal extension; exposed to the Aws/Mailkit providers via `InternalsVisibleTo`)
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
