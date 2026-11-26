# Headless.Emails.Testing

Test-only doubles for Headless email delivery.

## Why use this package

A test that checks what a user was emailed needs a sender that keeps each message, and the development senders write to a file or discard the message. This package supplies `RecordingEmailSender`, an in-memory `IEmailSender` that records every accepted request, so each test project does not keep its own copy of a fake.

## Install

```bash
dotnet add package Headless.Emails.Testing
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Email guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/emails.md#headlessemailstesting)
