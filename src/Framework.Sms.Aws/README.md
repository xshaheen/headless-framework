# Framework.Sms.Aws

This package allows sending SMS messages using AWS Simple Notification Service (SNS).

## Features

-   **AwsSnsSmsSender**: Implements `ISmsSender` using AWS SNS.
-   **Configuration**: `AwsSnsSmsOptions` for region, credentials (if not using default chain), and default settings.
-   **Setup**: `AddAwsSnsExtensions` for quick integration.

## Usage

### Configuration

Configure your AWS credentials and region. `AwsSnsSmsOptions` may include topic ARNs or other SNS specific settings.

### Registration

```csharp
services.AddAwsSnsSms(configuration);
```

### Sending

The `ISmsSender.SendAsync` calls are adapted to AWS SNS Publish calls.
