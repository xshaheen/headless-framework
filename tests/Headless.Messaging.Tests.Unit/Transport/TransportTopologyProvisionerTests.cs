// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Messaging;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tests.Registration;

namespace Tests.Transport;

/// <summary>
/// A transport's <see cref="ITransportTopologyProvisioner"/> as a provisioned resource: one resource per transport, fed the
/// topology the registered consumers and messages describe.
/// </summary>
public sealed class TransportTopologyProvisionerTests : TestBase
{
    [Fact]
    public async Task should_offer_one_resource_per_provisioner_and_pass_it_the_registered_topology()
    {
        // given
        var provisioner = new RecordingProvisioner();
        await using var provider = _Build(provisioner);

        // when
        var resource = (await _ResourcesAsync(provider)).Should().ContainSingle().Which;
        await resource.ProvisionAsync(AbortToken);

        // then
        resource.Kind.Should().Be("Recording");
        resource.Name.Should().Be("topology");
        var topology = provisioner.Calls.Should().ContainSingle().Which.Topology;
        // A Bus subscription is the consumer identity; a Queue subscription is the message it consumes.
        topology
            .Subscriptions.Where(s => s.Lane == MessageLane.Bus)
            .Select(s => (s.SubscriptionName, s.Kind))
            .Should()
            .BeEquivalentTo([
                (TestConsumers.InvoiceProjection, ConsumerSubscriptionKind.Competing),
                (TestConsumers.PriceCache, ConsumerSubscriptionKind.EveryInstance),
            ]);
        var queue = topology.Subscriptions.Should().ContainSingle(s => s.Lane == MessageLane.Queue).Which;
        queue.MessageNames.Should().Equal(queue.SubscriptionName);
        topology.Subscriptions.Should().AllSatisfy(s => s.MessageNames.Should().NotBeEmpty());
    }

    [Fact]
    public async Task should_report_the_provisioner_capabilities_with_purge_and_delete_supported_by_default()
    {
        await using var defaults = _Build(new RecordingProvisioner());
        await using var limited = _Build(new LimitedProvisioner());

        var supported = (await _ResourcesAsync(defaults)).Single();
        var unsupported = (await _ResourcesAsync(limited)).Single();

        supported.CanPurge.Should().BeTrue();
        supported.CanDelete.Should().BeTrue();
        unsupported.CanPurge.Should().BeFalse();
        unsupported.CanDelete.Should().BeFalse();
    }

    [Fact]
    public async Task should_route_every_operation_to_the_provisioner()
    {
        var provisioner = new RecordingProvisioner();
        await using var provider = _Build(provisioner);
        var resource = (await _ResourcesAsync(provider)).Single();

        await resource.VerifyAsync(AbortToken);
        await resource.PurgeAsync(AbortToken);
        await resource.DeleteAsync(AbortToken);
        var status = await resource.GetStatusAsync(AbortToken);

        provisioner.Calls.Select(c => c.Operation).Should().Equal("verify", "purge", "delete", "status");
        status.Should().ContainSingle().Which.Key.Should().Be("queues");
    }

    [Fact]
    public async Task should_offer_no_topology_resource_without_a_provisioner()
    {
        await using var provider = _Build(provisioner: null);

        (await _ResourcesAsync(provider)).Should().BeEmpty();
    }

    private static ServiceProvider _Build(RecordingProvisioner? provisioner)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<HostControlProbe>();

        if (provisioner is not null)
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<ITransportTopologyProvisioner>(provisioner));
        }

        services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            setup.AddModule<BillingModule>().AddModule<BillingPriceCacheModule>().AddModule<BillingQueueModule>();
        });

        return services.BuildServiceProvider();
    }

    // Only the transport topology resources: the in-memory storage registers none of its own.
    private static async Task<List<IResourceProvisioner>> _ResourcesAsync(IServiceProvider provider)
    {
        var resources = new List<IResourceProvisioner>();

        foreach (var source in provider.GetServices<IResourceProvisionerSource>())
        {
            resources.AddRange(await source.GetProvisionersAsync(AbortToken));
        }

        return resources;
    }

    private class RecordingProvisioner : ITransportTopologyProvisioner
    {
        public List<(string Operation, MessagingTopology Topology)> Calls { get; } = [];

        public string Kind => "Recording";

        public Task ProvisionAsync(MessagingTopology topology, CancellationToken cancellationToken) =>
            _Record("provision", topology);

        public Task VerifyAsync(MessagingTopology topology, CancellationToken cancellationToken) =>
            _Record("verify", topology);

        public Task PurgeAsync(MessagingTopology topology, CancellationToken cancellationToken) =>
            _Record("purge", topology);

        public Task DeleteAsync(MessagingTopology topology, CancellationToken cancellationToken) =>
            _Record("delete", topology);

        public async Task<IReadOnlyList<KeyValuePair<string, string>>> GetStatusAsync(
            MessagingTopology topology,
            CancellationToken cancellationToken
        )
        {
            await _Record("status", topology);

            return [new("queues", "3")];
        }

        private Task _Record(string operation, MessagingTopology topology)
        {
            Calls.Add((operation, topology));

            return Task.CompletedTask;
        }
    }

    // Re-implements the interface so the capability flags override the defaults the base class inherits.
    private sealed class LimitedProvisioner : RecordingProvisioner, ITransportTopologyProvisioner
    {
        public bool CanPurge => false;

        public bool CanDelete => false;
    }
}
