# Headless.Security

Default string encryption and hashing implementations plus DI registration helpers.

## Problem Solved

Provides the concrete implementations and DI registration helpers for `Headless.Security.Abstractions`, without coupling these concerns to `Headless.Core` or `Headless.Api`.

## Key Features

- `StringEncryptionService`
- `StringHashService`
- `AddStringEncryptionService(IConfiguration)`
- `AddStringEncryptionService(Action<StringEncryptionOptions>)`
- `AddStringEncryptionService(Action<StringEncryptionOptions, IServiceProvider>)`
- `AddStringHashService(IConfiguration)`
- `AddStringHashService(Action<StringHashOptions>)`
- `AddStringHashService(Action<StringHashOptions, IServiceProvider>)`

## Installation

```bash
dotnet add package Headless.Security
```

## Quick Start

```csharp
using System.Security.Cryptography;
using Headless.Security;

builder.Services.AddStringEncryptionService(options =>
{
    options.DefaultPassPhrase = "YourPassPhrase123";
    options.InitVectorBytes = "YourInitVector16"u8.ToArray();
    options.DefaultSalt = "YourSalt"u8.ToArray();
});

builder.Services.AddStringHashService(options =>
{
    options.Iterations = 600_000;
    options.Size = 128;
    options.Algorithm = HashAlgorithmName.SHA256;
    options.DefaultSalt = "DefaultSalt";
});
```

## Configuration

Use the `IConfiguration`, `Action<TOptions>`, or `Action<TOptions, IServiceProvider>` overload that fits the caller. Both services validate options through FluentValidation when configured. `IStringHashService.Create(...)` accepts an optional salt and falls back to `StringHashOptions.DefaultSalt` when present, or an empty salt when no default is configured.

## Dependencies

- `Headless.Security.Abstractions`
- `Headless.Checks`
- `Headless.Hosting`

## Side Effects

- Registers `IStringEncryptionService`
- Registers `IStringHashService`
- Registers validated options for hashing and encryption
