# Headless.Emails.Core

Core utilities and MimeKit integration for email implementations.

## Problem Solved

Provides shared conversion logic to bridge the framework email contracts with MimeKit, eliminating duplication across the Aws and Mailkit provider implementations.

## Key Features

- `EmailToMimMessageConverter.ConvertToMimeMessageAsync()` — converts `SendSingleEmailRequest` to a MimeKit `MimeMessage` (extension method on `SendSingleEmailRequest`)
- `EmailRequestAddress.MapToMailboxAddress()` — maps to MimeKit `MailboxAddress`
- Full address mapping (From, To, Cc, Bcc), body building (text + HTML via `BodyBuilder`), and attachment streaming

## Design Notes

This package is consumed transitively by `Headless.Emails.Aws` and `Headless.Emails.Mailkit` — application code does not reference it directly. The converter returns a `MimeMessage` that callers are responsible for disposing; the implementation disposes the message if an exception occurs during construction, preventing a resource leak.

## Installation

```bash
dotnet add package Headless.Emails.Core
```

## Quick Start

```csharp
// Used internally by providers — not called from application code.
// Shown for provider implementors:
using var mimeMessage = await request.ConvertToMimeMessageAsync(cancellationToken);
```

## Configuration

No configuration required.

## Dependencies

- `Headless.Emails.Abstractions`
- `MimeKit`

## Side Effects

None. This is a utility package with no DI registrations.
