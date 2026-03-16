using Demo;
using Demo.Contracts.DomainEvents;
using Demo.Contracts.IntegrationEvents;
using Headless.Abstractions;
using Headless.Messaging;
using Headless.Messaging.Dashboard;
using Headless.Messaging.Messages;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLogging(l => l.AddConsole());

builder.Services.AddMessaging(c =>
{
    c.Subscribe<SampleSubscriber>().Topic("messaging.sample.tests");

    c.UseInMemoryStorage();
    c.UseAzureServiceBus(asb =>
    {
        asb.ConnectionString = builder.Configuration.GetConnectionString("AzureServiceBus")!;
        asb.CustomHeadersBuilder = (message, serviceProvider) =>
        {
            var longIdGenerator = serviceProvider.GetRequiredService<ILongIdGenerator>();

            return new List<KeyValuePair<string, string>>
            {
                new(Headers.MessageId, longIdGenerator.Create().ToString(CultureInfo.InvariantCulture)),
                new(Headers.MessageName, message.Subject),
                new("IsFromSampleProject", "'true'"),
            };
        };
        asb.SqlFilters = new List<KeyValuePair<string, string>>
        {
            new("IsFromSampleProjectFilter", "IsFromSampleProject = 'true'"),
        };

        asb.ConfigureCustomProducer<EntityCreatedForIntegration>(cfg =>
            cfg.UseTopic("entity-created").WithSubscription()
        );
        asb.ConfigureCustomProducer<EntityDeletedForIntegration>(cfg =>
            cfg.UseTopic("entity-deleted").WithSubscription()
        );
    });

    c.UseDashboard(d => d.AllowAnonymousExplicit = true);
});

var app = builder.Build();

app.MapGet(
    "/entity-created-for-integration",
    async (IOutboxPublisher publisher) =>
    {
        var message = new EntityCreatedForIntegration(Guid.NewGuid());
        await publisher.PublishAsync(message, new PublishOptions { Topic = nameof(EntityCreatedForIntegration) });
    }
);

app.MapGet(
    "/entity-deleted-for-integration",
    async (IOutboxPublisher publisher) =>
    {
        var message = new EntityDeletedForIntegration(Guid.NewGuid());
        await publisher.PublishAsync(message, new PublishOptions { Topic = nameof(EntityDeletedForIntegration) });
    }
);

app.MapGet(
    "/entity-created",
    async (IOutboxPublisher publisher) =>
    {
        var message = new EntityCreated(Guid.NewGuid());
        await publisher.PublishAsync(message, new PublishOptions { Topic = nameof(EntityCreated) });
    }
);

app.MapGet(
    "/entity-deleted",
    async (IOutboxPublisher publisher) =>
    {
        var message = new EntityDeleted(Guid.NewGuid());
        await publisher.PublishAsync(message, new PublishOptions { Topic = nameof(EntityDeleted) });
    }
);

app.Run();
