# Headless.Messaging.Pulsar.InMemory.Demo

ASP.NET Core demo for Pulsar transport with in-memory Headless storage.

## Shows

- Assembly scanning with `ForMessagesFromAssembly(...)`.
- Pulsar transport through `UsePulsar(...)`.
- In-memory storage through `UseInMemoryStorage()`.
- Messaging dashboard registration with `WithNoAuth()`.
- Controller-based publishing.

## Run

```bash
dotnet run --project demo/Headless.Messaging.Pulsar.InMemory.Demo
```

The Pulsar URI defaults to `pulsar://localhost:6650` and can be overridden with `AppSettings:PulsarUri`.

## Production Note

The dashboard is unauthenticated and storage is in memory. Use durable storage and protected dashboard access for production.
