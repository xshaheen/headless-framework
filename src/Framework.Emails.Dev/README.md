# Framework.Emails.Dev

Development-time email implementations for local testing and debugging. This package provides email senders that do not actually send network calls, making it safe and easy to develop without a real SMTP server.

## Features

-   **DevEmailSender**: Writes full email content (headers, body, attachments list) to a specified local text file.
-   **NoopEmailSender**: A "No Operation" sender that simply discards the email.

## Usage

These implementations are typically registered conditionally for `Development` environments.

### DevEmailSender

Writes emails to a file, allowing you to inspect exactly what would have been sent.

```csharp
// Example registration (concept)
services.AddSingleton<IEmailSender>(new DevEmailSender("path/to/emails.txt"));
```

_(Check `AddDevEmailExtensions.cs` for the specific extension method provided by this package if available)_

### Output Format

The `DevEmailSender` appends to the file with a separator:

```text
From: sender@example.com
To: recipient@example.com
Subject: Test Email
Message:
Hello World!
--------------------
```
