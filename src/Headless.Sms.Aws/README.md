# Framework.Sms.Aws

AWS SNS SMS implementation.

## Problem Solved

Provides SMS sending via Amazon Simple Notification Service (SNS), supporting transactional and promotional message types with AWS SDK integration.

## Key Features

- `AwsSnsSmsSender` - ISmsSender implementation using AWS SNS
- Configurable sender ID
- Message type selection (transactional/promotional)
- AWS SDK integration with flexible credentials

## Installation

```bash
dotnet add package Framework.Sms.Aws
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

var awsOptions = builder.Configuration.GetAWSOptions();
builder.Services.AddAwsSnsSmsSender(
    builder.Configuration.GetSection("Sms:Aws"),
    awsOptions
);
```

## Configuration

### appsettings.json

```json
{
  "Sms": {
    "Aws": {
      "SenderId": "MyApp"
    }
  },
  "AWS": {
    "Region": "us-east-1"
  }
}
```

### Code Configuration

```csharp
builder.Services.AddAwsSnsSmsSender(options =>
{
    options.SenderId = "MyApp";
});
```

## Dependencies

- `Framework.Sms.Abstractions`
- `AWSSDK.SimpleNotificationService`
- `AWSSDK.Extensions.NETCore.Setup`

## Side Effects

- Registers `ISmsSender` as singleton
- Registers `IAmazonSimpleNotificationService` if not already registered
