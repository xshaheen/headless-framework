# Headless.Messaging.AzureServiceBus.InMemory.Demo

ASP.NET Core demo for Azure Service Bus publishing with in-memory Headless storage.

## Shows

- `UseAzureServiceBus(...)` with a configured connection string.
- Custom headers through `CustomHeadersBuilder`.
- SQL filters through `SqlFilters`.
- Custom topic/subscription producer configuration.
- Dashboard registration with `WithNoAuth()`.
- Minimal endpoints that publish integration and domain messages.

## Run

```bash
dotnet run --project demo/Headless.Messaging.AzureServiceBus.InMemory.Demo
```

Set the `AzureServiceBus` connection string in configuration before running.

## Production Note

The dashboard is unauthenticated and storage is in memory. Use durable storage, protected dashboard access, and non-demo credentials for production.
