using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Demo.Data;
using Headless.Messaging;
using Headless.Messaging.Dashboard;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
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
        options.RequireHttpsMetadata = false;
        options.SaveToken = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(key),
#pragma warning disable CA5404
            ValidateIssuer = false,
            ValidateAudience = false,
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

builder.Services.AddMessaging(options =>
{
    options.UseInMemoryStorage();
    options.UseInMemoryMessageQueue();
    options.UseDashboard(d => d.WithHostAuthentication(dashboardPolicy));
});

var app = builder.Build();

// Seed demo data so the dashboard has content to display
await seedDemoData(app.Services);

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.MapGet("/", () => Results.LocalRedirect("/messaging", true));

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

return;

static async Task seedDemoData(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var dataStorage = scope.ServiceProvider.GetRequiredService<IDataStorage>();
    var now = DateTime.UtcNow;

    // Published messages — mix of statuses
    string[][] publishedTopics =
    [
        ["Orders.Created", "OrderCreatedEvent"],
        ["Orders.Shipped", "OrderShippedEvent"],
        ["Payments.Processed", "PaymentProcessedEvent"],
        ["Users.Registered", "UserRegisteredEvent"],
        ["Inventory.Updated", "InventoryUpdatedEvent"],
        ["Notifications.Sent", "NotificationSentEvent"],
        ["Reports.Generated", "ReportGeneratedEvent"],
    ];

    for (var i = 0; i < 25; i++)
    {
        var topic = publishedTopics[i % publishedTopics.Length];
        var msgId = Guid.NewGuid().ToString();
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = msgId,
            [Headers.MessageName] = topic[0],
            [Headers.Type] = topic[1],
            [Headers.SentTime] = now.AddMinutes(-Random.Shared.Next(1, 1440)).ToString("O"),
        };

        var payload = new
        {
            Id = i + 1,
            Topic = topic[0],
            Data = $"Sample payload #{i + 1}",
        };
        var message = new Message(headers, payload);
        var medium = await dataStorage.StoreMessageAsync(topic[0], message);

        // ~60% succeeded, ~20% failed, ~20% remain scheduled
        if (i % 5 < 3)
        {
            await dataStorage.ChangePublishStateAsync(medium, Headless.Messaging.Internal.StatusName.Succeeded);
        }
        else if (i % 5 == 3)
        {
            message.AddOrUpdateException(new InvalidOperationException($"Processing failed for message #{i + 1}"));
            medium.Origin = message;
            await dataStorage.ChangePublishStateAsync(medium, Headless.Messaging.Internal.StatusName.Failed);
        }
    }

    // Received messages — mix of groups
    string[][] receivedTopics =
    [
        ["Orders.Created", "order-service", "OrderCreatedHandler"],
        ["Orders.Created", "notification-service", "OrderNotificationHandler"],
        ["Orders.Shipped", "tracking-service", "ShipmentTrackingHandler"],
        ["Payments.Processed", "accounting-service", "PaymentReconciliationHandler"],
        ["Users.Registered", "email-service", "WelcomeEmailHandler"],
        ["Inventory.Updated", "warehouse-service", "StockUpdateHandler"],
    ];

    for (var i = 0; i < 30; i++)
    {
        var topic = receivedTopics[i % receivedTopics.Length];
        var msgId = Guid.NewGuid().ToString();
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = msgId,
            [Headers.MessageName] = topic[0],
            [Headers.Group] = topic[1],
            [Headers.Type] = topic[2],
            [Headers.SentTime] = now.AddMinutes(-Random.Shared.Next(1, 1440)).ToString("O"),
        };

        var payload = new
        {
            Id = i + 1,
            Topic = topic[0],
            Group = topic[1],
            Data = $"Received payload #{i + 1}",
        };
        var message = new Message(headers, payload);
        await dataStorage.StoreReceivedMessageAsync(topic[0], topic[1], message);
    }
}
