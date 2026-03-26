using Headless.Messaging;

namespace Demo;

/// <summary>
/// Demo consumers — some intentionally fail to populate the dashboard with
/// failed messages and exception stack traces.
/// </summary>
public sealed class OrderCreatedConsumer(ILogger<OrderCreatedConsumer> logger) : IConsume<OrderCreated>
{
    public async ValueTask Consume(ConsumeContext<OrderCreated> context, CancellationToken cancellationToken)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Processing order #{OrderId} from {Customer} — ${Amount}",
                context.Message.OrderId,
                context.Message.CustomerName,
                context.Message.Amount
            );
        }
        await Task.Delay(Random.Shared.Next(30, 150), cancellationToken);
    }
}

public sealed class OrderNotificationConsumer(ILogger<OrderNotificationConsumer> logger) : IConsume<OrderCreated>
{
    public async ValueTask Consume(ConsumeContext<OrderCreated> context, CancellationToken cancellationToken)
    {
        // ~25% failure rate — simulates notification gateway flakiness
        if (Random.Shared.Next(4) == 0)
        {
            throw new InvalidOperationException(
                $"Notification gateway timeout while sending order confirmation for Order #{context.Message.OrderId} "
                    + $"to customer {context.Message.CustomerName}. The SMTP server at smtp.example.com:587 did not respond within 30 seconds."
            );
        }

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Sent order notification for #{OrderId} to {Customer}",
                context.Message.OrderId,
                context.Message.CustomerName
            );
        }
        await Task.Delay(Random.Shared.Next(50, 200), cancellationToken);
    }
}

public sealed class PaymentProcessedConsumer(ILogger<PaymentProcessedConsumer> logger) : IConsume<PaymentProcessed>
{
    public async ValueTask Consume(ConsumeContext<PaymentProcessed> context, CancellationToken cancellationToken)
    {
        // ~20% failure rate — simulates payment reconciliation issues
        if (Random.Shared.Next(5) == 0)
        {
            throw new AggregateException(
                $"Payment reconciliation failed for {context.Message.PaymentId}",
                new InvalidOperationException(
                    $"Ledger entry mismatch: expected {context.Message.Amount:C} {context.Message.Currency} "
                        + $"but settlement reported {context.Message.Amount * 0.99m:C} {context.Message.Currency}. "
                        + "This may indicate a currency conversion rounding error."
                ),
                new TimeoutException(
                    "Accounting service at https://accounting.internal/api/reconcile "
                        + "did not respond within the configured 15-second timeout."
                )
            );
        }

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Reconciled payment {PaymentId} for order #{OrderId}",
                context.Message.PaymentId,
                context.Message.OrderId
            );
        }
        await Task.Delay(Random.Shared.Next(100, 400), cancellationToken);
    }
}

public sealed class UserRegisteredConsumer(ILogger<UserRegisteredConsumer> logger) : IConsume<UserRegistered>
{
    public async ValueTask Consume(ConsumeContext<UserRegistered> context, CancellationToken cancellationToken)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Welcome email queued for {Email} (plan: {Plan})",
                context.Message.Email,
                context.Message.Plan
            );
        }
        // Simulate slow email rendering
        await Task.Delay(Random.Shared.Next(200, 600), cancellationToken);
    }
}

public sealed class InventoryUpdatedConsumer(ILogger<InventoryUpdatedConsumer> logger) : IConsume<InventoryUpdated>
{
    public async ValueTask Consume(ConsumeContext<InventoryUpdated> context, CancellationToken cancellationToken)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Stock updated: {ProductId} now {Quantity} units at {Warehouse}",
                context.Message.ProductId,
                context.Message.Quantity,
                context.Message.Warehouse
            );
        }
        await Task.Delay(Random.Shared.Next(20, 80), cancellationToken);
    }
}
