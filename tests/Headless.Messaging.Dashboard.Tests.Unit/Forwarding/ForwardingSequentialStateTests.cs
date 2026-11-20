// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.DistributedLocks;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Tests.Module;

namespace Tests.Forwarding;

/// <summary>
/// The consumer view's read of a sequential consumer: the host that holds its subscription, found through the lock
/// store and the nodes discovery lists, and the waiting count that host reports; or why the holder is unknown.
/// </summary>
public sealed class ForwardingSequentialStateTests : TestBase
{
    private const string _Path = "/headless/api/messaging/consumers/sequential";

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
    private readonly PeerNetwork _network = new();
    private readonly StaticDiscovery _discoveryA = new();
    private readonly RecordingOperatorLog _log = new();
    private readonly IDistributedLock _locks = Substitute.For<IDistributedLock>();

    private static readonly string _Resource = MessagingKeys.SequentialIntakeResource(
        "v1",
        MessageLane.Bus,
        LedgerModule.Sequential
    );

    public ForwardingSequentialStateTests()
    {
        _discoveryA.Set(ForwardingHost.Entry("node-b", "https://node-b"));

        // No host in these tests wins the lock itself: each test says who holds it.
        _locks
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>())
            .Returns((IDistributedLease?)null);
    }

    [Fact]
    public async Task should_name_node_b_and_its_waiting_count_when_node_a_serves_and_node_b_holds_the_lease()
    {
        // given
        await using var nodeB = await _StartAsync("node-b", new StaticDiscovery());
        await using var nodeA = await _StartAsync("node-a", _discoveryA);
        _Hold(nodeB, "lease-b");
        _LockedBy("lease-b");

        // when
        var state = await _ReadAsync(nodeA, LedgerModule.Sequential);

        // then
        state.GetProperty("intake").GetString().Should().Be("lock");
        var holder = state.GetProperty("holders").EnumerateArray().Should().ContainSingle().Subject;
        holder.GetProperty("subscription").GetString().Should().Be(LedgerModule.Sequential);
        holder.GetProperty("state").GetString().Should().Be("held");
        holder.GetProperty("node").GetString().Should().Be("node-b");
        holder.GetProperty("waiting").GetInt32().Should().Be(0);
        _HoldCalls("node-b").Should().Be(1);
    }

    [Fact]
    public async Task should_name_node_a_without_asking_another_node_when_node_a_holds_the_lease()
    {
        // given
        await using var nodeB = await _StartAsync("node-b", new StaticDiscovery());
        await using var nodeA = await _StartAsync("node-a", _discoveryA);
        _Hold(nodeA, "lease-a");
        _LockedBy("lease-a");

        // when
        var holder = _Holder(await _ReadAsync(nodeA, LedgerModule.Sequential));

        // then
        holder.GetProperty("state").GetString().Should().Be("held");
        holder.GetProperty("node").GetString().Should().Be("node-a");
        _HoldCalls("node-b").Should().Be(0);
    }

    [Fact]
    public async Task should_say_none_when_no_host_holds_the_lock()
    {
        // given
        await using var nodeA = await _StartAsync("node-a", _discoveryA);
        _locks.GetLockInfoAsync(_Resource, Arg.Any<CancellationToken>()).Returns((DistributedLockInfo?)null);

        // when
        var holder = _Holder(await _ReadAsync(nodeA, LedgerModule.Sequential));

        // then
        holder.GetProperty("state").GetString().Should().Be("none");
        holder.TryGetProperty("node", out _).Should().BeFalse();
    }

    [Fact]
    public async Task should_say_unavailable_not_none_when_the_lock_store_fails()
    {
        // given
        await using var nodeA = await _StartAsync("node-a", _discoveryA);
        _locks
            .GetLockInfoAsync(_Resource, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<DistributedLockInfo?>(new InvalidOperationException("store down")));

        // when
        var holder = _Holder(await _ReadAsync(nodeA, LedgerModule.Sequential));

        // then
        holder.GetProperty("state").GetString().Should().Be("unavailable");
        holder.GetProperty("reason").GetString().Should().Be("The lock store could not be read.");
    }

    [Fact]
    public async Task should_say_unavailable_when_the_lease_is_held_but_no_listed_node_claims_it()
    {
        // given: node B answers but holds nothing, and node C is listed but does not answer
        await using var nodeB = await _StartAsync("node-b", new StaticDiscovery());
        _discoveryA.Set(
            ForwardingHost.Entry("node-b", "https://node-b"),
            ForwardingHost.Entry("node-c", "https://node-c")
        );
        await using var nodeA = await _StartAsync("node-a", _discoveryA);
        _LockedBy("lease-elsewhere");

        // when
        var holder = _Holder(await _ReadAsync(nodeA, LedgerModule.Sequential));

        // then
        holder.GetProperty("state").GetString().Should().Be("unavailable");
        holder
            .GetProperty("reason")
            .GetString()
            .Should()
            .Be("Another host holds it, and a node that may hold it did not answer.");
    }

    [Fact]
    public async Task should_answer_404_for_a_consumer_that_is_not_sequential()
    {
        // given
        await using var nodeA = await _StartAsync("node-a", _discoveryA);
        using var client = MessagingCommandHost.Client(nodeA);
        var cookie = await MessagingCommandHost.SignInAsync(client, "viewer", AbortToken);

        // when
        using var response = await MessagingCommandHost.GetAsync(
            client,
            $"{_Path}?identity={LedgerModule.Concurrent}&lane=bus",
            cookie,
            AbortToken
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task should_report_the_serving_nodes_waiting_count_and_no_holder_when_no_lock_elects_a_host()
    {
        // given: without a messaging lock every host listens, so none holds the subscription
        await using var nodeA = await ForwardingHost.StartAsync(
            "node-a",
            _network,
            _discoveryA,
            _time,
            _log,
            AbortToken,
            messaging: LedgerModule.Setup
        );
        await _StartedAsync(nodeA);

        // when
        var state = await _ReadAsync(nodeA, LedgerModule.Sequential);

        // then
        state.GetProperty("intake").GetString().Should().Be("none");
        state.GetProperty("holders").EnumerateArray().Should().BeEmpty();
        state.GetProperty("waiting").GetInt32().Should().Be(0);
    }

    // Messaging starts in the background after the host does; a node reports a waiting count once its dispatcher runs.
    private static Task _StartedAsync(WebApplication node) =>
        node.Services.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);

    private void _LockedBy(string leaseId) =>
        _locks
            .GetLockInfoAsync(_Resource, Arg.Any<CancellationToken>())
            .Returns(new DistributedLockInfo { Resource = _Resource, LeaseId = leaseId });

    private static void _Hold(WebApplication node, string leaseId) =>
        node.Services.GetRequiredService<SequentialIntakeHolds>().Hold(_Resource, leaseId);

    private static JsonElement _Holder(JsonElement state) =>
        state.GetProperty("holders").EnumerateArray().Should().ContainSingle().Subject;

    private int _HoldCalls(string host) =>
        _network.Calls.Count(call =>
            string.Equals(call.Uri.Host, host, StringComparison.Ordinal)
            && call.Uri.AbsolutePath.EndsWith("/peer/sequential", StringComparison.Ordinal)
        );

    private async Task<JsonElement> _ReadAsync(WebApplication node, string identity)
    {
        using var client = MessagingCommandHost.Client(node);
        var cookie = await MessagingCommandHost.SignInAsync(client, "viewer", AbortToken);
        using var response = await MessagingCommandHost.GetAsync(
            client,
            $"{_Path}?identity={Uri.EscapeDataString(identity)}&lane=bus",
            cookie,
            AbortToken
        );
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return await MessagingCommandHost.ReadAsync(response, AbortToken);
    }

    private async Task<WebApplication> _StartAsync(string name, StaticDiscovery discovery)
    {
        var app = await ForwardingHost.StartAsync(
            name,
            _network,
            discovery,
            _time,
            _log,
            AbortToken,
            configure: services => services.AddKeyedSingleton(MessagingKeys.LockProvider, _locks),
            messaging: LedgerModule.Setup
        );
        _network.Add(name, app);
        await _StartedAsync(app);

        return app;
    }

    public sealed record LedgerPosted(string Account);

    public sealed class LedgerConsumer : IConsume<LedgerPosted>
    {
        public ValueTask ConsumeAsync(ConsumeContext<LedgerPosted> context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    public sealed class LedgerAuditConsumer : IConsume<LedgerPosted>
    {
        public ValueTask ConsumeAsync(ConsumeContext<LedgerPosted> context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private sealed class LedgerModule : IMessagingModule
    {
        public const string Sequential = "ledger.sequential";
        public const string Concurrent = "ledger.concurrent";

        public static void Setup(MessagingSetupBuilder setup)
        {
            // The in-memory store keeps its inbox in the process, which a durable consumer must accept by name.
            setup.Options.MinimumInboxGuarantee = InboxGuarantee.ProcessLocal;
            setup.AddModule<LedgerModule>().Tune(Sequential, consumer => consumer.Sequential());
        }

        public static void Register(MessagingCatalogBuilder catalog)
        {
            catalog.AddBusConsumer<LedgerConsumer, LedgerPosted>(
                Sequential,
                everyInstance: false,
                static (_, _, _) => ValueTask.CompletedTask
            );
            catalog.AddBusConsumer<LedgerAuditConsumer, LedgerPosted>(
                Concurrent,
                everyInstance: false,
                static (_, _, _) => ValueTask.CompletedTask
            );
        }
    }
}
