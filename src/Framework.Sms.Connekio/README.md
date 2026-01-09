# Framework.Sms.Connekio

Connekio SMS gateway implementation.

## Problem Solved

Provides SMS sending via Connekio API, supporting both single and batch SMS delivery with basic authentication.

## Key Features

- `ConnekioSmsSender` - ISmsSender implementation using Connekio
- Basic authentication (username:password:accountId)
- Single and batch SMS support
- Configurable sender name

## Installation

```bash
dotnet add package Framework.Sms.Connekio
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddConnekioSmsSender(options =>
{
    options.UserName = "your-username";
    options.Password = "your-password";
    options.AccountId = "your-account-id";
    options.Sender = "MyApp";
});
```

## Configuration

### appsettings.json

```json
{
  "Sms": {
    "Connekio": {
      "UserName": "your-username",
      "Password": "your-password",
      "AccountId": "your-account-id",
      "Sender": "MyApp"
    }
  }
}
```

## Dependencies

- `Framework.Sms.Abstractions`

## Side Effects

- Registers `ISmsSender` as singleton
- Registers `HttpClient` for Connekio API
