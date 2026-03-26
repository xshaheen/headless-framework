# Headless.Security.Abstractions

Defines security contracts and option models for string encryption and hashing.

## Problem Solved

Provides provider-agnostic contracts and validated option types for string encryption and hashing without coupling consumers to a concrete implementation or DI registration path.

## Key Features

- `IStringEncryptionService`
- `IStringHashService`
- `StringEncryptionOptions` / `StringEncryptionOptionsValidator`
- `StringHashOptions` / `StringHashOptionsValidator`

`IStringHashService.Create(...)` accepts an optional salt. Configure `StringHashOptions.DefaultSalt` when you want a default salt applied automatically; leave it unset when no default salt is needed.

## Installation

```bash
dotnet add package Headless.Security.Abstractions
```

## Usage

```csharp
public sealed class SecureSettingService(IStringEncryptionService encryptionService)
{
    public string Protect(string value)
    {
        return encryptionService.Encrypt(value)!;
    }
}
```

## Configuration

No configuration required. This is an abstractions-only package.

## Dependencies

- `FluentValidation`

## Side Effects

None. This is an abstractions package.
