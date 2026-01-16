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

## Message Retry and Error Handling

MassTransit adapter requires error queue configuration to prevent message loss:

```csharp
services.AddMassTransit(x =>
{
    // Error queue for failed messages
    x.AddConfigureEndpointsCallback((name, cfg) =>
    {
        cfg.UseMessageRetry(r => r.Immediate(5));
        cfg.UseDelayedRedelivery(r => r.Intervals(
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(15)
        ));
    });

    x.UsingRabbitMq((ctx, cfg) =>
    {
        cfg.ConfigureEndpoints(ctx);

        // Dead letter queue
        cfg.ReceiveEndpoint("error", e =>
        {
            e.ConfigureConsumeTopology = false;
        });
    });
});
```

Monitor the error queue for failed messages.

## Message Idempotency

**CRITICAL**: MassTransit provides at-least-once delivery. Messages may be duplicated during network failures or broker restarts. **All message handlers MUST be idempotent.**

### Implementing Idempotency

Use the `UniqueId` from `IMessageSubscribeMedium<T>` to detect duplicates:

```csharp
await messageBus.SubscribeAsync<OrderPlaced>(async (medium, ct) =>
{
    var messageId = medium.UniqueId;

    // Check if already processed
    if (await db.ProcessedMessages.AnyAsync(m => m.MessageId == messageId, ct))
    {
        _logger.LogInformation("Duplicate message {MessageId}, skipping", messageId);
        return; // Already processed
    }

    using var transaction = await db.Database.BeginTransactionAsync(ct);
    try
    {
        // Process business logic
        await db.Inventory.UpdateAsync(...);
        await paymentProcessor.ChargeCard(...);

        // Record message as processed
        await db.ProcessedMessages.AddAsync(new ProcessedMessage
        {
            MessageId = messageId,
            ProcessedAt = DateTime.UtcNow
        }, ct);

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
    catch
    {
        await transaction.RollbackAsync(ct);
        throw; // MassTransit will retry
    }
}, cancellationToken);
```

### Database Table

```sql
CREATE TABLE ProcessedMessages (
    MessageId UUID PRIMARY KEY,
    ProcessedAt TIMESTAMP NOT NULL,
    ExpiresAt TIMESTAMP -- Optional: for cleanup
);

CREATE INDEX IX_ProcessedMessages_ExpiresAt ON ProcessedMessages(ExpiresAt);
```

### Cleanup

Periodically remove old entries:

```csharp
await db.ProcessedMessages
    .Where(m => m.ProcessedAt < DateTime.UtcNow.AddDays(-7))
    .DeleteAsync();
```
