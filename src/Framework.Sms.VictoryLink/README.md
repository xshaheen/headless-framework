# Framework.Sms.VictoryLink

VictoryLink SMS gateway implementation.

## Problem Solved

Provides SMS sending via VictoryLink API, a regional SMS provider serving the Middle East market.

## Key Features

- `VictoryLinkSmsSender` - ISmsSender implementation using VictoryLink
- Username/password authentication
- Configurable sender name
- Response code handling

## Installation

```bash
dotnet add package Framework.Sms.VictoryLink
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddVictoryLinkSmsSender(options =>
{
    options.Username = "your-username";
    options.Password = "your-password";
    options.SenderName = "MyApp";
});
```

## Configuration

### appsettings.json

```json
{
  "Sms": {
    "VictoryLink": {
      "Username": "your-username",
      "Password": "your-password",
      "SenderName": "MyApp"
    }
  }
}
```

## Dependencies

- `Framework.Sms.Abstractions`

## Side Effects

- Registers `ISmsSender` as singleton
