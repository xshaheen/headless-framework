# Headless.Messaging.Aws.InMemory.Demo

ASP.NET Core demo for wiring the AWS messaging transport with in-memory storage.

## Shows

- A `[BusConsumer]` consumer registered through the generated module with `AddModule<…MessagingModule>()`.
- AWS transport setup through `UseAws(...)`.
- `AddDeployStepsOnStartup()`, which creates the SNS topics and SQS queues as the demo starts; a deployed app runs `resources provision` instead.
- In-memory storage through `UseInMemoryStorage()`.
- Messaging dashboard registration with `WithNoAuth()`.
- Controller-based publishing.

## Run

```bash
dotnet run --project demo/Headless.Messaging.Aws.InMemory.Demo
```

The demo configures `RegionEndpoint.CNNorthWest1`; AWS credentials come from your normal AWS SDK environment, and the identity must be allowed to create the topics and queues the demo provisions.

## Production Note

The dashboard is unauthenticated and storage is in memory. Use durable storage and production authentication before using this shape outside local demos.
