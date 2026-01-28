# Framework.Sms.Infobip

Infobip SMS gateway implementation.

## Problem Solved

Provides SMS sending via Infobip's global messaging platform with comprehensive delivery reporting.

## Key Features

- `InfobipSmsSender` - ISmsSender implementation using Infobip
- API key authentication
- Configurable base URL for regional endpoints
- Comprehensive delivery status reporting

## Installation

```bash
dotnet add package Framework.Sms.Infobip
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfobipSmsSender(options =>
{
    options.ApiKey = "your-api-key";
    options.BaseUrl = "https://api.infobip.com";
    options.SenderName = "MyApp";
});
```

## Configuration

### appsettings.json

```json
{
  "Sms": {
    "Infobip": {
      "ApiKey": "your-api-key",
      "BaseUrl": "https://api.infobip.com",
      "SenderName": "MyApp"
    }
  }
}
```

## Dependencies

- `Framework.Sms.Abstractions`

## Side Effects

- Registers `ISmsSender` as singleton
