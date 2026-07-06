# Headless.Messaging.Aws.InMemory.Demo

ASP.NET Core demo for wiring the AWS messaging transport with in-memory storage.

## Shows

- Assembly scanning with `ForMessagesFromAssembly(...)`.
- AWS transport setup through `UseAws(...)`.
- In-memory storage through `UseInMemoryStorage()`.
- Messaging dashboard registration with `WithNoAuth()`.
- Controller-based publishing.

## Run

```bash
dotnet run --project demo/Headless.Messaging.Aws.InMemory.Demo
```

The demo configures `RegionEndpoint.CNNorthWest1`; AWS credentials and reachable AWS messaging resources must come from your normal AWS SDK environment.

## Production Note

The dashboard is unauthenticated and storage is in memory. Use durable storage and production authentication before using this shape outside local demos.
