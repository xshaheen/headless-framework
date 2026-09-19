# Headless.EntityFramework.Messaging

Bridge package that ships the real `IHeadlessOutboxDispatcher` so integration events emitted during EF saves are written to the messaging outbox atomically with the business data.

## Why use this package

`Headless.EntityFramework` defines the `IHeadlessOutboxDispatcher` seam but ships no implementation, so it carries no messaging dependency. This package supplies the implementation: integration events emitted by entities during an EF `SaveChanges` are written to the messaging outbox atomically with the business data and delivered to the broker after commit — without the core Entity Framework package depending on messaging.

## Install

```bash
dotnet add package Headless.EntityFramework.Messaging
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [ORM guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/orm.md#headlessentityframeworkmessaging)
