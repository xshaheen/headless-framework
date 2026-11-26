# Headless.Sms.Testing

Test-only doubles for Headless SMS delivery.

## Why use this package

A test that checks what a user was texted needs a sender that keeps each message, and the development senders write to a file or discard the message. This package supplies `RecordingSmsSender`, an in-memory `ISmsSender` and `IBulkSmsSender` that records every accepted request, so each test project does not keep its own copy of a fake.

## Install

```bash
dotnet add package Headless.Sms.Testing
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [SMS guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/sms.md#headlesssmstesting)
