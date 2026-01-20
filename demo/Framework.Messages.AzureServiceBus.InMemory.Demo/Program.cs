using Demo;
using Demo.Contracts.DomainEvents;
using Demo.Contracts.IntegrationEvents;
using Framework.Abstractions;
using Framework.Messages;
using Framework.Messages.Messages;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLogging(l => l.AddConsole());

builder.Services.AddMessages(c =>
{
    c.Consumer<SampleSubscriber>().Topic("cap.sample.tests").Build();

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

    c.UseDashboard();
});

var app = builder.Build();

app.MapGet(
    "/entity-created-for-integration",
    async (IOutboxPublisher publisher) =>
    {
        var message = new EntityCreatedForIntegration(Guid.NewGuid());
        await publisher.PublishAsync(nameof(EntityCreatedForIntegration), message);
    }
);

app.MapGet(
    "/entity-deleted-for-integration",
    async (IOutboxPublisher publisher) =>
    {
        var message = new EntityDeletedForIntegration(Guid.NewGuid());
        await publisher.PublishAsync(nameof(EntityDeletedForIntegration), message);
    }
);

app.MapGet(
    "/entity-created",
    async (IOutboxPublisher publisher) =>
    {
        var message = new EntityCreated(Guid.NewGuid());
        await publisher.PublishAsync(nameof(EntityCreated), message);
    }
);

app.MapGet(
    "/entity-deleted",
    async (IOutboxPublisher publisher) =>
    {
        var message = new EntityDeleted(Guid.NewGuid());
        await publisher.PublishAsync(nameof(EntityDeleted), message);
    }
);

app.Run();
