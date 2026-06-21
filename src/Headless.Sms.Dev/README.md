# Headless.Sms.Dev

Development SMS implementations that avoid real sends.

## Problem Solved

Provides no-op and file-logging SMS senders for development and test environments, enabling full SMS workflow testing without requiring vendor credentials or sending actual messages.

## Key Features

- `DevSmsSender` — appends formatted SMS details to a local file for inspection.
- `NoopSmsSender` — silently discards all messages and returns `SendSingleSmsResponse.Succeeded()`.
- No external dependencies, no HTTP calls, no API credentials needed.

## Installation

```bash
dotnet add package Headless.Sms.Dev
```

## Quick Start

### File-based Logging

```csharp
var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddHeadlessSms(setup => setup.UseDev("sms-log.txt"));
}
```

### No-op (Silent)

```csharp
var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddHeadlessSms(setup => setup.UseNoop());
}
```

## Configuration

No configuration required.

## Dependencies

- `Headless.Sms.Abstractions`

## Side Effects

- Registers `ISmsSender` as singleton
- `DevSmsSender` writes to the specified file path
