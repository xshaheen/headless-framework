# Headless.Sms.Aws

AWS SNS SMS implementation of `ISmsSender`.

## Problem Solved

Provides SMS sending via Amazon Simple Notification Service (SNS), reusing existing AWS SDK credentials and IAM-based access control already present in AWS-hosted applications.

## Key Features

- `AwsSnsSmsSender` — `ISmsSender` implementation backed by AWS SNS.
- `SenderId` — alphanumeric sender ID displayed to recipients (support varies by country).
- `MaxPrice` — optional per-message USD price cap; SNS rejects sends that would exceed it.
- Accepts any AWS credential source: environment, instance metadata, `appsettings.json` via `AWSOptions`, or explicit `BasicAWSCredentials`.

## Installation

```bash
dotnet add package Headless.Sms.Aws
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Option 1: bind from appsettings (recommended)
var awsOptions = builder.Configuration.GetAWSOptions();
builder.Services.AddHeadlessSms(setup => setup.UseAwsSns(
    builder.Configuration.GetSection("Sms:Aws"),
    awsOptions
));

// Option 2: configure in code
builder.Services.AddHeadlessSms(setup => setup.UseAwsSns(options =>
{
    options.SenderId = "MyApp";
    // options.MaxPrice = 0.05m; // optional per-message USD cap
}, awsOptions));
```

## Configuration

### appsettings.json

```json
{
  "Sms": {
    "Aws": {
      "SenderId": "MyApp",
      "MaxPrice": null
    }
  },
  "AWS": {
    "Region": "us-east-1"
  }
}
```

### Options

| Option | Type | Required | Description |
|---|---|---|---|
| `SenderId` | `string` | Yes | Alphanumeric sender ID shown to recipients (country support varies). |
| `MaxPrice` | `decimal?` | No | Maximum USD price per message. SNS rejects if exceeded. |

## Dependencies

- `Headless.Sms.Abstractions`
- `AWSSDK.SimpleNotificationService`
- `AWSSDK.Extensions.NETCore.Setup`

## Side Effects

- Registers `IAmazonSimpleNotificationService` if not already registered.
- Registers `ISmsSender` as singleton (`AwsSnsSmsSender`).
