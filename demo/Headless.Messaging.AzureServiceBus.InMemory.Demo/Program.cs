using Demo;
using Demo.Contracts.DomainEvents;
using Demo.Contracts.IntegrationEvents;
using Headless.Abstractions;
using Headless.Messaging;
using Headless.Messaging.Dashboard;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLogging(l => l.AddConsole());

builder.Services.AddHeadlessMessaging(setup =>
{
    setup.ForMessage<SampleMessage>(message => message.MessageName("messaging.sample.tests").OnBus<SampleSubscriber>());
    setup.UseInMemoryStorage();
    setup.UseAzureServiceBus(asb =>
    {
        asb.ConnectionString = builder.Configuration.GetConnectionString("AzureServiceBus")!;
        asb.CustomHeadersBuilder = (message, serviceProvider) =>
        {
            var longIdGenerator = serviceProvider.GetRequiredService<ILongIdGenerator>();

            return
            [
                new(Headers.MessageId, longIdGenerator.Create().ToString(CultureInfo.InvariantCulture)),
                new(Headers.MessageName, message.Subject),
                new("IsFromSampleProject", "'true'"),
            ];
        };
        asb.SqlFilters = [new("IsFromSampleProjectFilter", "IsFromSampleProject = 'true'")];

        asb.ConfigureCustomProducer<EntityCreatedForIntegration>(cfg =>
            cfg.UseTopic("entity-created").WithSubscription()
        );
        asb.ConfigureCustomProducer<EntityDeletedForIntegration>(cfg =>
            cfg.UseTopic("entity-deleted").WithSubscription()
        );
    });

    setup.UseDashboard(d => d.WithNoAuth());
});

var app = builder.Build();

app.MapGet(
    "/entity-created-for-integration",
    async (IOutboxBus publisher) =>
    {
        var message = new EntityCreatedForIntegration(Guid.NewGuid());
        await publisher.PublishAsync(message, new PublishOptions { MessageName = nameof(EntityCreatedForIntegration) });
    }
);

app.MapGet(
    "/entity-deleted-for-integration",
    async (IOutboxBus publisher) =>
    {
        var message = new EntityDeletedForIntegration(Guid.NewGuid());
        await publisher.PublishAsync(message, new PublishOptions { MessageName = nameof(EntityDeletedForIntegration) });
    }
);

app.MapGet(
    "/entity-created",
    async (IOutboxBus publisher) =>
    {
        var message = new EntityCreated(Guid.NewGuid());
        await publisher.PublishAsync(message, new PublishOptions { MessageName = nameof(EntityCreated) });
    }
);

app.MapGet(
    "/entity-deleted",
    async (IOutboxBus publisher) =>
    {
        var message = new EntityDeleted(Guid.NewGuid());
        await publisher.PublishAsync(message, new PublishOptions { MessageName = nameof(EntityDeleted) });
    }
);

app.Run();
