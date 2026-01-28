# Framework.Sms.Dev

Development SMS implementations for testing.

## Problem Solved

Provides development-only SMS senders that either log messages to a file or silently discard them, enabling SMS workflow testing without sending actual messages.

## Key Features

- `DevSmsSender` - Writes SMS to a file for inspection
- `NoopSmsSender` - Silently discards all messages
- No external dependencies or API calls

## Installation

```bash
dotnet add package Framework.Sms.Dev
```

## Quick Start

### File-based Logging

```csharp
var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddDevSmsSender("sms-log.txt");
}
```

### No-op (Silent)

```csharp
var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddNoopSmsSender();
}
```

## Configuration

No configuration required.

## Dependencies

- `Framework.Sms.Abstractions`

## Side Effects

- Registers `ISmsSender` as singleton
- `DevSmsSender` writes to the specified file path
