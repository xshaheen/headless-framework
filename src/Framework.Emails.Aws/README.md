# Framework.Emails.Aws

AWS SES (Simple Email Service) v2 implementation of the email sending abstraction.

## Problem Solved

Provides email sending via AWS SES using the unified `IEmailSender` abstraction, ideal for production deployments on AWS.

## Key Features

- Full `IEmailSender` implementation using AWS SES v2
- High deliverability and scalability
- AWS SDK configuration integration
- Attachment support

## Installation

```bash
dotnet add package Framework.Emails.Aws
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Using configuration
var awsOptions = builder.Configuration.GetAWSOptions();
builder.Services.AddAwsSesEmailSender(awsOptions);

// Or using explicit options
builder.Services.AddAwsSesEmailSender(new AWSOptions
{
    Region = RegionEndpoint.USEast1,
    Credentials = new BasicAWSCredentials("accessKey", "secretKey")
});
```

## Configuration

### appsettings.json

```json
{
  "AWS": {
    "Region": "us-east-1"
  }
}
```

## Dependencies

- `Framework.Emails.Core`
- `AWSSDK.SimpleEmailV2`
- `AWSSDK.Extensions.NETCore.Setup`

## Side Effects

- Registers `IAmazonSimpleEmailServiceV2` if not already registered
- Registers `IEmailSender` as singleton
