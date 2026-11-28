// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Hosting;
using Headless.Messaging;
using Headless.Messaging.Redis;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.Options;
using NSubstitute.ExceptionExtensions;
using StackExchange.Redis;

namespace Tests;

public sealed class RedisTopologyProvisionerTests : TestBase
{
    private const string _OrdersStream = "headless:messaging:queue:orders";
    private const string _InvoicesStream = "headless:messaging:queue:invoices";

    // The host consumes "orders" on the Queue lane and "paid" on the Bus lane as "billing", and only publishes "invoices".
    private static readonly MessagingTopology _Topology = new(
        [
            new TopologySubscription("orders", MessageLane.Queue, ConsumerSubscriptionKind.Competing, ["orders"]),
            new TopologySubscription("billing", MessageLane.Bus, ConsumerSubscriptionKind.Competing, ["paid"]),
            new TopologySubscription("cache", MessageLane.Bus, ConsumerSubscriptionKind.EveryInstance, ["evicted"]),
        ],
        [
            new TopologyMessage("orders", MessageLane.Queue),
            new TopologyMessage("invoices", MessageLane.Queue),
            new TopologyMessage("paid", MessageLane.Bus),
        ]
    );

    private readonly IDatabase _database = Substitute.For<IDatabase>();
    private readonly IBatch _batch = Substitute.For<IBatch>();

    public RedisTopologyProvisionerTests()
    {
        _database.CreateBatch(Arg.Any<object?>()).Returns(_batch);
    }

    [Fact]
    public void should_report_the_public_kind_constant()
    {
        // when
        var kind = _CreateSut().Kind;

        // then
        // The value is what an operator passes to 'resources provision -k', so the constant must not change it.
        kind.Should().Be(SetupMessagingRedis.TopologyResourceKind).And.Be("RedisStreams");
    }

    [Fact]
    public void should_own_the_groups_this_host_consumes_through_and_skip_every_instance_subscriptions()
    {
        // when
        var groups = RedisTopologyProvisioner.Groups(_Topology);

        // then - a publisher-only Queue message gets its group so entries wait for the consumer, a Bus-only message none
        groups
            .Select(group => (group.Stream, group.Group, group.Owned))
            .Should()
            .Equal(
                ("headless:messaging:bus:paid", "billing", true),
                (_InvoicesStream, "invoices", false),
                (_OrdersStream, "orders", true)
            );
    }

    [Fact]
    public void should_apply_the_host_prefix_to_the_stream_keys()
    {
        // when
        var groups = RedisTopologyProvisioner.Groups(_Topology, "wt1");

        // then
        groups
            .Should()
            .OnlyContain(group => group.Stream.StartsWith("wt1:headless:messaging:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task should_create_each_group_with_its_stream_and_accept_one_that_already_exists()
    {
        // given a group another process created first
        _database
            .StreamCreateConsumerGroupAsync(
                _OrdersStream,
                "orders",
                Arg.Any<RedisValue?>(),
                Arg.Any<bool>(),
                Arg.Any<CommandFlags>()
            )
            .ThrowsAsync(_ServerException("BUSYGROUP Consumer Group name already exists"));

        // when provisioned twice
        await _CreateSut().ProvisionAsync(_Topology, AbortToken);
        await _CreateSut().ProvisionAsync(_Topology, AbortToken);

        // then
        foreach (var (stream, group) in _AllGroups())
        {
            await _database
                .Received(2)
                .StreamCreateConsumerGroupAsync(
                    stream,
                    group,
                    StreamPosition.NewMessages,
                    true,
                    Arg.Any<CommandFlags>()
                );
        }

        await _database
            .DidNotReceive()
            .StreamCreateConsumerGroupAsync(
                "headless:messaging:bus:evicted",
                Arg.Any<RedisValue>(),
                Arg.Any<RedisValue?>(),
                Arg.Any<bool>(),
                Arg.Any<CommandFlags>()
            );
    }

    [Fact]
    public async Task should_fail_provisioning_on_a_refusal_other_than_an_existing_group()
    {
        // given
        _database
            .StreamCreateConsumerGroupAsync(
                Arg.Any<RedisKey>(),
                Arg.Any<RedisValue>(),
                Arg.Any<RedisValue?>(),
                Arg.Any<bool>(),
                Arg.Any<CommandFlags>()
            )
            .ThrowsAsync(_ServerException("NOPERM this user has no permissions to run the 'xgroup|create' command"));

        // when
        var act = () => _CreateSut().ProvisionAsync(_Topology, AbortToken);

        // then
        await act.Should().ThrowExactlyAsync<RedisServerException>();
    }

    [Fact]
    public async Task should_verify_every_stream_in_one_batch_without_writing()
    {
        // given
        _GivenGroups(
            ("headless:messaging:bus:paid", ["billing", "shipping"]),
            (_InvoicesStream, ["invoices"]),
            (_OrdersStream, ["orders"])
        );

        // when
        await _CreateSut().VerifyAsync(_Topology, AbortToken);

        // then
        _database.Received(1).CreateBatch(Arg.Any<object?>());
        _batch.Received(1).Execute();
        await _database.DidNotReceive().StreamGroupInfoAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>());
        await _database
            .DidNotReceive()
            .StreamCreateConsumerGroupAsync(
                Arg.Any<RedisKey>(),
                Arg.Any<RedisValue>(),
                Arg.Any<RedisValue?>(),
                Arg.Any<bool>(),
                Arg.Any<CommandFlags>()
            );
    }

    [Fact]
    public async Task should_fail_verification_naming_resources_provision_when_a_stream_is_absent()
    {
        // given the orders stream was never created
        _GivenGroups(("headless:messaging:bus:paid", ["billing"]), (_InvoicesStream, ["invoices"]));
        _batch
            .StreamGroupInfoAsync(_OrdersStream, Arg.Any<CommandFlags>())
            .ThrowsAsync(_ServerException("ERR no such key"));

        // when
        var act = () => _CreateSut().VerifyAsync(_Topology, AbortToken);

        // then
        (await act.Should().ThrowExactlyAsync<ResourceProvisioningException>())
            .Which.Message.Should()
            .Contain("'orders'")
            .And.Contain($"'{_OrdersStream}'")
            .And.Contain("resources provision");
    }

    [Fact]
    public async Task should_fail_verification_when_the_stream_lacks_this_hosts_group()
    {
        // given another identity's group on the shared Bus stream, but not billing's
        _GivenGroups(
            ("headless:messaging:bus:paid", ["shipping"]),
            (_InvoicesStream, ["invoices"]),
            (_OrdersStream, ["orders"])
        );

        // when
        var act = () => _CreateSut().VerifyAsync(_Topology, AbortToken);

        // then
        (await act.Should().ThrowExactlyAsync<ResourceProvisioningException>())
            .Which.Message.Should()
            .Contain("'billing'");
    }

    [Fact]
    public async Task should_skip_the_check_when_the_topology_needs_no_group()
    {
        // when
        await _CreateSut().VerifyAsync(MessagingTopology.Empty, AbortToken);

        // then
        _database.DidNotReceive().CreateBatch(Arg.Any<object?>());
    }

    [Fact]
    public async Task should_reset_only_the_owned_groups_and_trim_only_queue_streams_when_purging()
    {
        // given
        _GivenScriptReturns(1);

        // when
        await _CreateSut().PurgeAsync(_Topology, AbortToken);

        // then - the shared Bus stream keeps its entries for the other identities; the publisher-only group is untouched
        var calls = _ScriptCalls();
        calls
            .Should()
            .Equal(
                ("headless:messaging:bus:paid", (RedisValue)"billing", (RedisValue)0),
                (_OrdersStream, (RedisValue)"orders", (RedisValue)1)
            );
    }

    [Fact]
    public async Task should_fail_purging_a_group_that_does_not_exist()
    {
        // given
        _GivenScriptReturns(0);

        // when
        var act = () => _CreateSut().PurgeAsync(_Topology, AbortToken);

        // then
        await act.Should().ThrowExactlyAsync<ResourceProvisioningException>();
    }

    [Fact]
    public async Task should_delete_only_the_owned_groups()
    {
        // given
        _GivenScriptReturns(0);

        // when, twice, as deleting what is gone succeeds
        await _CreateSut().DeleteAsync(_Topology, AbortToken);
        await _CreateSut().DeleteAsync(_Topology, AbortToken);

        // then
        _ScriptCalls()
            .Select(call => (call.Stream, call.Group))
            .Should()
            .Equal(
                ("headless:messaging:bus:paid", (RedisValue)"billing"),
                (_OrdersStream, (RedisValue)"orders"),
                ("headless:messaging:bus:paid", (RedisValue)"billing"),
                (_OrdersStream, (RedisValue)"orders")
            );
    }

    [Fact]
    public async Task should_report_each_groups_consumers_and_pending_entries_and_a_missing_one()
    {
        // given
        _GivenGroups(("headless:messaging:bus:paid", ["billing"]), (_InvoicesStream, []));
        _batch
            .StreamGroupInfoAsync(_OrdersStream, Arg.Any<CommandFlags>())
            .Returns(Task.FromResult(new[] { _GroupInfo("orders", consumers: 2, pending: 3) }));

        // when
        var status = await _CreateSut().GetStatusAsync(_Topology, AbortToken);

        // then
        status
            .Should()
            .Equal(
                new KeyValuePair<string, string>("headless:messaging:bus:paid billing", "0 consumers, 0 pending"),
                new KeyValuePair<string, string>($"{_InvoicesStream} invoices", "missing"),
                new KeyValuePair<string, string>($"{_OrdersStream} orders", "2 consumers, 3 pending")
            );
    }

    private static IEnumerable<(string Stream, string Group)> _AllGroups()
    {
        yield return ("headless:messaging:bus:paid", "billing");
        yield return (_InvoicesStream, "invoices");
        yield return (_OrdersStream, "orders");
    }

    private void _GivenGroups(params (string Stream, string[] Groups)[] streams)
    {
        foreach (var (stream, groups) in streams)
        {
            _batch
                .StreamGroupInfoAsync(stream, Arg.Any<CommandFlags>())
                .Returns(Task.FromResult(groups.Select(group => _GroupInfo(group)).ToArray()));
        }
    }

    private void _GivenScriptReturns(long result)
    {
        _database
            .ScriptEvaluateAsync(
                Arg.Any<string>(),
                Arg.Any<RedisKey[]?>(),
                Arg.Any<RedisValue[]?>(),
                Arg.Any<CommandFlags>()
            )
            .Returns(Task.FromResult(RedisResult.Create(result)));
    }

    private List<(string Stream, RedisValue Group, RedisValue Trim)> _ScriptCalls()
    {
        return
        [
            .. _database
                .ReceivedCalls()
                .Where(call =>
                    string.Equals(
                        call.GetMethodInfo().Name,
                        nameof(IDatabaseAsync.ScriptEvaluateAsync),
                        StringComparison.Ordinal
                    )
                )
                .Select(call =>
                {
                    var keys = (RedisKey[])call.GetArguments()[1]!;
                    var values = (RedisValue[])call.GetArguments()[2]!;

                    return (keys[0].ToString(), values[0], values.Length > 1 ? values[1] : RedisValue.Null);
                }),
        ];
    }

    // StackExchange.Redis builds StreamGroupInfo only from a server reply, so the test fills its internal constructor.
    private static StreamGroupInfo _GroupInfo(string name, int consumers = 0, int pending = 0)
    {
        var constructor = typeof(StreamGroupInfo)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .OrderByDescending(ctor => ctor.GetParameters().Length)
            .First();

        var arguments = constructor
            .GetParameters()
            .Select(parameter =>
                parameter.Name switch
                {
                    "name" => name,
                    "consumerCount" => consumers,
                    "pendingMessageCount" => pending,
                    _ => parameter.ParameterType.IsValueType ? Activator.CreateInstance(parameter.ParameterType) : null,
                }
            )
            .ToArray();

        return (StreamGroupInfo)constructor.Invoke(arguments);
    }

    private static RedisServerException _ServerException(string message)
    {
#pragma warning disable CS0618 // The replacement constructor needs the experimental RedisErrorKind; keep this one until it exits preview.
        return new RedisServerException(message);
#pragma warning restore CS0618
    }

    private RedisTopologyProvisioner _CreateSut()
    {
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(_database);
        var pool = Substitute.For<IRedisConnectionPool>();
        pool.ConnectAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(multiplexer));

        return new RedisTopologyProvisioner(
            pool,
            Options.Create(new RedisMessagingOptions { Configuration = ConfigurationOptions.Parse("localhost:6379") })
        );
    }
}
