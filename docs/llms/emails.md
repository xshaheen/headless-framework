---
domain: Email
packages: Emails.Abstractions, Emails, Emails.Aws, Emails.Azure, Emails.Dev, Emails.Mailkit
---

# Email

> Provider-agnostic email sending through `IEmailSender`, with providers for Azure Communication Services, AWS SES, SMTP (MailKit), and development.

## Orientation

Install `Headless.Emails.Abstractions` and one provider package. Each provider package brings `Headless.Emails`, which owns `AddHeadlessEmails`.

| Need | Package | Call |
| --- | --- | --- |
| Local development, tests | `Headless.Emails.Dev` | `UseDevelopment()` captures each email in an in-memory inbox (`IEmailCaptureStore`), and optionally a file. `UseNoop()` discards it. |
| Azure | `Headless.Emails.Azure` | `UseAzure(...)` sends through Azure Communication Services (ACS) Email. |
| AWS | `Headless.Emails.Aws` | `UseAwsSes(awsOptions)` sends through AWS SES v2. |
| Any SMTP server | `Headless.Emails.Mailkit` | `UseMailkit(...)` sends over pooled SMTP connections. |

Register every sender in one `AddHeadlessEmails` call: an optional **default** sender, chosen by a `Use*` call on the setup builder, plus any number of **named** senders added with `setup.AddNamed(name, i => i.Use…())`. Inject `IEmailSender` for the default. Resolve a named sender with `[FromKeyedServices("name")] IEmailSender` or `IEmailSenderProvider.GetSender("name")`.

```csharp
builder.Services.AddHeadlessEmails(setup =>
{
    if (builder.Environment.IsDevelopment())
    {
        setup.UseDevelopment("emails.txt");
    }
    else
    {
        setup.UseMailkit(builder.Configuration.GetSection("Smtp"));
    }
});
```

**Verify:** start the host. A provider with invalid options (Mailkit, Azure) fails the start with `OptionsValidationException`. Under `RunHeadlessAsync`, `dotnet MyApp.dll check` prints `Host check failed: ...` and exits 1 for the same mistake. Validation proves the options are present, not that the credentials work: send one email and assert `response.Success`.

## Agent Rules

- Call `AddHeadlessEmails` once per service collection. A second call, or more than one default `Use*` call in the delegate, throws `InvalidOperationException` at registration. Zero defaults is allowed: the unkeyed `IEmailSender` is then not registered, and the host resolves senders by name only.
- The `Use*` calls are `UseAzure`, `UseAwsSes`, `UseMailkit`, `UseDevelopment`, and `UseNoop`. Each one is available on the setup builder (default) and on each named instance.
- Give each named sender a non-empty name that is unique in the call, and exactly one provider. A blank name throws `ArgumentException`. A duplicate name, or zero or several providers in one named instance, throws `InvalidOperationException`.
- Resolve a named sender with `IEmailSenderProvider.GetSender(name)`, which throws `InvalidOperationException` for an unregistered name, or `GetSenderOrNull(name)`, which returns `null`. To validate a name from outside input, check `IEmailSenderProvider.RegisteredNames` first. `IEmailSenderProvider` never returns the default sender, and `RegisteredNames` does not list it.
- Use `UseDevelopment` or `UseNoop` in development and in tests, gated on `builder.Environment.IsDevelopment()`, so no real email leaves the machine.
- Read what a development sender captured through `IEmailCaptureStore` (`Query`, `Get`, `WriteEmlAsync`), not by parsing its file. Follow new emails with `WaitForChangeAsync`, not by polling `Query`.
- Set both `From` and `Destination` on `SendSingleEmailRequest`; they are `required`. Set `MessageHtml`, `MessageText`, or both. A request with neither body throws `InvalidOperationException` from every sender except `UseNoop`, which discards everything without validation.
- Check `response.Success` after every `SendAsync`. A provider or transport failure returns a failed `SendSingleEmailResponse` with a non-null `FailureError`. Only cancellation by your token and argument validation throw.
- Keep an attachment's `File` memory unchanged until `SendAsync` returns. The providers read it during the send, without a copy.
- With `Headless.Emails.Azure`, verify and link the sender domain in the Communication Services resource. An unverified sender returns a failed response at send time. Nothing checks it earlier.

---

## Core Concepts

### Request and response

`SendSingleEmailRequest` is an immutable record:

| Property | Type | Notes |
| --- | --- | --- |
| `From` | `EmailRequestAddress` | Required. A `string` converts implicitly. |
| `Destination` | `EmailRequestDestination` | Required. `ToAddresses` is required. `CcAddresses` and `BccAddresses` default to empty. |
| `Subject` | `string` | Required. |
| `MessageHtml` | `string?` | Set at least one body. |
| `MessageText` | `string?` | Set at least one body. |
| `Attachments` | `IReadOnlyList<EmailRequestAttachment>` | Default empty. Each has `Name`, `File` (`ReadOnlyMemory<byte>`), and an optional `ContentType`. |

`EmailRequestAddress` takes an optional display name: `new EmailRequestAddress("a@example.com", "Alice")`, or a bare string. When `ContentType` is `null`, the provider derives it from `Name`, falling back to `application/octet-stream`. `EmailAttachmentContentType.Resolve(fileName)` in `Headless.Emails` applies the same rule if you need the value yourself.

`SendSingleEmailResponse` has `Success`, `ProviderMessageId`, and `FailureError`:

- On success, `ProviderMessageId` holds the backend's id when it returns one: the SES message id, the ACS operation id, or the SMTP server's final response. Otherwise it is `null`.
- On failure, `FailureError` is non-null. The compiler knows this after `if (!response.Success)`, so you need no null check.
- Build responses in a test double with `SendSingleEmailResponse.Succeeded(providerMessageId)`, `Failed(reason)` (a blank reason throws `ArgumentException`), or `FromException(exception)`.

### Default and named senders

```csharp
builder.Services.AddHeadlessEmails(setup =>
{
    setup.UseAwsSes(awsOptions); // default (optional)
    setup.AddNamed("marketing", i => i.UseMailkit(smtpSection)); // keyed "marketing"
    setup.AddNamed("alerts", i => i.UseAzure(acsSection)); // keyed "alerts"
});
```

- The default resolves as the unkeyed `IEmailSender`. Every sender is a singleton.
- A named sender resolves as a keyed `IEmailSender` under its name, and through `IEmailSenderProvider`.
- Each named sender owns its options and backend client: its own MailKit connection pool, ACS `EmailClient`, or SES client. Two named senders never share configuration, and no named sender reads the default's options.

## Choosing a Provider

| Provider | Use when | Avoid when | Trade-off |
| --- | --- | --- | --- |
| `Headless.Emails.Azure` | You run on Azure and want managed-identity auth. | You need SMTP portability. | `SendAsync` waits for ACS to reach a terminal status. Managed-domain sends are limited to 5 per minute. The sender display name is ignored. |
| `Headless.Emails.Aws` | You run on AWS and use SES. | You need SMTP portability. | A send without attachments uses the SES structured API; a send with attachments goes as raw MIME. |
| `Headless.Emails.Mailkit` | You send through any SMTP server (SendGrid, Gmail, Outlook, on-premises). | You want a provider API with delivery events. | Pooled connections amortize connect and TLS cost. |
| `Headless.Emails.Dev` | Local development, CI, tests. | Production. | No delivery. `UseDevelopment` captures in memory and optionally a file. `UseNoop` discards. |

---

## Headless.Emails.Abstractions

The `IEmailSender` contract, the request and response types, and `IEmailSenderProvider`.

### Setup

```bash
dotnet add package Headless.Emails.Abstractions
```

```csharp
public sealed class NotificationService(IEmailSender emailSender, ILogger<NotificationService> logger)
{
    public async Task SendWelcomeEmailAsync(string to, string name, CancellationToken ct)
    {
        var response = await emailSender
            .SendAsync(
                new SendSingleEmailRequest
                {
                    From = "noreply@example.com",
                    Destination = new EmailRequestDestination { ToAddresses = [new EmailRequestAddress(to)] },
                    Subject = "Welcome!",
                    MessageHtml = $"<h1>Hello {name}!</h1>",
                    MessageText = $"Hello {name}!",
                },
                ct
            )
            .ConfigureAwait(false);

        if (!response.Success)
        {
            logger.LogError("Failed to send email: {Error}", response.FailureError);
        }
    }
}
```

**Verify:** in a test, register `UseDevelopment()`, call your code, and assert that `response.Success` is `true` and `IEmailCaptureStore.Query(new())` returns an email with the subject.

### Design and runtime behavior

- `IEmailSender.SendAsync(SendSingleEmailRequest, CancellationToken)` returns `ValueTask<SendSingleEmailResponse>`.
- `IEmailSenderProvider` has `GetSender(name)`, `GetSenderOrNull(name)`, and `RegisteredNames` (`IReadOnlySet<string>`). Both methods throw `ArgumentException` for a blank name.

---

## Headless.Emails

The registration entry point, `AddHeadlessEmails`. Each provider package references it.

### Setup

```bash
dotnet add package Headless.Emails
```

```csharp
builder.Services.AddHeadlessEmails(setup =>
{
    setup.UseNoop(); // default (optional)
    setup.AddNamed("marketing", i => i.UseNoop()); // keyed "marketing"
});

var marketing = provider.GetRequiredService<IEmailSenderProvider>().GetSender("marketing");
```

**Verify:** a registration mistake throws from `AddHeadlessEmails`, before the host builds. In a test, assert that `IEmailSenderProvider.RegisteredNames` contains each named sender.

### Design and runtime behavior

- `AddHeadlessEmails(Action<HeadlessEmailsSetupBuilder>)` registers `IEmailSenderProvider`, then the default sender when one is configured, then each named sender.
- A setup that throws leaves the service collection unchanged.
- `HeadlessEmailsSetupBuilder` carries no shared options. Each provider binds its own options in its `Use*` call.
- The package references `MimeKit.Core`, not the `MimeKit` meta-package, so it brings no S/MIME, PGP, DKIM, or ARC support and no BouncyCastle. If your app signs or encrypts messages with MimeKit, reference `MimeKit.Cryptography` yourself and call `CryptographyModule.Initialize()` at startup.

---

## Headless.Emails.Aws

The AWS SES v2 provider (`AWSSDK.SimpleEmailV2`), plus typed contracts for SES event notifications.

### Setup

```bash
dotnet add package Headless.Emails.Aws
```

`UseAwsSes(AWSOptions?)` takes the AWS SDK options. Pass `null` to use the `AWSOptions` already registered in DI, or the AWS SDK defaults.

```csharp
using Amazon;
using Amazon.Extensions.NETCore.Setup;
using Amazon.Runtime;

var builder = WebApplication.CreateBuilder(args);

// From configuration: AWS:Region, and credentials from the standard AWS chain.
var awsOptions = builder.Configuration.GetAWSOptions();
builder.Services.AddHeadlessEmails(setup => setup.UseAwsSes(awsOptions));

// Or explicit credentials:
// setup.UseAwsSes(new AWSOptions { Region = RegionEndpoint.USEast1, Credentials = new BasicAWSCredentials("accessKey", "secretKey") });

// Or as a named sender, keyed "ses":
// setup.AddNamed("ses", i => i.UseAwsSes(awsOptions));
```

```json
{
  "AWS": {
    "Region": "us-east-1"
  }
}
```

**Verify:** no startup signal; SES options are not validated. Send one email and assert `response.Success`. A wrong region, an unverified sender, or a missing credential returns a failed response with the SES error message in `FailureError`.

### Design and runtime behavior

- **Failures.** Every SES rejection, such as a rejected message, an unverified `MAIL FROM` domain, a suspended account, or throttling, returns a failed response carrying the SES error message. On success, `ProviderMessageId` is the SES message id.
- **BCC.** BCC recipients go in the SES envelope. On the raw MIME path the `Bcc` header is removed, so recipients never see each other.
- **Display names.** Sender and recipient display names are sent as RFC 5322 mailboxes: a name with `,`, `"`, `<`, or `;` is quoted, and a non-ASCII name is RFC 2047 encoded.
- **Logging.** A failure logs the SES error code, HTTP status, and request id. It never logs addresses or the exception message, which can contain a rejected address.
- **Client.** The default sender registers `IAmazonSimpleEmailServiceV2` only if none is registered yet, so a client you register first wins. A named sender builds its own keyed client, from the options you pass, else the `AWSOptions` in DI, else the `AWS` configuration section.

### Email event tracking

SES can publish sending events (delivery, bounce, complaint, open, click, and others) for a configuration set to a destination such as an Amazon SNS topic. The `Headless.Emails.Aws.Tracking` namespace has typed contracts for those JSON payloads. You own the transport (an SNS HTTPS subscription, an SQS poller, a Lambda) and the handling.

Every property has an explicit `[JsonPropertyName]`, so deserialization works with any `JsonSerializerOptions`. Unknown fields are kept, not rejected.

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
                // Permanent (hard) bounce: suppress this address.
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

Correlate an event with a send through `notification.Mail.MessageId`, which equals the send's `ProviderMessageId`.

Types: `EmailTrackingNotification`, `MailDetails` (`EmailHeader`, `EmailCommonHeaders`), `SesSendEvent`, `SesDeliveryEvent`, `SesDeliveryDelayEvent` (`DelayedRecipient`), `SesOpenEvent`, `SesClickEvent`, `SesBounceEvent` (`BouncedRecipient`), `SesComplaintEvent` (`ComplainedRecipient`), `SesRejectEvent`, and `SesRenderingFailureEvent`. Constants: `EmailEventTypes`, `BounceTypes`, `BounceSubTypes`, `DelayTypes`, and `ComplaintFeedbackTypes`.

---

## Headless.Emails.Azure

The Azure Communication Services (ACS) Email provider.

### Setup

```bash
dotnet add package Headless.Emails.Azure
```

`UseAzure` takes `IConfiguration`, `Action<AzureCommunicationEmailOptions>`, or `Action<AzureCommunicationEmailOptions, IServiceProvider>`. Configure exactly one authentication mode:

| Mode | Set | Bindable from configuration |
| --- | --- | --- |
| Connection string | `ConnectionString` | Yes |
| Access key | `Endpoint` and `AccessKey` | Yes |
| Managed identity | `Endpoint` and `TokenCredential` | No; use a delegate overload |

```csharp
using Azure.Identity;

var builder = WebApplication.CreateBuilder(args);

// Connection string, or endpoint + access key, from configuration:
builder.Services.AddHeadlessEmails(setup => setup.UseAzure(builder.Configuration.GetSection("AzureEmail")));

// Managed identity (needs the Azure.Identity package):
// setup.UseAzure(options =>
// {
//     options.Endpoint = new Uri("https://my-resource.communication.azure.com/");
//     options.TokenCredential = new DefaultAzureCredential();
// });

// As a named sender, keyed "alerts":
// setup.AddNamed("alerts", i => i.UseAzure(builder.Configuration.GetSection("AlertsEmail")));
```

```json
{
  "AzureEmail": {
    "ConnectionString": "endpoint=https://my-resource.communication.azure.com/;accesskey=<key>"
  }
}
```

**Verify:** start the host. No mode, or more than one, fails the start with `OptionsValidationException`. A bare `BuildServiceProvider()` in a test skips that check, so the misconfiguration surfaces only when `IEmailSender` is first resolved. Send one email and assert `response.Success` to prove the domain and credentials.

### Design and runtime behavior

- **Blocking send.** `SendAsync` waits until ACS reaches a terminal status. Under throttling the Azure SDK retries 429 and 5xx responses, honoring `Retry-After`, so a send can take a long time. To bound it, pass a token with a deadline (`CancellationTokenSource.CancelAfter(...)`).
- **Failures.** A rejected request, any other SDK fault, and a send that completes with a non-`Succeeded` status all return a failed response. On success, `ProviderMessageId` is the ACS operation id.
- **Limits.** ACS limits managed-domain sends to 5 per minute.
- **Display name.** ACS takes the sender as a bare address, so the sender's display name is ignored.
- **Logging.** A failure logs the HTTP status, the ACS error code, and the `x-ms-request-id`, or the terminal status and operation id. It never logs addresses.
- **Dependencies.** The package depends on `Azure.Core`, not `Azure.Identity`. Add `Azure.Identity` yourself for `DefaultAzureCredential`.

---

## Headless.Emails.Dev

Development senders that deliver nothing. `UseDevelopment` captures every email in an in-memory inbox, `IEmailCaptureStore`, so a dev inbox page or a test can read it. It can also append a plain-text copy to a file. `UseNoop` discards every email.

### Setup

```bash
dotnet add package Headless.Emails.Dev
```

```csharp
var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddHeadlessEmails(setup =>
    {
        setup.UseDevelopment(); // capture only
        setup.AddNamed("audit", i => i.UseDevelopment(o =>
        {
            o.FilePath = "audit-emails.txt"; // also append a plain-text copy
            o.MaxCapturedAttachmentBytes = 256 * 1024; // keep small attachments for the .eml export
        }));
    });
    // Or discard: setup.UseNoop()
}
```

`UseDevelopment` has the usual overloads: `()`, `(string filePath)`, `(IConfiguration)`, `(Action<DevEmailOptions>)`, and `(Action<DevEmailOptions, IServiceProvider>)`. Each one is on the setup builder and on a named instance.

When `FilePath` is set, each email is appended to the file:

```text
From: sender@example.com
To: recipient@example.com
Subject: Test Email
Message:

Hello World!
--------------------
```

**Verify:** resolve `IEmailCaptureStore`, send one email, and assert that `store.Query(new())` returns it with your subject. With a `FilePath`, the file ends with the message and a `--------------------` separator.

### Configuration

| Option | Default | Notes |
| --- | --- | --- |
| `FilePath` | `null` | File to append a plain-text copy to. `null` writes no file. Must not be empty. |
| `CaptureCapacity` | `500` | Emails the store keeps before it drops the oldest. Greater than 0. All development senders share one store, which takes the largest value any of them sets. |
| `MaxCapturedAttachmentBytes` | `0` | Largest attachment whose content the store keeps. `0` keeps only the name, content type, and size. The limit applies to each attachment, and kept content stays in memory: with limit L and at most K attachments per email, it can reach `CaptureCapacity` × K × L bytes. |

An invalid value fails the start with `OptionsValidationException`.

### Read the captured emails

To read them in the dashboard, add the Dev inbox module: see [Dev inbox module](dashboards/dev-inbox-module.md).

Resolve `IEmailCaptureStore` from the container. It is registered only when a `UseDevelopment` sender is, so `GetService<IEmailCaptureStore>()` returning `null` means no development sender is in use.

```csharp
var store = app.Services.GetRequiredService<IEmailCaptureStore>();

var latest = store.Query(new EmailCaptureQuery { SenderName = "audit", Search = "invoice", Limit = 50 });
var email = store.Get(latest[0].Id);
await email!.WriteEmlAsync(response.Body, cancellationToken); // a "Download .eml" response
store.Delete(email.Id);
store.Clear();
```

- `Query` takes a query (`new()` lists everything) and returns a snapshot, newest first. `Sequence` is the order: it increases strictly with each capture, and `CapturedAt` never runs backwards against it. `Id` is unique, not ordered. `SenderName` matches exactly; the default sender's name is the empty string. `Search` matches, ignoring case, the subject, both bodies, and every address and display name. `Limit` must be positive.
- Each `CapturedEmail` carries `Id`, `Sequence`, `CapturedAt` (from the registered `TimeProvider`), `SenderName`, `From`, `To`, `Cc`, `Bcc`, `Subject`, `HtmlBody`, `TextBody`, `Attachments` (`FileName`, `ContentType`, `SizeBytes`, and `Content` when kept), `FailureError`, `TraceId` (the current `Activity`), and `CorrelationId` (from a registered `ICorrelationIdProvider`).
- The capture copies the request's recipient lists and attachment bytes, so changing the request after the send does not change it.
- `WriteEmlAsync(stream)` writes the message the SMTP and SES providers would send, with CRLF line endings and a `Bcc` header. A kept attachment becomes a part. An attachment whose content was not kept is listed in an `X-Headless-Attachment-Omitted` header. The sender, trace id, correlation id, and failure go in `X-Headless-*` headers.

To follow new emails live, for example from a server-sent events endpoint, wait on the version instead of polling the list:

```csharp
var version = store.Version;
while (!cancellationToken.IsCancellationRequested)
{
    var next = await store.WaitForChangeAsync(version, cancellationToken);
    var added = store.Query(new EmailCaptureQuery { AfterSequence = version });
    // Push `added`. A delete or clear also bumps the version and adds nothing, so re-read the list when `added` is empty.
    version = next;
}
```

### Assert on captured emails in tests

`AddHeadlessEmails` accepts one call per service collection, so a test host cannot add a second registration over the app's. Choose the development provider by environment in `Program.cs`, then run the test host in that environment:

```csharp no-compile
// Program.cs
builder.Services.AddHeadlessEmails(setup =>
{
    if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing"))
    {
        setup.UseDevelopment(); // capture only, no file
    }
    else
    {
        setup.UseMailkit(builder.Configuration.GetSection("Smtp"));
    }
});

// Test
public sealed class SignupTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task sends_the_welcome_email()
    {
        var app = factory.WithWebHostBuilder(b => b.UseEnvironment("Testing"));
        var store = app.Services.GetRequiredService<IEmailCaptureStore>();
        store.Clear(); // the store is a process-wide singleton; clear it so earlier tests do not leak in

        await app.CreateClient().PostAsJsonAsync("/signup", new { email = "lina@example.com" });

        var email = store.Query(new EmailCaptureQuery { Search = "lina@example.com" }).Should().ContainSingle().Subject;
        email.Subject.Should().Be("Welcome");
    }
}
```

The store is shared by every test that uses the same host, so run such tests in one xUnit collection, or filter `Query` by a value unique to the test (a recipient or `SenderName`).

### Design and runtime behavior

- `UseDevelopment` captures the email, then appends it to `FilePath` when one is set, and returns `Succeeded(id)` with the capture id as `ProviderMessageId`. The file copy writes `MessageText` when present, else `MessageHtml`. Concurrent sends do not interleave.
- A request with no body throws `InvalidOperationException`, as the real providers do, and a file that cannot be written throws its `IOException`. Both are captured first with `FailureError` set, so the inbox shows the failed attempt.
- The store lives in memory for the life of the process. A restart empties it. Nothing gates it on the environment, so register `UseDevelopment` only in development, as the setup above does.
- `UseNoop()` discards every request and always returns `Succeeded()`. It never validates or captures the request.

---

## Headless.Emails.Mailkit

The SMTP provider, built on MailKit, with pooled connections.

### Setup

```bash
dotnet add package Headless.Emails.Mailkit
```

`UseMailkit` takes `IConfiguration`, `Action<MailkitSmtpOptions>`, or `Action<MailkitSmtpOptions, IServiceProvider>`.

```csharp
using MailKit.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHeadlessEmails(setup =>
{
    setup.UseMailkit(builder.Configuration.GetSection("Smtp")); // default
    setup.AddNamed("marketing", i => i.UseMailkit(builder.Configuration.GetSection("MarketingSmtp")));
});

// Or in code:
// setup.UseMailkit(options =>
// {
//     options.Server = "smtp.example.com";
//     options.Port = 587;
//     options.User = builder.Configuration["Smtp:User"];
//     options.Password = builder.Configuration["Smtp:Password"];
//     options.SocketOptions = SecureSocketOptions.StartTls;
// });
```

```json
{
  "Smtp": {
    "Server": "smtp.example.com",
    "Port": 587,
    "User": "user@example.com",
    "SocketOptions": "StartTls"
  }
}
```

**Verify:** start the host. A missing `Server` or an out-of-range value fails the start with `OptionsValidationException`. Then send one email and assert `response.Success`. A rejected credential returns a failed response and logs at `Critical` level.

### Configuration

| Option | Default | Notes |
| --- | --- | --- |
| `Server` | (required) | SMTP host name or IP address. |
| `Port` | `587` | Greater than 0. |
| `User`, `Password` | `null` | Set both for authenticated SMTP. Leave both unset for an anonymous relay. Keep `Password` in a secret store. |
| `SocketOptions` | `StartTls` | A MailKit `SecureSocketOptions` value: `None`, `Auto`, `StartTls`, `StartTlsWhenAvailable`, or `SslOnConnect`. |
| `Timeout` | 30 seconds | Bounds connect, authenticate, and each read or write. Greater than zero. |
| `MaxPoolSize` | `10` | Connections kept in the pool. `0` keeps at most one connection; it does not turn pooling off. |
| `IdleProbeThreshold` | 30 seconds | A connection idle longer than this is probed with `NOOP` before a send. `TimeSpan.Zero` probes before every send. Must not be negative. Keep it below the server's idle timeout. Measured in real time, not on the app's `TimeProvider`. |

### Design and runtime behavior

- **Pooling.** Connections are reused across sends. Each named Mailkit sender owns its own pool.
- **Stale connections.** A connection idle longer than `IdleProbeThreshold` sends `NOOP` first, because a server can close an idle session without the client noticing. A failed probe reconnects instead of failing the send.
- **One safe retry.** If a recently used connection turns out to be closed, the send reconnects and retries once, but only when the failure came before the server accepted `MAIL FROM`. The message cannot reach the server twice. A failure after `MAIL FROM` is accepted returns a failure and is not retried.
- **Failures.** SMTP command and protocol errors, a rejected credential, a `530` authentication-required reply, and connect, TLS, or timeout faults all return a failed response. A rejected credential also logs at `Critical` level, because it is a configuration error. A connect timeout returns a failure; only cancellation of your own token throws.
- **After a failure.** The connection is closed instead of returned to the pool, and the next send connects and authenticates again.
- **Success.** `ProviderMessageId` is the SMTP server's final response. A multi-line response keeps every line, joined with `\r\n`.
