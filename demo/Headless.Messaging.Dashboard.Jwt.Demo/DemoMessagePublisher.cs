using Headless.Messaging;

namespace Demo;

/// <summary>
/// Seeds initial messages on startup, then publishes more every 30 seconds.
/// </summary>
public sealed class DemoMessagePublisher(IServiceScopeFactory scopeFactory, ILogger<DemoMessagePublisher> logger)
    : BackgroundService
{
    private static readonly string[] CustomerNames =
    [
        "Alice Johnson",
        "Bob Smith",
        "Carol Davis",
        "Dave Wilson",
        "Eve Martinez",
        "Frank Lee",
        "Grace Chen",
    ];

    private static readonly string[] Products =
    [
        "SKU-1001",
        "SKU-2042",
        "SKU-3099",
        "SKU-4010",
        "SKU-5077",
        "SKU-6023",
    ];

    private static readonly string[] Warehouses = ["US-East", "US-West", "EU-Central", "APAC-South"];
    private static readonly string[] Currencies = ["USD", "EUR", "GBP"];
    private static readonly string[] Plans = ["Free", "Starter", "Pro", "Enterprise"];

    private int _counter;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for the messaging bootstrapper to register subscriber groups
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);

        // Initial burst — populate dashboard with some history
        logger.LogInformation("Seeding initial demo messages...");
        await PublishBatch(15, stoppingToken);
        logger.LogInformation("Initial seed complete — switching to 30s interval");

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

            try
            {
                var count = Random.Shared.Next(2, 6);
                await PublishBatch(count, stoppingToken);
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("Published {Count} demo messages (cycle #{Cycle})", count, _counter);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Error publishing demo messages");
            }
        }
    }

    private async Task PublishBatch(int count, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IOutboxPublisher>();

        for (var i = 0; i < count; i++)
        {
            _counter++;
            await PublishRandomMessage(publisher, ct);
        }
    }

    private async Task PublishRandomMessage(IOutboxPublisher publisher, CancellationToken ct)
    {
        switch (Random.Shared.Next(4))
        {
            case 0:
                await publisher.PublishAsync(
                    new OrderCreated
                    {
                        OrderId = _counter,
                        CustomerName = CustomerNames[Random.Shared.Next(CustomerNames.Length)],
                        Amount = Math.Round((decimal)(Random.Shared.NextDouble() * 500 + 10), 2),
                    },
                    cancellationToken: ct
                );
                break;

            case 1:
                await publisher.PublishAsync(
                    new PaymentProcessed
                    {
                        PaymentId = $"PAY-{_counter:D6}",
                        OrderId = Random.Shared.Next(1, _counter + 1),
                        Amount = Math.Round((decimal)(Random.Shared.NextDouble() * 500 + 10), 2),
                        Currency = Currencies[Random.Shared.Next(Currencies.Length)],
                    },
                    cancellationToken: ct
                );
                break;

            case 2:
                await publisher.PublishAsync(
                    new UserRegistered
                    {
                        UserId = Guid.NewGuid().ToString()[..8],
                        Email = $"user{_counter}@example.com",
                        Plan = Plans[Random.Shared.Next(Plans.Length)],
                    },
                    cancellationToken: ct
                );
                break;

            case 3:
                await publisher.PublishAsync(
                    new InventoryUpdated
                    {
                        ProductId = Products[Random.Shared.Next(Products.Length)],
                        Quantity = Random.Shared.Next(0, 500),
                        Warehouse = Warehouses[Random.Shared.Next(Warehouses.Length)],
                    },
                    cancellationToken: ct
                );
                break;
        }
    }
}
