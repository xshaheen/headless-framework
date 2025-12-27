# Framework.Emails.Mailkit

SMTP implementation of the email abstraction using [MailKit](https://github.com/jstedfast/MailKit). This package enables sending emails via any standard SMTP server.

## Features

-   **SMTP Support**: Send emails via any SMTP provider (Gmail, Outlook, custom servers, etc.).
-   **MimeKit Integration**: Uses `Framework.Emails.Core` for robust MIME message creation.
-   **Fluent Validation**: Includes validation for SMTP configuration options.

## Usage

### Registration

You can register the MailKit sender using `IConfiguration` or a configuration action.

**Using IConfiguration:**

```csharp
// Assumes configuration section matches MailkitSmtpOptions structure
services.AddMailKitEmailSender(configuration.GetSection("Smtp"));
```

**Using Action:**

```csharp
services.AddMailKitEmailSender(options =>
{
    options.Server = "smtp.example.com";
    options.Port = 587;
    options.User = "user@example.com";
    options.Password = "securepassword";
    options.SocketOptions = SecureSocketOptions.StartTls;
});
```

### Configuration Options (`MailkitSmtpOptions`)

| Property        | Description                                                        |
| --------------- | ------------------------------------------------------------------ |
| `Server`        | The hostname of the SMTP server (required).                        |
| `Port`          | The port to connect to (default: 25).                              |
| `User`          | Username for authentication.                                       |
| `Password`      | Password for authentication.                                       |
| `SocketOptions` | `SecureSocketOptions` from MailKit (e.g., StartTls, SslOnConnect). |

The `RequiresAuthentication` property is automatically derived if User and Password are set.
