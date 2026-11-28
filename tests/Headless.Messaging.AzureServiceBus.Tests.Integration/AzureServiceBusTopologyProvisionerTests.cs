// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Headless.Hosting;
using Headless.Messaging;
using Headless.Messaging.AzureServiceBus;
using Headless.Messaging.AzureServiceBus.Helpers;
using Headless.Messaging.Persistence;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tests.Helpers;

namespace Tests;

/// <summary>
/// The shared Azure Service Bus topology as a deploy-time resource on the emulator: provision creates what the
/// registered consumers and Queue messages need, verify reads it without writing, purge empties it, delete removes it,
/// and a host at its defaults creates nothing and holds readiness until the deployment provisions.
/// </summary>
/// <remarks>
/// The emulator's AMQP connection string reaches no administration endpoint, so the transport's client pool is given an
/// administration client on the emulator's management port, as a real namespace's connection string carries both.
/// </remarks>
[Collection("AzureServiceBusEmulator")]
public sealed class AzureServiceBusTopologyProvisionerTests(AzureServiceBusEmulatorFixture fixture) : TestBase
{
    private const string _Identity = "tests.asb.topology";

    [Fact]
    public async Task should_provision_verify_purge_and_delete_the_shared_topology()
    {
        // given
        AzureServiceBusEmulatorFixture.RequireEulaAccepted();
        var (provider, topic, queue) = _Build();
        await using var _ = provider;
        var administration = new ServiceBusAdministrationClient(fixture.AdministrationConnectionString);
        var subscription = AzureServiceBusConsumerClientFactory.BusSubscriptionName(_Identity);

        try
        {
            var topology = (await _ResourcesAsync(provider))
                .Should()
                .ContainSingle(r => r.Kind == SetupMessagingAzureServiceBus.TopologyResourceKind)
                .Which;
            topology.Name.Should().Be("topology");

            // then — verify reads a namespace that lacks the topology and changes nothing in it
            await FluentActions
                .Awaiting(() => topology.VerifyAsync(AbortToken))
                .Should()
                .ThrowAsync<ResourceProvisioningException>()
                .WithMessage($"*topic '{topic}' does not exist*Run 'resources provision'*");
            (await administration.TopicExistsAsync(topic, AbortToken)).Value.Should().BeFalse();

            // when
            await provider.ProvisionResourcesAsync(AbortToken);

            // then
            (await administration.TopicExistsAsync(topic, AbortToken))
                .Value.Should()
                .BeTrue();
            (await administration.QueueExistsAsync(queue, AbortToken)).Value.Should().BeTrue();
            var rules = new List<string>();
            await foreach (var rule in administration.GetRulesAsync(topic, subscription, AbortToken))
            {
                rules.Add(rule.Name);
            }

            rules.Should().Equal(queue + ".bus");
            await topology.VerifyAsync(AbortToken);
            await provider.ProvisionResourcesAsync(AbortToken);

            // and when — a message waits in the queue, then the state is cleared
            await using (var client = new ServiceBusClient(fixture.ConnectionString))
            await using (var sender = client.CreateSender(queue))
            {
                await sender.SendMessageAsync(new ServiceBusMessage("waiting"), AbortToken);
            }

            (await administration.GetQueueRuntimePropertiesAsync(queue, AbortToken))
                .Value.ActiveMessageCount.Should()
                .Be(1);
            await provider.PurgeResourcesAsync(AbortToken);

            // then
            (await administration.GetQueueRuntimePropertiesAsync(queue, AbortToken))
                .Value.ActiveMessageCount.Should()
                .Be(0);

            // and when
            await provider.DeleteResourcesAsync(AbortToken);

            // then — the owned entities go and the shared topic stays
            (await administration.QueueExistsAsync(queue, AbortToken))
                .Value.Should()
                .BeFalse();
            (await administration.SubscriptionExistsAsync(topic, subscription, AbortToken)).Value.Should().BeFalse();
            (await administration.TopicExistsAsync(topic, AbortToken))
                .Value.Should()
                .BeTrue("every service on the namespace shares the topic, so teardown keeps it");
            await FluentActions
                .Awaiting(() => topology.VerifyAsync(AbortToken))
                .Should()
                .ThrowAsync<ResourceProvisioningException>()
                .WithMessage("*does not exist*Run 'resources provision'*");
        }
        finally
        {
            await _DeleteAsync(administration, topic, queue);
        }
    }

    [Fact]
    public async Task should_create_nothing_at_start_and_hold_readiness_until_resources_provision_runs()
    {
        // given
        AzureServiceBusEmulatorFixture.RequireEulaAccepted();
        var (provider, topic, queue) = _Build();
        await using var _ = provider;
        var administration = new ServiceBusAdministrationClient(fixture.AdministrationConnectionString);
        var warmup = provider.GetServices<IWarmup>().Should().ContainSingle(w => w.Name == "Resources").Which;

        try
        {
            // when — the host starts its consumers at its defaults
            await provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);

            // then — nothing was created, and the readiness warm-up names what is missing
            (await administration.TopicExistsAsync(topic, AbortToken))
                .Value.Should()
                .BeFalse();
            (await administration.QueueExistsAsync(queue, AbortToken)).Value.Should().BeFalse();
            await FluentActions
                .Awaiting(() => warmup.WarmUpAsync(AbortToken))
                .Should()
                .ThrowAsync<ResourceProvisioningException>()
                .WithMessage($"*topic '{topic}' does not exist*queue '{queue}' does not exist*");

            // and when — the deployment creates the topology
            await provider.ProvisionResourcesAsync(AbortToken);

            // then
            await warmup.WarmUpAsync(AbortToken);
        }
        finally
        {
            await _DeleteAsync(administration, topic, queue);
        }
    }

    private (ServiceProvider Provider, string Topic, string Queue) _Build()
    {
        var topic = $"topology-{Guid.NewGuid():N}";
        var queue = $"topology-{Guid.NewGuid():N}";
        var connectionString = fixture.ConnectionString;
        var administrationConnectionString = fixture.AdministrationConnectionString;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<OrderConsumer>();
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseAzureServiceBus(options =>
            {
                options.ConnectionString = connectionString;
                options.TopicPath = topic;
            });
            setup.UseInMemoryStorage();
            setup.Options.MinimumInboxGuarantee = InboxGuarantee.ProcessLocal;
        });
        services.ConfigureMessaging(messaging =>
        {
            messaging.Message<Order>(queue, "1");
            messaging.Message<Shipment>(queue + ".bus", "1");
            messaging.AddModule<OrderModule>();
        });

        // Registered last, so it replaces the transport's pool.
        services.AddSingleton<IAzureServiceBusClientPool>(sp => new AzureServiceBusClientPool(
            sp.GetRequiredService<ILogger<AzureServiceBusClientPool>>(),
            sp.GetRequiredService<IOptions<AzureServiceBusMessagingOptions>>(),
            ServiceBusHelpers.CreateClient,
            _ => new ServiceBusAdministrationClient(administrationConnectionString)
        ));

        return (services.BuildServiceProvider(), topic, queue);
    }

    private static async Task<List<IResourceProvisioner>> _ResourcesAsync(IServiceProvider provider)
    {
        var resources = new List<IResourceProvisioner>();

        foreach (var source in provider.GetServices<IResourceProvisionerSource>())
        {
            resources.AddRange(await source.GetProvisionersAsync(AbortToken));
        }

        return resources;
    }

    // The emulator keeps entities across tests of a run, and it holds only a few dozen.
    private static async Task _DeleteAsync(ServiceBusAdministrationClient administration, string topic, string queue)
    {
        foreach (
            var delete in new Func<Task<Azure.Response>>[]
            {
                () => administration.DeleteTopicAsync(topic, CancellationToken.None),
                () => administration.DeleteQueueAsync(queue, CancellationToken.None),
            }
        )
        {
            try
            {
                await delete();
            }
            catch (ServiceBusException e) when (e.Reason is ServiceBusFailureReason.MessagingEntityNotFound)
            {
                // Never created, or deleted by the test.
            }
        }
    }

    public sealed record Order(string Value);

    public sealed record Shipment(string Value);

    public sealed class OrderModule : IMessagingModule
    {
        public static void Register(MessagingCatalogBuilder catalog)
        {
            catalog.AddQueueConsumer<OrderConsumer, Order>(
                "tests.asb.topology.queue",
                TestConsumerDispatch.FromServices<OrderConsumer, Order>()
            );
            catalog.AddBusConsumer<OrderConsumer, Shipment>(
                _Identity,
                everyInstance: false,
                TestConsumerDispatch.FromServices<OrderConsumer, Shipment>()
            );
        }
    }

    public sealed class OrderConsumer : IConsume<Order>, IConsume<Shipment>
    {
        public ValueTask ConsumeAsync(ConsumeContext<Order> context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask ConsumeAsync(ConsumeContext<Shipment> context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }
}
