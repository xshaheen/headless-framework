# Headless.Messaging.Console.Demo

Console demo for the smallest in-process messaging setup.

## Shows

- `AddHeadlessMessaging(...)` without ASP.NET Core.
- In-memory transport through `UseInMemory()`.
- In-memory storage through `UseInMemoryStorage()`.
- Bus consumer registration with `OnBus<TConsumer>()`.
- `IOutboxBus.PublishAsync(...)` with callback metadata.
- A custom bus consume middleware.

## Run

```bash
dotnet run --project demo/Headless.Messaging.Console.Demo
```

The process publishes a `ShowTimeEvent` every two seconds until the console exits.

## Production Note

This demo is local-only. In-memory transport and storage do not provide durable production messaging.
