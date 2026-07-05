# Headless.Emails.Dev

Development email implementations for local testing and debugging.

## Problem Solved

Provides safe email implementations for development and testing that do not send real emails, preventing accidental sends and enabling easy inspection of email content locally.

## Key Features

- `DevEmailSender` — writes full email content to a local file; appends with a `--------------------` separator per message; prefers `MessageText` over `MessageHtml` for readability. Writes are serialized so concurrent sends do not interleave, and a body-less request throws `InvalidOperationException` (same `EnsureHasBody()` guard as the real providers)
- `NoopEmailSender` — silently discards all emails, always returns `Succeeded()` (never validates — the explicit "disable email" sender)
- No network calls, no external dependencies beyond the abstractions and core packages

## Installation

```bash
dotnet add package Headless.Emails.Dev
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    // Write emails to a local file for inspection
    builder.Services.AddHeadlessEmails(setup => setup.UseDevelopment("path/to/emails.txt"));

    // Or discard silently (useful in automated tests)
    // builder.Services.AddHeadlessEmails(setup => setup.UseNoop());
}

// As a named instance alongside a real default sender (keyed IEmailSender "audit"):
builder.Services.AddHeadlessEmails(setup =>
{
    setup.UseAwsSes(awsOptions); // default (optional)
    setup.AddNamed("audit", i => i.UseDevelopment("audit-emails.txt"));
});
```

Example output written to the file:

```text
From: sender@example.com
To: recipient@example.com
Subject: Test Email
Message:

Hello World!
--------------------
```

## Configuration

`UseDevelopment(string filePath)` — the file path is the only parameter. The file is created if it does not exist; emails are appended.

`UseNoop()` — no parameters. Useful in test projects where you want `IEmailSender` resolved but do not want file I/O.

## Dependencies

- `Headless.Emails.Abstractions`
- `Headless.Emails.Core`

## Side Effects

- `UseDevelopment` registers `IEmailSender` as singleton (instance of `DevEmailSender`); appends to the specified file on each `SendAsync` call
- `UseNoop` registers `IEmailSender` as singleton (instance of `NoopEmailSender`); no I/O
- Named (`AddNamed(name, i => i.UseDevelopment(path))` / `i.UseNoop()`): registers the same sender as a keyed `IEmailSender` under the instance name
