# Headless.Sms.Aws

AWS SNS SMS implementation of `ISmsSender`.

## Problem Solved

Provides SMS sending via Amazon Simple Notification Service (SNS), reusing existing AWS SDK credentials and IAM-based access control already present in AWS-hosted applications.

## Key Features

- `AwsSnsSmsSender` — `ISmsSender` implementation backed by AWS SNS. Single recipient per send; does not implement `IBulkSmsSender` (SNS publishes to one phone number per call).
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
builder.Services.AddHeadlessSms(setup => setup.UseAwsSns(builder.Configuration.GetSection("Sms:Aws"), awsOptions));

// Option 2: configure in code
builder.Services.AddHeadlessSms(setup =>
    setup.UseAwsSns(
        options =>
        {
            options.SenderId = "MyApp";
            // options.MaxPrice = 0.05m; // optional per-message USD cap
        },
        awsOptions
    )
);

// Named instance (keyed ISmsSender + keyed IAmazonSimpleNotificationService, resolvable via ISmsSenderProvider):
builder.Services.AddHeadlessSms(setup =>
{
    setup.UseNoop(); // default (required)
    setup.AddNamed("sns", i => i.UseAwsSns(builder.Configuration.GetSection("Sms:Aws"), awsOptions)); // keyed "sns"
});
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

- `Headless.Sms.Core`
- `AWSSDK.SimpleNotificationService`
- `AWSSDK.Extensions.NETCore.Setup`

## Side Effects

- Default: registers `IAmazonSimpleNotificationService` via `TryAddAWSService` (no-op if already registered) and `ISmsSender` (`AwsSnsSmsSender`) as an unkeyed singleton. No `IBulkSmsSender` — SNS publishes to one recipient per call.
- Named (`AddNamed(name, i => i.UseAwsSns(…))`): registers a keyed `IAmazonSimpleNotificationService` (built via `AWSOptions.CreateServiceClient<T>` from the supplied options, the ambient `AWSOptions` in DI, `IConfiguration` `AWS:*` via `GetAWSOptions()`, or SDK defaults — mirroring `TryAddAWSService(null)`, which has no keyed overload) and a keyed `ISmsSender`, both under the instance name.
