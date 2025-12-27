# Framework.Emails.Core

Core utilities and shared logic for email implementations. This package primarily provides conversion logic to bridge the framework's abstractions with `MimeKit`.

## Features

-   **MimeKit Integration**: Utilities to convert framework email contracts into `MimeKit` objects.

## Key Components

### EmailToMimMessageConverter

Provides extension methods to convert a `SendSingleEmailRequest` to a `MimeKit.MimeMessage`. This is particularly useful for implementations that rely on MimeKit/MailKit.

```csharp
MimeMessage message = await request.ConvertToMimeMessageAsync(cancellationToken);
```

This handles:

-   Mapping addresses (To, From, Cc, Bcc)
-   Setting Subject
-   Building the body (Text/HTML)
-   Attaching files
