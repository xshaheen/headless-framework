# Headless.Messaging.UnitOfWork

The enlisted publish contract for Headless Messaging, reached from a unit of work as `unit.Outbox`.

## Why use this package

`IBus` and `IQueue` are autonomous: a publish through them survives the caller's rollback. This package declares
the opposite surface, so a reader can tell from the call line that a message is written inside the unit's
transaction and discarded with it. Reference it where you publish enlisted messages and cannot reference
`Headless.Messaging.Core` — the implementation ships there and is registered by `AddHeadlessMessaging`.

## Install

```bash
dotnet add package Headless.Messaging.UnitOfWork
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Messaging guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/messaging.md#headlessmessagingunitofwork)
