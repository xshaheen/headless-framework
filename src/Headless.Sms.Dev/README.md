# Headless.Sms.Dev

Development SMS implementations that avoid real sends.

## Problem Solved

Provides no-op and file-logging SMS senders for development and test environments, enabling full SMS workflow testing without requiring vendor credentials or sending actual messages.

## Key Features

- `DevSmsSender` — implements `ISmsSender` and `IBulkSmsSender`; appends formatted SMS details to a local file for inspection.
- `NoopSmsSender` — implements `ISmsSender` and `IBulkSmsSender`; silently discards all messages and returns a success response.
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
    builder.Services.AddHeadlessSms(setup => setup.UseDevelopment("sms-log.txt"));
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

### As a Named Instance

```csharp
// Keyed ISmsSender "audit" writes to a file while the default sends for real:
builder.Services.AddHeadlessSms(setup =>
{
    setup.UseTwilio(builder.Configuration.GetSection("Sms:Twilio")); // default (required)
    setup.AddNamed("audit", i => i.UseDevelopment("audit-sms.txt"));
});
```

## Configuration

No configuration required.

## Dependencies

- `Headless.Sms.Core`

## Side Effects

- Default: registers `ISmsSender` and `IBulkSmsSender` (the bulk sender forwards to the same instance) as unkeyed singletons. `DevSmsSender` appends to the specified file on each send; `NoopSmsSender` discards silently.
- Named (`AddNamed(name, i => i.UseDevelopment(path))` / `i.UseNoop()`): registers the same sender as a keyed `ISmsSender` (and keyed `IBulkSmsSender`) under the instance name.
