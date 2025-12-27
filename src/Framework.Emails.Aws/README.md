# Framework.Emails.Aws

AWS SES (Simple Email Service) implementation of the generic email abstraction. This package enables sending emails via Amazon SES V2 using the standard `IEmailSender` interface.

## Installation

Ensure you have the `AWSSDK.SimpleEmailV2` and `AWSSDK.Extensions.NETCore.Setup` packages configured.

## Usage

### Registration

Register the AWS SES email sender in your dependency injection container:

```csharp
// Using configuration
var awsOptions = builder.Configuration.GetAWSOptions();
services.AddAwsSesEmailSender(awsOptions);

// Or using explicit options
var awsOptions = new AWSOptions
{
    Region = RegionEndpoint.USEast1,
    Credentials = new BasicAWSCredentials("accessKey", "secretKey")
};
services.AddAwsSesEmailSender(awsOptions);
```

### Configuration

The generic `IEmailSender` will now resolve to `AwsSesEmailSender`, allowing you to inject it into your services without binding them to AWS specifically.
