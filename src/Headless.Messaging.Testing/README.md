# Headless.Messaging.Testing

In-process test harness for asserting on published, consumed, faulted, and exhausted messages without external infrastructure.

## Why use this package

Integration-testing a messaging pipeline typically requires a running broker and timing-sensitive polling. This package eliminates both: it wires the full pipeline in memory and exposes awaitable assertions that block until the expected message arrives (or the timeout elapses).

## Install

```bash
dotnet add package Headless.Messaging.Testing
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Messaging guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/messaging.md#headlessmessagingtesting)
