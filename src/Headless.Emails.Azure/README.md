# Headless.Emails.Azure

Azure Communication Services (ACS) Email implementation of the email sending abstraction.

## Problem Solved

Provides email sending via Azure Communication Services using the unified `IEmailSender` abstraction — the intended cloud-email backend for Azure-hosted consumers, with managed-identity support.

## Key Features

- Full `IEmailSender` implementation over `Azure.Communication.Email`
- Three authentication modes: connection string, endpoint + access key, and endpoint + managed-identity `TokenCredential`
- Maps `SendSingleEmailRequest` (From, To/Cc/Bcc, Subject, HTML + plain-text bodies, attachments) to an ACS `EmailMessage`
- Attachment content type derived from the file name via `EmailAttachmentContentType.Resolve()` (`application/octet-stream` fallback)
- Both a thrown `RequestFailedException` and a completed-but-failed terminal status map to `SendSingleEmailResponse.Failed(...)`; a `Succeeded` status carries the ACS operation id as `ProviderMessageId`
- Non-PII logging on failure (operation id, status, error code — no recipient/sender addresses)

## Design Notes

The send uses `EmailClient.SendAsync(WaitUntil.Completed, …)`, so the call blocks until ACS reaches a terminal state — matching the contract's "accepted for delivery" success semantics. ACS can complete a long-running send with a non-`Succeeded` status **without throwing**, so the sender inspects `operation.Value.Status` and treats any terminal non-`Succeeded` state as a failure (an exception-only check would report rejected mail as delivered). Only `Succeeded` returns `Succeeded()`; unrelated exceptions (cancellation, argument errors) propagate.

The package depends on `Azure.Core` (not `Azure.Identity`): supply your own `DefaultAzureCredential` through the delegate overload to keep the dependency surface narrow. The `IConfiguration` overload binds only the connection-string and endpoint + access-key modes. ACS's `senderAddress` is a bare string, so the sender's display name is not honored. No custom retry loop is added — `Azure.Core`'s pipeline already retries 429/5xx honoring `Retry-After`. The sender domain must be verified and linked in the Communication Services resource; managed-domain send limits are low (5/min; custom domains 30/min).

## Installation

```bash
dotnet add package Headless.Emails.Azure
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Option 1: connection string or endpoint + access key, bound from configuration
builder.Services.AddHeadlessEmails(setup => setup.UseAzure(builder.Configuration.GetSection("AzureEmail")));

// Option 2: endpoint + access key (delegate)
builder.Services.AddHeadlessEmails(setup =>
    setup.UseAzure(options =>
    {
        options.Endpoint = new Uri("https://my-resource.communication.azure.com/");
        options.AccessKey = builder.Configuration["AzureEmail:AccessKey"]!;
    })
);

// Option 3: managed identity (TokenCredential — delegate only; requires the Azure.Identity package)
builder.Services.AddHeadlessEmails(setup =>
    setup.UseAzure(options =>
    {
        options.Endpoint = new Uri("https://my-resource.communication.azure.com/");
        options.TokenCredential = new DefaultAzureCredential();
    })
);

// Named instance (keyed IEmailSender + keyed EmailClient, resolvable via IEmailSenderProvider):
builder.Services.AddHeadlessEmails(setup =>
{
    setup.UseNoop(); // default (optional)
    setup.AddNamed("alerts", i => i.UseAzure(builder.Configuration.GetSection("AlertsEmail")));
});
```

## Configuration

```json
{
    "AzureEmail": {
        "ConnectionString": "endpoint=https://my-resource.communication.azure.com/;accesskey=<key>"
    }
}
```

`AzureCommunicationEmailOptions` properties — exactly one auth mode must be configured:

| Property | Type | Description |
|---|---|---|
| `ConnectionString` | `string?` | Resource connection string (connection-string mode) |
| `Endpoint` | `Uri?` | Resource endpoint; pair with `AccessKey` or `TokenCredential` |
| `AccessKey` | `string?` | Resource access key (access-key mode) |
| `TokenCredential` | `TokenCredential?` | Managed-identity credential (delegate overload only — not bindable from configuration) |

## Dependencies

- `Headless.Emails.Abstractions`
- `Headless.Emails.Core`
- `Azure.Communication.Email`

## Side Effects

- Default: binds and validates `AzureCommunicationEmailOptions` (exactly one auth mode required), and registers `EmailClient` and `IEmailSender` as unkeyed singletons
- Named (`AddNamed(name, i => i.UseAzure(…))`): binds named options and registers a keyed `EmailClient` (constructed from the named auth mode) and a keyed `IEmailSender`, both under the instance name
