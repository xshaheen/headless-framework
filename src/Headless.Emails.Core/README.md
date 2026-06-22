# Headless.Emails.Core

Core utilities and MimeKit integration for email implementations.

## Problem Solved

Provides shared conversion logic to bridge the framework email contracts with MimeKit, eliminating duplication across the Aws and Mailkit provider implementations.

## Key Features

- `EmailToMimeMessageConverter.ConvertToMimeMessageAsync()` — converts `SendSingleEmailRequest` to a MimeKit `MimeMessage` (internal extension; exposed to the Aws/Mailkit providers via `InternalsVisibleTo`)
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
