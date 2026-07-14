# Headless.Emails.Aws

AWS SES (Simple Email Service) v2 implementation of the email sending abstraction.

## Problem Solved

Provides email sending via AWS SES v2 using the unified `IEmailSender` abstraction, ideal for production deployments on AWS.

## Key Features

- Full `IEmailSender` implementation using AWS SES v2 (`AWSSDK.SimpleEmailV2`)
- Simple sends (no attachments) use the SES structured API path — no MIME serialization
- Attachment sends serialize to raw MIME and use the SES raw message path
- AWS SDK configuration integration (`AWSOptions` from `AWSSDK.Extensions.NETCore.Setup`)
- SES-specific exceptions (`MessageRejectedException`, `BadRequestException`, `NotFoundException`, `AccountSuspendedException`, `MailFromDomainNotVerifiedException`, `LimitExceededException`, `TooManyRequestsException`, `SendingPausedException`) — surfaced by the SDK as `AmazonSimpleEmailServiceV2Exception` — are returned as a failed `SendSingleEmailResponse` (the provider's raw error message is surfaced to the caller), never thrown; only `OperationCanceledException` and argument validation propagate. On success `ProviderMessageId` carries the SES message id
- BCC is delivered via the SES envelope (`Destination`) and the `Bcc` header is hidden from the serialized MIME on the raw (attachment) path, so BCC recipients are never disclosed
- Non-PII logging on failure (SES error code, HTTP status, request/message ID — never the exception message, which can embed a rejected address, and never recipient/sender addresses)
- Strongly-typed SES event tracking contracts (`Headless.Emails.Aws.Tracking`) for delivery/bounce/complaint/open/click/etc. notifications (see [Email Event Tracking](#email-event-tracking))

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
builder.Services.AddHeadlessEmails(setup =>
    setup.UseAwsSes(
        new AWSOptions
        {
            Region = RegionEndpoint.USEast1,
            Credentials = new BasicAWSCredentials("accessKey", "secretKey"),
        }
    )
);

// Option 3: use default AWSOptions already registered in DI (pass null)
builder.Services.AddHeadlessEmails(setup => setup.UseAwsSes(null));

// Named instance (keyed IEmailSender, resolvable via IEmailSenderProvider):
builder.Services.AddHeadlessEmails(setup =>
{
    setup.UseNoop(); // default (optional)
    setup.AddNamed("ses", i => i.UseAwsSes(awsOptions)); // keyed "ses"
});
```

## Email Event Tracking

SES can publish sending events (delivery, bounce, complaint, open, click, etc.) to an event
destination — typically an Amazon SNS topic — for a configuration set. The `Headless.Emails.Aws.Tracking`
namespace provides strongly-typed contracts for those JSON payloads so you can deserialize and react to
them. This package supplies only the typed contract; you own the transport (SNS HTTP/S subscription, SQS
poller, Lambda, etc.) and the handling logic.

Every property carries an explicit `[JsonPropertyName]` matching the SES field, so deserialization works
with any `JsonSerializerOptions` (no naming policy required). Unknown fields are tolerated via
`[JsonExtensionData]` on the container types.

```csharp
using System.Text.Json;
using Headless.Emails.Aws.Tracking;

// `body` is the SES event record JSON (the SNS message body).
var notification = JsonSerializer.Deserialize<EmailTrackingNotification>(body);

switch (notification?.EventType)
{
    case EmailEventTypes.Bounce:
        foreach (var recipient in notification.Bounce!.BouncedRecipients)
        {
            if (notification.Bounce.BounceType == BounceTypes.Permanent)
            {
                // Permanent (hard) bounce — suppress this address.
            }
        }
        break;

    case EmailEventTypes.Complaint:
        // notification.Complaint!.ComplainedRecipients
        break;

    case EmailEventTypes.Delivery:
        // notification.Delivery!.Recipients / Timestamp
        break;

    case EmailEventTypes.Open:
    case EmailEventTypes.Click:
        // notification.Open! / notification.Click!
        break;
}
```

The message correlation ID is `notification.Mail.MessageId` (the ID SES returns from a send).

Available types: `EmailTrackingNotification`, `MailDetails` (`EmailHeader`, `EmailCommonHeaders`),
`SesSendEvent`, `SesDeliveryEvent`, `SesDeliveryDelayEvent` (`DelayedRecipient`), `SesOpenEvent`, `SesClickEvent`,
`SesBounceEvent` (`BouncedRecipient`), `SesComplaintEvent` (`ComplainedRecipient`), `SesRejectEvent`,
`SesRenderingFailureEvent`; plus the `EmailEventTypes`, `BounceTypes`, `BounceSubTypes`, `DelayTypes`, and
`ComplaintFeedbackTypes` constant holders.

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
