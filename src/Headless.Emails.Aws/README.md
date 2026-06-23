# Headless.Emails.Aws

AWS SES (Simple Email Service) v2 implementation of the email sending abstraction.

## Problem Solved

Provides email sending via AWS SES v2 using the unified `IEmailSender` abstraction, ideal for production deployments on AWS.

## Key Features

- Full `IEmailSender` implementation using AWS SES v2 (`AWSSDK.SimpleEmailV2`)
- Simple sends (no attachments) use the SES structured API path — no MIME serialization
- Attachment sends serialize to raw MIME and use the SES raw message path
- AWS SDK configuration integration (`AWSOptions` from `AWSSDK.Extensions.NETCore.Setup`)
- SES-specific exceptions (`MessageRejectedException`, `BadRequestException`, `NotFoundException`, `AccountSuspendedException`, `MailFromDomainNotVerifiedException`, `LimitExceededException`, `TooManyRequestsException`, `SendingPausedException`) propagate — not wrapped in `Failed()`
- BCC is delivered via the SES envelope (`Destination`) and the `Bcc` header is hidden from the serialized MIME on the raw (attachment) path, so BCC recipients are never disclosed
- Non-PII logging on non-success HTTP responses (status code, request ID, message ID — no recipient/sender addresses)

## Installation

```bash
dotnet add package Headless.Emails.Aws
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Option 1: from configuration (reads AWS:Region, credentials from environment/profile)
var awsOptions = builder.Configuration.GetAWSOptions();
builder.Services.AddHeadlessEmails(setup => setup.UseAwsSes(awsOptions));

// Option 2: explicit credentials
builder.Services.AddHeadlessEmails(setup => setup.UseAwsSes(new AWSOptions
{
    Region = RegionEndpoint.USEast1,
    Credentials = new BasicAWSCredentials("accessKey", "secretKey"),
}));

// Option 3: use default AWSOptions already registered in DI (pass null)
builder.Services.AddHeadlessEmails(setup => setup.UseAwsSes(null));

// Named instance (keyed IEmailSender, resolvable via IEmailSenderProvider):
builder.Services.AddHeadlessEmails(setup =>
{
    setup.UseNoop();                                       // default (required)
    setup.AddNamed("ses", i => i.UseAwsSes(awsOptions));   // keyed "ses"
});
```

## Configuration

```json
{
    "AWS": {
        "Region": "us-east-1"
    }
}
```

Credentials are resolved from the standard AWS credential chain (environment variables, `~/.aws/credentials`, IAM role) when not passed explicitly.

## Dependencies

- `Headless.Emails.Core`
- `Headless.Extensions`
- `AWSSDK.SimpleEmailV2`
- `AWSSDK.Extensions.NETCore.Setup`

## Side Effects

- Default: registers `IAmazonSimpleEmailServiceV2` via `TryAddAWSService` (no-op if already registered) and `IEmailSender` as an unkeyed singleton
- Named (`AddNamed(name, i => i.UseAwsSes(…))`): registers a keyed `IAmazonSimpleEmailServiceV2` (built from the supplied options, the ambient `AWSOptions` in DI, or `IConfiguration` (`AWS:*` via `GetAWSOptions()`) — mirroring `TryAddAWSService(null)` — using `AWSOptions.CreateServiceClient<T>`, since `TryAddAWSService` has no keyed overload) and a keyed `IEmailSender`, both under the instance name
