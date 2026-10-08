// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Nats;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using INatsConnectionPool = Headless.Messaging.Nats.INatsConnectionPool;
using MsOptions = Microsoft.Extensions.Options;

namespace Tests;

public sealed class NatsStreamWarmupTests : TestBase
{
    [Fact]
    public async Task should_not_touch_the_broker_when_no_owned_stream_is_declared()
    {
        // given
        var options = new NatsMessagingOptions();
        options.Streams.Bind("ORDERS", "headless.bus.orders.>");
        var pool = Substitute.For<INatsConnectionPool>();
        await using var warmup = _CreateWarmup(options, pool, Substitute.For<ILogger<NatsStreamWarmup>>());

        // when
        await warmup.StartAsync(AbortToken);
        await warmup.DisposeAsync();

        // then
        pool.DidNotReceive().GetConnection();
    }

    [Fact]
    public async Task should_log_and_not_fail_host_start_when_the_warm_up_fails()
    {
        // given - the connection is not usable yet
        var options = new NatsMessagingOptions();
        options.Streams.Own("ORDERS", stream => stream.Subjects("headless.bus.orders.>"));
        var pool = Substitute.For<INatsConnectionPool>();
        pool.GetConnection().Returns(_ => throw new InvalidOperationException("not connected"));
        var logger = Substitute.For<ILogger<NatsStreamWarmup>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        var warmup = _CreateWarmup(options, pool, logger);

        // when
        var start = () => warmup.StartAsync(AbortToken).AsTask();
        await start.Should().NotThrowAsync();
        await warmup.DisposeAsync();

        // then - DisposeAsync joined the background warm-up, so its failure is already logged
        logger
            .ReceivedCalls()
            .Should()
            .Contain(call =>
                string.Equals(call.GetMethodInfo().Name, nameof(ILogger.Log), StringComparison.Ordinal)
                && (LogLevel)call.GetArguments()[0]! == LogLevel.Warning
            );
    }

    private static NatsStreamWarmup _CreateWarmup(
        NatsMessagingOptions options,
        INatsConnectionPool pool,
        ILogger<NatsStreamWarmup> logger
    ) => new(new NatsStreamProvisioner(MsOptions.Options.Create(options)), pool, logger);
}
