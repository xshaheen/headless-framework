# Headless.Sms.Cequens

Cequens SMS gateway implementation.

## Problem Solved

Provides SMS sending via Cequens API, a regional SMS provider popular in the Middle East and North Africa with token-based authentication.

## Key Features

- `CequensSmsSender` - ISmsSender implementation using Cequens
- JWT token-based authentication with auto-refresh
- Configurable sender name
- Batch SMS support

## Installation

```bash
dotnet add package Headless.Sms.Cequens
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCequensSmsSender(options =>
{
    options.ApiKey = "your-api-key";
    options.UserName = "your-username";
    options.SenderName = "MyApp";
});
```

## Configuration

### appsettings.json

```json
{
  "Sms": {
    "Cequens": {
      "ApiKey": "your-api-key",
      "UserName": "your-username",
      "SenderName": "MyApp"
    }
  }
}
```

## Dependencies

- `Headless.Sms.Abstractions`

## Side Effects

- Registers `ISmsSender` as singleton
- Registers `HttpClient` for Cequens API
