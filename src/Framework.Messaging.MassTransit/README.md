# Framework.Messaging.MassTransit

MassTransit adapter for enterprise messaging with RabbitMQ, Azure Service Bus, and more.

## Service Lifetime

**Important**: This adapter registers services as **Scoped** to align with MassTransit's `IPublishEndpoint` lifetime. MassTransit uses scoped registration for publishing to maintain proper ASP.NET request scopes and transaction boundaries.

### Using from Singleton Services

If you need to publish messages from singleton services (e.g., `IHostedService`, background workers), create a scope:

```csharp
public class MyBackgroundService(IServiceProvider serviceProvider) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = serviceProvider.CreateScope();
        var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        await messageBus.PublishAsync(new MyMessage(), cancellationToken: stoppingToken);
    }
}
```

### Why Scoped vs Singleton?

- **MassTransit's IPublishEndpoint**: Scoped (by design)
- **MassTransit's IReceiveEndpointConnector**: Singleton (used only during subscription setup)
- **This adapter**: Scoped (follows IPublishEndpoint since it's used for ongoing operations)

This differs from the Foundatio adapter (Singleton) because:
- Foundatio's `IMessageBus` is inherently singleton
- MassTransit requires scoped context for proper request tracking and transactions
- The adapter must match MassTransit's architectural constraints
