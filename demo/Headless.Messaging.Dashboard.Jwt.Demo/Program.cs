using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Demo.Data;
using Headless.Messaging;
using Headless.Messaging.Dashboard;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var key = Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!);
builder
    .Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = false; // DEMO ONLY — require HTTPS in production
        options.SaveToken = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(key),
#pragma warning disable CA5404
            ValidateIssuer = false, // DEMO ONLY — validate issuer in production
            ValidateAudience = false, // DEMO ONLY — validate audience in production
#pragma warning restore CA5404
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.Zero,
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (context.Request.Query.ContainsKey("access_token"))
                {
                    context.Token = context.Request.Query["access_token"];
                }

                return Task.CompletedTask;
            },
        };
    });

const string dashboardPolicy = "DashboardPolicy";

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(dashboardPolicy, policy => policy.RequireAuthenticatedUser());
});

builder.Services.AddHttpClient();

builder.Services.AddHeadlessMessaging(options =>
{
    options.FailedRetryCount = 0;
    options.SubscribeFromAssembly(typeof(Program).Assembly);
    options.UseInMemoryStorage();
    options.UseInMemoryMessageQueue();
    options.UseDashboard(d => d.WithHostAuthentication(dashboardPolicy));
});

builder.Services.AddHostedService<DemoMessagePublisher>();

var app = builder.Build();

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.MapGet("/", () => Results.LocalRedirect("/index.html", true));

app.MapPost(
    "/security/createToken",
    [AllowAnonymous]
    (User user) =>
    {
        if (user is { UserName: "bob", Password: "bob" })
        {
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity([
                    new Claim("Id", Guid.NewGuid().ToString()),
                    new Claim(JwtRegisteredClaimNames.Sub, user.UserName),
                    new Claim(JwtRegisteredClaimNames.Email, user.UserName),
                    new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                ]),
                Expires = DateTime.UtcNow.AddMinutes(60),
                Issuer = "Test",
                Audience = "Test",
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(key),
                    SecurityAlgorithms.HmacSha512Signature
                ),
            };
            var tokenHandler = new JwtSecurityTokenHandler();
            var token = tokenHandler.CreateToken(tokenDescriptor);
            var stringToken = tokenHandler.WriteToken(token);
            return Results.Ok(stringToken);
        }
        return Results.Unauthorized();
    }
);

app.UseAuthentication();
app.UseAuthorization();

await app.RunAsync();

// ---------------------------------------------------------------------------
// Demo message types
// ---------------------------------------------------------------------------

public sealed record OrderCreated
{
    public required int OrderId { get; init; }
    public required string CustomerName { get; init; }
    public required decimal Amount { get; init; }
}

public sealed record PaymentProcessed
{
    public required string PaymentId { get; init; }
    public required int OrderId { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
}

public sealed record UserRegistered
{
    public required string UserId { get; init; }
    public required string Email { get; init; }
    public required string Plan { get; init; }
}

public sealed record InventoryUpdated
{
    public required string ProductId { get; init; }
    public required int Quantity { get; init; }
    public required string Warehouse { get; init; }
}

// ---------------------------------------------------------------------------
// Demo consumers — some intentionally fail to populate the dashboard with
// failed messages and exception stack traces.
// ---------------------------------------------------------------------------

public sealed class OrderCreatedConsumer(ILogger<OrderCreatedConsumer> logger) : IConsume<OrderCreated>
{
    public async ValueTask Consume(ConsumeContext<OrderCreated> context, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Processing order #{OrderId} from {Customer} — ${Amount}",
            context.Message.OrderId,
            context.Message.CustomerName,
            context.Message.Amount
        );
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
                $"Notification gateway timeout while sending order confirmation for Order #{context.Message.OrderId} " +
                $"to customer {context.Message.CustomerName}. The SMTP server at smtp.example.com:587 did not respond within 30 seconds."
            );
        }

        logger.LogInformation(
            "Sent order notification for #{OrderId} to {Customer}",
            context.Message.OrderId,
            context.Message.CustomerName
        );
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
                    $"Ledger entry mismatch: expected {context.Message.Amount:C} {context.Message.Currency} " +
                    $"but settlement reported {context.Message.Amount * 0.99m:C} {context.Message.Currency}. " +
                    "This may indicate a currency conversion rounding error."
                ),
                new TimeoutException(
                    "Accounting service at https://accounting.internal/api/reconcile " +
                    "did not respond within the configured 15-second timeout."
                )
            );
        }

        logger.LogInformation(
            "Reconciled payment {PaymentId} for order #{OrderId}",
            context.Message.PaymentId,
            context.Message.OrderId
        );
        await Task.Delay(Random.Shared.Next(100, 400), cancellationToken);
    }
}

public sealed class UserRegisteredConsumer(ILogger<UserRegisteredConsumer> logger) : IConsume<UserRegistered>
{
    public async ValueTask Consume(ConsumeContext<UserRegistered> context, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Welcome email queued for {Email} (plan: {Plan})",
            context.Message.Email,
            context.Message.Plan
        );
        // Simulate slow email rendering
        await Task.Delay(Random.Shared.Next(200, 600), cancellationToken);
    }
}

public sealed class InventoryUpdatedConsumer(ILogger<InventoryUpdatedConsumer> logger) : IConsume<InventoryUpdated>
{
    public async ValueTask Consume(ConsumeContext<InventoryUpdated> context, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Stock updated: {ProductId} now {Quantity} units at {Warehouse}",
            context.Message.ProductId,
            context.Message.Quantity,
            context.Message.Warehouse
        );
        await Task.Delay(Random.Shared.Next(20, 80), cancellationToken);
    }
}

// ---------------------------------------------------------------------------
// Background publisher — seeds initial data then publishes every 30 seconds
// ---------------------------------------------------------------------------

public sealed class DemoMessagePublisher(
    IServiceScopeFactory scopeFactory,
    ILogger<DemoMessagePublisher> logger
) : BackgroundService
{
    private static readonly string[] CustomerNames =
        ["Alice Johnson", "Bob Smith", "Carol Davis", "Dave Wilson", "Eve Martinez", "Frank Lee", "Grace Chen"];

    private static readonly string[] Products =
        ["SKU-1001", "SKU-2042", "SKU-3099", "SKU-4010", "SKU-5077", "SKU-6023"];

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
                logger.LogInformation("Published {Count} demo messages (cycle #{Cycle})", count, _counter);
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
