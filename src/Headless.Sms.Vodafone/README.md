# Framework.Sms.Vodafone

Vodafone SMS gateway implementation.

## Problem Solved

Provides SMS sending via Vodafone's enterprise messaging API with OAuth2 authentication.

## Key Features

- `VodafoneSmsSender` - ISmsSender implementation using Vodafone
- OAuth2 client credentials authentication
- Configurable sender name and base URL
- Regional endpoint support

## Installation

```bash
dotnet add package Framework.Sms.Vodafone
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddVodafoneSmsSender(options =>
{
    options.ClientId = "your-client-id";
    options.ClientSecret = "your-client-secret";
    options.SenderName = "MyApp";
    options.BaseUrl = "https://api.vodafone.com";
});
```

## Configuration

### appsettings.json

```json
{
  "Sms": {
    "Vodafone": {
      "ClientId": "your-client-id",
      "ClientSecret": "your-client-secret",
      "SenderName": "MyApp",
      "BaseUrl": "https://api.vodafone.com"
    }
  }
}
```

## Dependencies

- `Framework.Sms.Abstractions`

## Side Effects

- Registers `ISmsSender` as singleton
