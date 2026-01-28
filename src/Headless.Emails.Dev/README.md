# Headless.Emails.Dev

Development email implementations for local testing and debugging.

## Problem Solved

Provides safe email implementations for development/testing that don't send real emails, preventing accidental sends and enabling easy inspection of email content.

## Key Features

- `DevEmailSender` - Writes full email content to a local file
- `NoopEmailSender` - Discards emails silently
- No network calls required
- Easy inspection of email content

## Installation

```bash
dotnet add package Headless.Emails.Dev
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddDevEmailSender("path/to/emails.txt");
}
```

## Output Format

The `DevEmailSender` appends to the file with a separator:

```text
From: sender@example.com
To: recipient@example.com
Subject: Test Email
Message:
Hello World!
--------------------
```

## Configuration

### Options

```csharp
services.AddDevEmailSender("emails.txt"); // Path to output file
```

## Dependencies

- `Headless.Emails.Abstractions`
- `Headless.Hosting`

## Side Effects

- Registers `IEmailSender` as singleton
- Writes emails to specified file
