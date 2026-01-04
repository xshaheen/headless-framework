# Framework.Emails.Abstraction

Core abstractions and contracts for email sending capabilities within the framework. This package defines the standardized interface `IEmailSender` and related data models that other implementations adhere to.

## Features

-   **Standardized Interface**: Defines `IEmailSender` for loose coupling of email providers.
-   **Contracts**: strongly-typed models for email requests and responses.

## Key Components

### IEmailSender

The primary interface for sending emails.

```csharp
public interface IEmailSender
{
    ValueTask<SendSingleEmailResponse> SendAsync(
        SendSingleEmailRequest request,
        CancellationToken cancellationToken = default
    );
}
```

### Models

-   **SendSingleEmailRequest**: Represents a request to send a single email, including:
    -   From/To/Cc/Bcc addresses
    -   Subject
    -   Text and HTML body content
    -   Attachments
-   **SendSingleEmailResponse**: Represents the result of the email send operation.
