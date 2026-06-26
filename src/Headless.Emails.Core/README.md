# Headless.Emails.Core

Setup builder, MimeKit integration, and shared utilities for email implementations.

## Problem Solved

Owns the unified email setup builder (`AddHeadlessEmails`) plus shared conversion logic that bridges the framework email contracts with MimeKit, eliminating duplication across the provider implementations.

## Key Features

- `AddHeadlessEmails(Action<HeadlessEmailsSetupBuilder>)` — the single provider-agnostic registration entry point, with an exactly-one-default-provider gate
- `HeadlessEmailsSetupBuilder` — receives the default `Use*` selection plus `AddNamed(name, …)` named instances; `HeadlessEmailInstanceBuilder` — the per-named-instance builder that providers extend with their `Use*` members
- `IEmailSenderProvider` — registered automatically by the gate (keyed-service-backed); resolves named senders by name
- `EmailAttachmentContentType.Resolve(fileName)` — derives an attachment MIME type from its file name (MimeKit lookup, `application/octet-stream` fallback)
- `EmailToMimeMessageConverter.ConvertToMimeMessageAsync()` — converts `SendSingleEmailRequest` to a MimeKit `MimeMessage` (internal extension; exposed to the Aws/Mailkit providers via `InternalsVisibleTo`)
- `MapToMailboxAddress()` — maps an `EmailRequestAddress` to a MimeKit `MailboxAddress` (internal)
- Full address mapping (From, To, Cc, Bcc), body building (text + HTML via `BodyBuilder`), and attachment streaming

## Design Notes

The builder carries no shared, cross-provider feature options — it is provider-selection-only; each provider binds its own options inside its `Use*` member. The gate is **per-slot**: it requires exactly one default provider (rejecting zero or multiple) while allowing unbounded uniquely-named instances, and rejects a repeated `AddHeadlessEmails` on the same `IServiceCollection`. Providers contribute deferred `Action<IServiceCollection>` registrations (`RegisterDefaultProvider` for the default, `instance.RegisterProvider` for a named instance) rather than implementing a provider interface, keeping the default and named paths symmetric. The MimeKit converter returns a `MimeMessage` that callers dispose; the implementation disposes the message if an exception occurs during construction, preventing a resource leak.

## Installation

```bash
dotnet add package Headless.Emails.Core
```

## Quick Start

```csharp
// Provider-agnostic registration entry point (a provider package supplies the Use* member):
builder.Services.AddHeadlessEmails(setup =>
{
    setup.UseNoop(); // default (required)
    setup.AddNamed("marketing", i => i.UseNoop()); // optional named sender, keyed "marketing"
});

// Resolve a named sender:
var marketing = serviceProvider.GetRequiredService<IEmailSenderProvider>().GetSender("marketing");

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

`AddHeadlessEmails` registers a provider-registration marker and `IEmailSenderProvider` (keyed-service-backed), then runs the default provider's wiring (the unkeyed `IEmailSender`) followed by each named instance's wiring (keyed under the instance name). The marker enforces the single-call rule.
