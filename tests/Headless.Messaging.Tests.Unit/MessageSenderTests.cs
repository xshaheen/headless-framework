// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.Persistence;
using Headless.Messaging.Retry;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Tests.Guarantees;

#pragma warning disable MA0015 // Specify the parameter name in ArgumentException
namespace Tests;

public sealed class MessageSenderTests : TestBase
{
    private static readonly IServiceProvider _EmptyScope = new ServiceCollection().BuildServiceProvider();

    // Four attempts of one message (three inline retries), each failing the way the transport is told to.
    private MessageSender _CreateFailingSender(TransportConnectivity connectivity, Func<OperateResult> send)
    {
        var transportMessage = _CreateTransportMessage();
        var serializer = Substitute.For<IMessageSerializer>();
        serializer
            .SerializeToTransportMessageAsync(Arg.Any<Message>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(transportMessage));

        var transport = Substitute.For<ITransport>();
        transport.BrokerAddress.Returns(new BrokerAddress("Test", "localhost"));
        transport.SendAsync(transportMessage, Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult(send()));

        return _CreateSender(
            Substitute.For<IDataStorage>(),
            serializer,
            transport,
            new MessagingOptions
            {
                RetryPolicy = { RetryStrategy = TestRetryStrategies.ZeroDelay(3), MaxPersistedRetries = 0 },
            },
            connectivity: connectivity
        );
    }

    private static MediumMessage _CreateMediumMessage(
        MessageLane lane = MessageLane.Bus,
        string name = "test.messageName"
    )
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = Guid.NewGuid().ToString(),
            [Headers.MessageName] = name,
        };

        return new MediumMessage
        {
            StorageId = Guid.NewGuid(),
            Origin = new Message(headers, "{}"),
            Content = "{}",
            Lane = lane,
            Added = DateTimeOffset.UtcNow,
        };
    }

    private static TransportMessage _CreateTransportMessage()
    {
        return new TransportMessage(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageId] = Guid.NewGuid().ToString(),
                [Headers.MessageName] = "test.messageName",
            },
            ReadOnlyMemory<byte>.Empty
        );
    }

    [Fact]
    public async Task should_reject_stored_affinity_when_current_configuration_has_no_mapping_before_resolving_transport()
    {
        var transportFactoryCalls = 0;
        var services = new ServiceCollection();
        services.AddLogging();
        var storage = Substitute.For<IDataStorage>();
        services.AddSingleton(storage);
        services.AddSingleton<IMessageSerializer>(new JsonUtf8Serializer(Options.Create(new MessagingOptions())));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(new MessagingOptions()));
        services.AddSingleton(new TransportConnectivity());
        services.AddSingleton<IMessageCapabilityGate>(
            MessagingCapabilityModel.Compose([
                MessagingProviderCapabilities.Transport("Kafka", [MessageLane.Queue], false),
            ])
        );
        services.AddSingleton<IQueueTransport>(_ =>
        {
            transportFactoryCalls++;
            return Substitute.For<IQueueTransport>();
        });
        await using var provider = services.BuildServiceProvider();
        var stored = _CreateMediumMessage();
        stored.Lane = MessageLane.Queue;
        stored.Origin.Headers[Headers.RoutingAffinityKey] = "order-42";

        var act = async () =>
        {
            var sender = new MessageSender(provider.GetRequiredService<ILogger<MessageSender>>(), provider);
            await sender.SendAsync(stored);
        };

        await act.Should()
            .ThrowAsync<MessagingConfigurationException>()
            .WithMessage("*affinity*unsupported*unverifiable*");
        transportFactoryCalls.Should().Be(0);
        storage
            .ReceivedCalls()
            .Should()
            .BeEmpty("the invalid stored key must reject before lease or attempt reservation");
    }

    [Fact]
    public async Task should_send_the_transport_prepared_at_publish_without_serializing_again()
    {
        // given a stored message whose publish already serialized the payload
        var storage = Substitute.For<IDataStorage>();
        var serializer = Substitute.For<IMessageSerializer>();
        await using var transport = new CapturingTransport();
        var sender = _CreateSender(storage, serializer, transport, new MessagingOptions());
        var message = _CreateMediumMessage();
        var body = """{"prepared":true}"""u8.ToArray();
        message.PreparedTransport = (message.Origin, new TransportMessage(message.Origin.Headers, body));

        // and a header stamped after the store
        message.Origin.Headers["late-header"] = "after-store";

        // when
        var result = await sender.SendAsync(message);

        // then
        result.Succeeded.Should().BeTrue();
        await serializer
            .DidNotReceive()
            .SerializeToTransportMessageAsync(Arg.Any<Message>(), Arg.Any<CancellationToken>());
        var delivered = transport.Sent.Should().ContainSingle().Subject;
        delivered.Body.ToArray().Should().Equal(body);
        delivered.Headers["late-header"].Should().Be("after-store");
    }

    [Fact]
    public async Task should_serialize_again_when_the_prepared_transport_no_longer_matches_the_origin()
    {
        // given a prepared transport built for an envelope the message no longer carries
        var storage = Substitute.For<IDataStorage>();
        var transportMessage = _CreateTransportMessage();
        var serializer = Substitute.For<IMessageSerializer>();
        serializer
            .SerializeToTransportMessageAsync(Arg.Any<Message>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(transportMessage));
        await using var transport = new CapturingTransport();
        var sender = _CreateSender(storage, serializer, transport, new MessagingOptions());
        var message = _CreateMediumMessage();
        var stale = new Message(
            new Dictionary<string, string?>(message.Origin.Headers, StringComparer.Ordinal),
            "stale"
        );
        message.PreparedTransport = (stale, new TransportMessage(stale.Headers, "\"stale\""u8.ToArray()));

        // when
        var result = await sender.SendAsync(message);

        // then
        result.Succeeded.Should().BeTrue();
        await serializer.Received(1).SerializeToTransportMessageAsync(message.Origin, Arg.Any<CancellationToken>());
        transport.Sent.Should().ContainSingle().Which.Should().Be(transportMessage);
    }

    private sealed class CapturingTransport : ITransport
    {
        public List<TransportMessage> Sent { get; } = [];

        public BrokerAddress BrokerAddress { get; } = new("Test", "localhost");

        public Task<OperateResult> SendAsync(TransportMessage message, CancellationToken cancellationToken = default)
        {
            Sent.Add(message);
            return Task.FromResult(OperateResult.Success);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static MessageSender _CreateSender(
        IDataStorage storage,
        IMessageSerializer serializer,
        ITransport transport,
        MessagingOptions options,
        IHostApplicationLifetime? lifetime = null,
        TransportConnectivity? connectivity = null,
        TimeProvider? timeProvider = null,
        ILoggerProvider? loggerProvider = null,
        TimeProvider? backoffClock = null
    )
    {
        storage
            .LeasePublishAsync(Arg.Any<MediumMessage>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(true));
        storage
            .LeasePublishAndReserveAttemptAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));
        storage
            .ReservePublishAttemptAsync(Arg.Any<MediumMessage>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(true));
        storage
            .ChangePublishStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<MessageContentWrite>(),
                Arg.Any<System.Data.Common.DbTransaction?>(),
                Arg.Any<RetryDelay?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));
        storage
            .ChangePublishRetryStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<MessageContentWrite>(),
                Arg.Any<RetryDelay?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));

        var services = new ServiceCollection();
        services.AddLogging(logging =>
        {
            if (loggerProvider is not null)
            {
                logging.AddProvider(loggerProvider);
            }
        });
        services.AddSingleton(storage);
        services.AddSingleton(serializer);
        services.AddSingleton(transport);
        services.AddSingleton<IBusTransport>(_ => new TestBusTransportAdapter(transport));
        services.AddSingleton<IQueueTransport>(_ => new TestQueueTransportAdapter(transport));
        services.AddSingleton(timeProvider ?? TimeProvider.System);
        if (backoffClock is not null)
        {
            services.AddKeyedSingleton(MessagingOptions.BackoffClockServiceKey, backoffClock);
        }

        services.AddSingleton(Options.Create(options));
        if (lifetime is not null)
        {
            services.AddSingleton(lifetime);
        }

        services.AddSingleton(connectivity ?? new TransportConnectivity());

        var provider = services.BuildServiceProvider();
        return new MessageSender(provider.GetRequiredService<ILogger<MessageSender>>(), provider);
    }

    private static MessageSender _CreateSenderWithTransports(
        IDataStorage storage,
        IMessageSerializer serializer,
        MessagingOptions options,
        IBusTransport? busTransport = null,
        IQueueTransport? queueTransport = null
    )
    {
        storage
            .LeasePublishAsync(Arg.Any<MediumMessage>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(true));
        storage
            .LeasePublishAndReserveAttemptAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));
        storage
            .ReservePublishAttemptAsync(Arg.Any<MediumMessage>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(true));
        storage
            .ChangePublishRetryStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<MessageContentWrite>(),
                Arg.Any<RetryDelay?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(storage);
        services.AddSingleton(serializer);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(options));
        services.AddSingleton(new TransportConnectivity());

        if (busTransport is not null)
        {
            services.AddSingleton(busTransport);
        }

        if (queueTransport is not null)
        {
            services.AddSingleton(queueTransport);
        }

        var provider = services.BuildServiceProvider();
        return new MessageSender(provider.GetRequiredService<ILogger<MessageSender>>(), provider);
    }

    [Fact]
    public async Task should_report_the_broker_unreachable_after_consecutive_send_failures_and_reachable_after_the_next_success()
    {
        // given a publish-only host whose transport fails three sends in a row, then accepts the fourth
        var storage = Substitute.For<IDataStorage>();
        var transportMessage = _CreateTransportMessage();
        var serializer = Substitute.For<IMessageSerializer>();
        serializer
            .SerializeToTransportMessageAsync(Arg.Any<Message>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(transportMessage));

        var connectivity = new TransportConnectivity();
        var reachableAtEachSend = new List<bool>();
        var transport = Substitute.For<ITransport>();
        transport.BrokerAddress.Returns(new BrokerAddress("Test", "localhost"));
        transport
            .SendAsync(transportMessage, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                reachableAtEachSend.Add(connectivity.IsReachable);
                return Task.FromResult(
                    reachableAtEachSend.Count <= 3
                        ? OperateResult.BrokerUnreachable(new PublisherSentFailedException("connection refused"))
                        : OperateResult.Success
                );
            });

        var sender = _CreateSender(
            storage,
            serializer,
            transport,
            new MessagingOptions
            {
                RetryPolicy = { RetryStrategy = TestRetryStrategies.ZeroDelay(3), MaxPersistedRetries = 0 },
            },
            connectivity: connectivity
        );

        // when
        var result = await sender.SendAsync(_CreateMediumMessage());

        // then
        result.Succeeded.Should().BeTrue();
        reachableAtEachSend.Should().Equal(true, true, true, false);
        connectivity.IsReachable.Should().BeTrue("the fourth send reached the broker");
    }

    [Fact]
    public async Task should_stay_reachable_when_the_broker_refuses_every_send()
    {
        // given a broker that answers but refuses the message, as for a missing topic or an oversized message
        var connectivity = new TransportConnectivity();
        var sender = _CreateFailingSender(
            connectivity,
            () => OperateResult.Failed(new PublisherSentFailedException("topic not found"))
        );

        // when one message fails every inline retry
        var result = await sender.SendAsync(_CreateMediumMessage());

        // then
        result.Succeeded.Should().BeFalse();
        connectivity.IsReachable.Should().BeTrue("a refused message says nothing about whether the broker is up");
    }

    [Fact]
    public async Task should_count_a_thrown_socket_failure_toward_the_broker_being_unreachable()
    {
        // given a transport that throws a socket failure instead of returning a result
        var connectivity = new TransportConnectivity();
        var sender = _CreateFailingSender(
            connectivity,
            () => throw new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.ConnectionRefused)
        );

        // when - a throwing send propagates after one attempt, so three messages reach the threshold
        for (var i = 0; i < TransportConnectivity.FailureThreshold; i++)
        {
            var send = () => sender.SendAsync(_CreateMediumMessage());
            await send.Should().ThrowAsync<System.Net.Sockets.SocketException>();
        }

        // then
        connectivity.IsReachable.Should().BeFalse();
    }

    [Fact]
    public async Task should_report_the_broker_unreachable_when_consecutive_sends_time_out()
    {
        // given a transport that never answers, so every send runs into the publish timeout
        var storage = Substitute.For<IDataStorage>();
        var transportMessage = _CreateTransportMessage();
        var serializer = Substitute.For<IMessageSerializer>();
        serializer
            .SerializeToTransportMessageAsync(Arg.Any<Message>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(transportMessage));

        var connectivity = new TransportConnectivity();
        var transport = Substitute.For<ITransport>();
        transport.BrokerAddress.Returns(new BrokerAddress("Test", "localhost"));
        transport
            .SendAsync(transportMessage, Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                await Task.Delay(Timeout.Infinite, callInfo.Arg<CancellationToken>());
                return OperateResult.Success;
            });

        var sender = _CreateSender(
            storage,
            serializer,
            transport,
            new MessagingOptions
            {
                TransportPublishTimeout = TimeSpan.FromMilliseconds(20),
                RetryPolicy = { RetryStrategy = TestRetryStrategies.ZeroDelay(0), MaxPersistedRetries = 0 },
            },
            connectivity: connectivity
        );

        // when three messages each run into the timeout
        for (var i = 0; i < 3; i++)
        {
            var result = await sender.SendAsync(_CreateMediumMessage());
            result.Exception.Should().BeOfType<TimeoutException>();
        }

        // then
        connectivity.IsReachable.Should().BeFalse();
    }

    [Fact]
    public async Task should_not_count_serialization_failures_as_broker_failures()
    {
        // given a message the serializer rejects on every attempt
        var storage = Substitute.For<IDataStorage>();
        var serializer = Substitute.For<IMessageSerializer>();
        serializer
            .SerializeToTransportMessageAsync(Arg.Any<Message>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask<TransportMessage>>(_ => throw new System.Text.Json.JsonException("not serializable"));

        var connectivity = new TransportConnectivity();
        var transport = Substitute.For<ITransport>();
        transport.BrokerAddress.Returns(new BrokerAddress("Test", "localhost"));

        var sender = _CreateSender(
            storage,
            serializer,
            transport,
            new MessagingOptions
            {
                RetryPolicy = { RetryStrategy = TestRetryStrategies.ZeroDelay(0), MaxPersistedRetries = 0 },
            },
            connectivity: connectivity
        );

        // when three messages each fail to serialize
        for (var i = 0; i < 3; i++)
        {
            var send = () => sender.SendAsync(_CreateMediumMessage());
            await send.Should().ThrowAsync<System.Text.Json.JsonException>();
        }

        // then
        connectivity.IsReachable.Should().BeTrue("the broker was never contacted");
    }

    [Fact]
    public async Task should_honor_failed_retry_count_above_three()
    {
        // given
        var storage = Substitute.For<IDataStorage>();
        storage
            .ChangePublishRetryStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<MessageContentWrite>(),
                Arg.Any<RetryDelay?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));

        var transportMessage = _CreateTransportMessage();
        var serializer = Substitute.For<IMessageSerializer>();
        serializer
            .SerializeToTransportMessageAsync(Arg.Any<Message>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(transportMessage));

        var transport = Substitute.For<ITransport>();
        transport.BrokerAddress.Returns(new BrokerAddress("Test", "localhost"));
        var attempts = 0;
        transport
            .SendAsync(transportMessage, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                attempts++;
                if (attempts <= 4)
                {
                    return Task.FromResult(OperateResult.Failed(new TimeoutException("boom")));
                }

                return Task.FromResult(OperateResult.Success);
            });

        var sender = _CreateSender(
            storage,
            serializer,
            transport,
            new MessagingOptions
            {
                RetryPolicy = { RetryStrategy = TestRetryStrategies.ZeroDelay(4), MaxPersistedRetries = 0 },
            }
        );

        // when
        var executionState = new RetryExecutionState();
        var result = await sender.SendRetryAsync(_CreateMediumMessage(), _EmptyScope, executionState);

        // then
        result.Succeeded.Should().BeTrue();
        executionState.LeaseClearedByTransition.Should().BeTrue();
        attempts.Should().Be(5);
    }

    [Fact]
    public async Task should_apply_backoff_delay_before_retrying()
    {
        // given
        var storage = Substitute.For<IDataStorage>();
        storage
            .ChangePublishStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<MessageContentWrite>(),
                Arg.Any<System.Data.Common.DbTransaction?>(),
                Arg.Any<RetryDelay?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));

        var transportMessage = _CreateTransportMessage();
        var serializer = Substitute.For<IMessageSerializer>();
        serializer
            .SerializeToTransportMessageAsync(Arg.Any<Message>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(transportMessage));

        var transport = Substitute.For<ITransport>();
        transport.BrokerAddress.Returns(new BrokerAddress("Test", "localhost"));
        var attempts = 0;
        transport
            .SendAsync(transportMessage, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                attempts++;
                if (attempts == 1)
                {
                    return Task.FromResult(OperateResult.Failed(new TimeoutException("boom")));
                }

                return Task.FromResult(OperateResult.Success);
            });

        var sender = _CreateSender(
            storage,
            serializer,
            transport,
            new MessagingOptions
            {
                RetryPolicy =
                {
                    RetryStrategy = TestRetryStrategies.FixedDelay(1, TimeSpan.FromMilliseconds(40)),
                    MaxPersistedRetries = 0,
                },
            }
        );

        var stopwatch = Stopwatch.StartNew();

        // when
        var result = await sender.SendAsync(_CreateMediumMessage());

        // then
        stopwatch.Stop();
        result.Succeeded.Should().BeTrue();
        attempts.Should().Be(2);
        stopwatch.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(30));
    }

    [Fact]
    public async Task should_persist_delayed_retry_in_single_failed_state_update_when_inline_budget_exhausts()
    {
        // given
        var storage = Substitute.For<IDataStorage>();
        storage
            .ChangePublishStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<MessageContentWrite>(),
                Arg.Any<System.Data.Common.DbTransaction?>(),
                Arg.Any<RetryDelay?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));

        var transportMessage = _CreateTransportMessage();
        var serializer = Substitute.For<IMessageSerializer>();
        serializer
            .SerializeToTransportMessageAsync(Arg.Any<Message>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(transportMessage));

        var transport = Substitute.For<ITransport>();
        transport.BrokerAddress.Returns(new BrokerAddress("Test", "localhost"));
        transport
            .SendAsync(transportMessage, Arg.Any<CancellationToken>())
            .Returns(OperateResult.Failed(new TimeoutException("boom")));

        var sender = _CreateSender(
            storage,
            serializer,
            transport,
            new MessagingOptions
            {
                RetryPolicy =
                {
                    RetryStrategy = TestRetryStrategies.FixedDelay(0, TimeSpan.FromSeconds(5)),
                    MaxPersistedRetries = 1,
                },
            }
        );

        // when
        var executionState = new RetryExecutionState();
        var result = await sender.SendRetryAsync(_CreateMediumMessage(), _EmptyScope, executionState);

        // then
        result.Succeeded.Should().BeFalse();
        executionState.LeaseClearedByTransition.Should().BeTrue();
        await storage
            .Received(1)
            .ChangePublishRetryStateAsync(
                Arg.Any<MediumMessage>(),
                StatusName.Failed,
                Arg.Any<MessageContentWrite>(),
                Arg.Is<RetryDelay?>(value => value.HasValue),
                Arg.Any<DateTimeOffset?>(),
                Arg.Is<int>(value => value == 0),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_persist_failed_and_report_permanent_failure_when_exception_is_permanent()
    {
        // given — strategy classifies the exception as permanent (returns Stop). The sender
        // must persist Failed with nextRetryAt=null (no future retry scheduled) and report the
        // row to OnTerminal as a permanent failure: the budget was never spent, but the row ended.
        var storage = Substitute.For<IDataStorage>();
        storage
            .ChangePublishStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<MessageContentWrite>(),
                Arg.Any<System.Data.Common.DbTransaction?>(),
                Arg.Any<RetryDelay?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));

        var transportMessage = _CreateTransportMessage();
        var serializer = Substitute.For<IMessageSerializer>();
        serializer
            .SerializeToTransportMessageAsync(Arg.Any<Message>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(transportMessage));

        var transport = Substitute.For<ITransport>();
        transport.BrokerAddress.Returns(new BrokerAddress("Test", "localhost"));
        transport
            .SendAsync(transportMessage, Arg.Any<CancellationToken>())
            // ReSharper disable once NotResolvedInText
            .Returns(OperateResult.Failed(new ArgumentNullException("param")));

        MessageTerminalReason? reason = null;
        var sender = _CreateSender(
            storage,
            serializer,
            transport,
            new MessagingOptions
            {
                RetryPolicy =
                {
                    RetryStrategy = TestRetryStrategies.PermanentArgument(0),
                    MaxPersistedRetries = 0,
                    OnTerminal = (info, _) =>
                    {
                        reason = info.Reason;
                        return Task.CompletedTask;
                    },
                },
            }
        );

        // when
        var result = await sender.SendAsync(_CreateMediumMessage());

        // then — Stop classification: persisted as Failed with null nextRetryAt, reported as permanent.
        result.Succeeded.Should().BeFalse();
        reason.Should().Be(MessageTerminalReason.PermanentFailure);
        await storage
            .Received()
            .ChangePublishRetryStateAsync(
                Arg.Any<MediumMessage>(),
                StatusName.Failed,
                Arg.Any<MessageContentWrite>(),
                Arg.Is<RetryDelay?>(v => v == null),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_store_the_exception_text_and_a_fingerprint_with_a_terminal_publish_failure()
    {
        // given — two broker failures of different causes, thrown for real so each carries the stack the fingerprint
        // hashes
        var failures = new Queue<Exception>([_CatchBrokerRefusal("orders-7731"), _CatchBrokerTimeout()]);
        var storage = Substitute.For<IDataStorage>();
        var transportMessage = _CreateTransportMessage();
        var serializer = Substitute.For<IMessageSerializer>();
        serializer
            .SerializeToTransportMessageAsync(Arg.Any<Message>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(transportMessage));
        var transport = Substitute.For<ITransport>();
        transport.BrokerAddress.Returns(new BrokerAddress("Test", "localhost"));
        transport
            .SendAsync(transportMessage, Arg.Any<CancellationToken>())
            .Returns(_ => OperateResult.Failed(failures.Dequeue()));
        var sender = _CreateSender(
            storage,
            serializer,
            transport,
            new MessagingOptions
            {
                RetryPolicy = { RetryStrategy = TestRetryStrategies.ZeroDelay(0), MaxPersistedRetries = 0 },
            }
        );
        var written = new List<(StatusName Status, string? ExceptionInfo, string? FailureFingerprint)>();
        storage
            .When(x =>
                x.ChangePublishRetryStateAsync(
                    Arg.Any<MediumMessage>(),
                    Arg.Any<StatusName>(),
                    Arg.Any<MessageContentWrite>(),
                    Arg.Any<RetryDelay?>(),
                    Arg.Any<DateTimeOffset?>(),
                    Arg.Any<int>(),
                    Arg.Any<int>(),
                    Arg.Any<CancellationToken>()
                )
            )
            .Do(call =>
            {
                var message = call.ArgAt<MediumMessage>(0);
                written.Add((call.ArgAt<StatusName>(1), message.ExceptionInfo, message.FailureFingerprint));
            });

        // when
        var refused = await sender.SendAsync(_CreateMediumMessage());
        var timedOut = await sender.SendAsync(_CreateMediumMessage());

        // then — each terminal write carries its failure's text and the versioned fingerprint of its own cause
        refused.Succeeded.Should().BeFalse();
        timedOut.Succeeded.Should().BeFalse();
        written.Should().HaveCount(2).And.OnlyContain(write => write.Status == StatusName.Failed);
        written[0].ExceptionInfo.Should().Contain("orders-7731");
        written[0].FailureFingerprint.Should().MatchRegex("^v1:[0-9a-f]{16}$");
        written[1].FailureFingerprint.Should().MatchRegex("^v1:[0-9a-f]{16}$");
        written[1].FailureFingerprint.Should().NotBe(written[0].FailureFingerprint);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Exception _CatchBrokerTimeout()
    {
        try
        {
            throw new TimeoutException("The broker did not acknowledge the publish within 30 s.");
        }
        catch (TimeoutException ex)
        {
#pragma warning disable ERP022 // False positive: the caught exception is the helper's return value, which the test asserts on.
            return ex;
#pragma warning restore ERP022
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Exception _CatchBrokerRefusal(string topic)
    {
        try
        {
            throw new InvalidOperationException($"The broker refused the publish to {topic}.");
        }
        catch (InvalidOperationException ex)
        {
#pragma warning disable ERP022 // False positive: the caught exception is the helper's return value, which the test asserts on.
            return ex;
#pragma warning restore ERP022
        }
    }

    [Fact]
    public async Task should_keep_a_terminal_publish_failure_for_the_failed_retention_from_now_when_it_was_scheduled_far_ahead()
    {
        // A publish scheduled further ahead than the failed retention fails long after the row was
        // stored. The collector purges Failed rows whose ExpiresAt has passed, so the expiry must
        // count from the failure, not from Added.
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
        var storage = Substitute.For<IDataStorage>();
        var transportMessage = _CreateTransportMessage();
        var serializer = Substitute.For<IMessageSerializer>();
        serializer
            .SerializeToTransportMessageAsync(Arg.Any<Message>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(transportMessage));
        var transport = Substitute.For<ITransport>();
        transport.BrokerAddress.Returns(new BrokerAddress("Test", "localhost"));
        transport
            .SendAsync(transportMessage, Arg.Any<CancellationToken>())
            // ReSharper disable once NotResolvedInText
            .Returns(OperateResult.Failed(new ArgumentNullException("param")));
        var options = new MessagingOptions
        {
            RetryPolicy = { RetryStrategy = TestRetryStrategies.PermanentArgument(0), MaxPersistedRetries = 0 },
        };
        var sender = _CreateSender(storage, serializer, transport, options, timeProvider: clock);
        var message = _CreateMediumMessage();
        message.Added = clock.GetUtcNow().AddSeconds(-2L * options.FailedMessageExpiredAfter);

        var result = await sender.SendAsync(message);

        result.Succeeded.Should().BeFalse();
        message.ExpiresAt.Should().Be(clock.GetUtcNow().AddSeconds(options.FailedMessageExpiredAfter));
    }

    [Fact]
    public async Task should_schedule_a_retry_when_the_transport_publish_times_out()
    {
        // given — the broker never answers, so the transport publish timeout cancels the send while the host runs.
        var storage = Substitute.For<IDataStorage>();
        var transportMessage = _CreateTransportMessage();
        var serializer = Substitute.For<IMessageSerializer>();
        serializer
            .SerializeToTransportMessageAsync(Arg.Any<Message>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(transportMessage));

        var sendStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = Substitute.For<ITransport>();
        transport.BrokerAddress.Returns(new BrokerAddress("Test", "localhost"));
        transport
            .SendAsync(transportMessage, Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                sendStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, call.Arg<CancellationToken>());
                return OperateResult.Success;
            });

        var options = new MessagingOptions
        {
            RetryPolicy =
            {
                RetryStrategy = TestRetryStrategies.FixedDelay(0, TimeSpan.FromSeconds(5)),
                MaxPersistedRetries = 1,
            },
        };
        var backoffClock = new FakeTimeProvider();
        var sender = _CreateSender(
            storage,
            serializer,
            transport,
            options,
            new FakeHostApplicationLifetime(AbortToken),
            backoffClock: backoffClock
        );

        // when: the send waits for the broker until the back-off clock passes the publish timeout
        var send = sender.SendAsync(_CreateMediumMessage());
        await sendStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        send.IsCompleted.Should().BeFalse();
        backoffClock.Advance(options.TransportPublishTimeout);
        var result = await send.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // then — a transient failure: the row is scheduled for a later attempt with back-off, not left on its lease.
        result.Succeeded.Should().BeFalse();
        result.Exception.Should().BeOfType<TimeoutException>();
        await storage
            .Received(1)
            .ChangePublishRetryStateAsync(
                Arg.Any<MediumMessage>(),
                StatusName.Failed,
                Arg.Any<MessageContentWrite>(),
                Arg.Is<RetryDelay?>(value => value.HasValue),
                Arg.Any<DateTimeOffset?>(),
                Arg.Is<int>(value => value == 0),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_schedule_a_retry_when_the_publish_times_out_on_the_system_clock_while_the_host_runs()
    {
        // given — no back-off clock is registered, so one source on the system clock carries both the publish timeout
        // and the running host's stopping token.
        var storage = Substitute.For<IDataStorage>();
        var transportMessage = _CreateTransportMessage();
        var serializer = Substitute.For<IMessageSerializer>();
        serializer
            .SerializeToTransportMessageAsync(Arg.Any<Message>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(transportMessage));

        var transport = Substitute.For<ITransport>();
        transport.BrokerAddress.Returns(new BrokerAddress("Test", "localhost"));
        transport
            .SendAsync(transportMessage, Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, call.Arg<CancellationToken>());
                return OperateResult.Success;
            });

        var options = new MessagingOptions
        {
            TransportPublishTimeout = TimeSpan.FromMilliseconds(20),
            RetryPolicy =
            {
                RetryStrategy = TestRetryStrategies.FixedDelay(0, TimeSpan.FromSeconds(5)),
                MaxPersistedRetries = 1,
            },
        };
        var sender = _CreateSender(
            storage,
            serializer,
            transport,
            options,
            new FakeHostApplicationLifetime(AbortToken)
        );

        // when
        var result = await sender.SendAsync(_CreateMediumMessage()).WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // then — the timeout, not shutdown, cut the send off, so the row is scheduled for a later attempt.
        result.Succeeded.Should().BeFalse();
        result.Exception.Should().BeOfType<TimeoutException>();
        await storage
            .Received(1)
            .ChangePublishRetryStateAsync(
                Arg.Any<MediumMessage>(),
                StatusName.Failed,
                Arg.Any<MessageContentWrite>(),
                Arg.Is<RetryDelay?>(value => value.HasValue),
                Arg.Any<DateTimeOffset?>(),
                Arg.Is<int>(value => value == 0),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_rethrow_a_transport_cancellation_caused_by_shutdown()
    {
        // given — the host is stopping, which cancels the in-flight publish.
        var storage = Substitute.For<IDataStorage>();
        var transportMessage = _CreateTransportMessage();
        var serializer = Substitute.For<IMessageSerializer>();
        serializer
            .SerializeToTransportMessageAsync(Arg.Any<Message>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(transportMessage));

        var transport = Substitute.For<ITransport>();
        transport.BrokerAddress.Returns(new BrokerAddress("Test", "localhost"));
        transport
            .SendAsync(transportMessage, Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, call.Arg<CancellationToken>());
                return OperateResult.Success;
            });

        using var stopping = new CancellationTokenSource();
        await stopping.CancelAsync();
        var sender = _CreateSender(
            storage,
            serializer,
            transport,
            new MessagingOptions
            {
                RetryPolicy =
                {
                    RetryStrategy = TestRetryStrategies.FixedDelay(0, TimeSpan.Zero),
                    MaxPersistedRetries = 1,
                },
            },
            new FakeHostApplicationLifetime(stopping.Token)
        );

        // when
        var act = () => sender.SendAsync(_CreateMediumMessage());

        // then — shutdown is not a failed attempt: nothing is written, and the retry pickup resumes after restart.
        await act.Should().ThrowAsync<OperationCanceledException>();
        await storage
            .DidNotReceive()
            .ChangePublishRetryStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<MessageContentWrite>(),
                Arg.Any<RetryDelay?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_treat_shutdown_oce_as_cancellation_without_writing_state()
    {
        // given — IHostApplicationLifetime.ApplicationStopping is signalled and the transport
        // surfaces an OCE bound to that same token. The sender must classify this as cancellation
        // (non-retryable), NOT invoke OnTerminal, and NOT write a state transition. The row's
        // existing NextRetryAt remains and the persisted retry processor picks it up on restart.
        var storage = Substitute.For<IDataStorage>();
        storage
            .ChangePublishStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<MessageContentWrite>(),
                Arg.Any<System.Data.Common.DbTransaction?>(),
                Arg.Any<RetryDelay?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));

        var transportMessage = _CreateTransportMessage();
        var serializer = Substitute.For<IMessageSerializer>();
        serializer
            .SerializeToTransportMessageAsync(Arg.Any<Message>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(transportMessage));

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var shutdownOce = new OperationCanceledException("App stopping", cts.Token);

        var transport = Substitute.For<ITransport>();
        transport.BrokerAddress.Returns(new BrokerAddress("Test", "localhost"));
        transport.SendAsync(transportMessage, Arg.Any<CancellationToken>()).Returns(OperateResult.Failed(shutdownOce));

        var callbackInvoked = false;
        var sender = _CreateSender(
            storage,
            serializer,
            transport,
            new MessagingOptions
            {
                RetryPolicy =
                {
                    RetryStrategy = TestRetryStrategies.FixedDelay(0, TimeSpan.Zero),
                    MaxPersistedRetries = 4,
                    OnTerminal = (_, _) =>
                    {
                        callbackInvoked = true;
                        return Task.CompletedTask;
                    },
                },
            },
            new FakeHostApplicationLifetime(cts.Token)
        );

        // when
        var result = await sender.SendAsync(_CreateMediumMessage());

        // then — Shutdown OCE: no state write, no OnTerminal. Row keeps prior NextRetryAt/Status.
        result.Succeeded.Should().BeFalse();
        callbackInvoked.Should().BeFalse();
        await storage
            .DidNotReceive()
            .ChangePublishStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<MessageContentWrite>(),
                Arg.Any<System.Data.Common.DbTransaction?>(),
                Arg.Any<RetryDelay?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_skip_on_terminal_when_publish_state_change_reports_already_terminal()
    {
        // given — X1 contract: a broker redelivery (or any path where the row reached terminal
        // state by another worker) means ChangePublishStateAsync's conditional UPDATE matches zero
        // rows and returns false. The sender MUST skip OnTerminal in that case so the callback is
        // not invoked twice for the same logical exhaustion. The Subscribe path has the same
        // guarantee; this test pins the publish-side mirror.
        var storage = Substitute.For<IDataStorage>();
        storage
            .ChangePublishStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<MessageContentWrite>(),
                Arg.Any<System.Data.Common.DbTransaction?>(),
                Arg.Any<RetryDelay?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(false));

        var transportMessage = _CreateTransportMessage();
        var serializer = Substitute.For<IMessageSerializer>();
        serializer
            .SerializeToTransportMessageAsync(Arg.Any<Message>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(transportMessage));

        var transport = Substitute.For<ITransport>();
        transport.BrokerAddress.Returns(new BrokerAddress("Test", "localhost"));
        transport
            .SendAsync(transportMessage, Arg.Any<CancellationToken>())
            .Returns(OperateResult.Failed(new TimeoutException("transient")));

        var callbackInvoked = false;
        var sender = _CreateSender(
            storage,
            serializer,
            transport,
            new MessagingOptions
            {
                RetryPolicy =
                {
                    RetryStrategy = TestRetryStrategies.FixedDelay(0, TimeSpan.Zero),
                    MaxPersistedRetries = 0,
                    OnTerminal = (_, _) =>
                    {
                        callbackInvoked = true;
                        return Task.CompletedTask;
                    },
                },
            }
        );
        storage
            .ChangePublishRetryStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<MessageContentWrite>(),
                Arg.Any<RetryDelay?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(false));

        // when — config forces Exhausted on the first failure
        var result = await sender.SendAsync(_CreateMediumMessage());

        // then — conditional UPDATE returned false: callback skipped despite the Exhausted decision
        result.Succeeded.Should().BeFalse();
        callbackInvoked.Should().BeFalse();
        await storage
            .Received(1)
            .ChangePublishRetryStateAsync(
                Arg.Any<MediumMessage>(),
                StatusName.Failed,
                Arg.Any<MessageContentWrite>(),
                Arg.Is<RetryDelay?>(v => v == null),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            );
    }

    private sealed class FakeHostApplicationLifetime(CancellationToken stoppingToken) : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => stoppingToken;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication() { }
    }

    [Fact]
    public async Task should_resolve_same_scoped_service_as_dispatch_scope_when_on_terminal_callback()
    {
        // given — a Scoped marker service. The Dispatcher creates a per-message scope and
        // passes its IServiceProvider; MessageSender must surface that SAME provider via
        // TerminalMessageInfo.ServiceProvider so OnTerminal sees the live scope.
        var rootServices = new ServiceCollection();
        rootServices.AddScoped<ScopedMarker>();
        var rootProvider = rootServices.BuildServiceProvider();
        using var dispatchScope = rootProvider.CreateScope();
        var expected = dispatchScope.ServiceProvider.GetRequiredService<ScopedMarker>();

        var storage = Substitute.For<IDataStorage>();
        storage
            .ChangePublishStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<MessageContentWrite>(),
                Arg.Any<System.Data.Common.DbTransaction?>(),
                Arg.Any<RetryDelay?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));

        var transportMessage = _CreateTransportMessage();
        var serializer = Substitute.For<IMessageSerializer>();
        serializer
            .SerializeToTransportMessageAsync(Arg.Any<Message>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(transportMessage));

        var transport = Substitute.For<ITransport>();
        transport.BrokerAddress.Returns(new BrokerAddress("Test", "localhost"));
        transport
            .SendAsync(transportMessage, Arg.Any<CancellationToken>())
            .Returns(OperateResult.Failed(new TimeoutException("boom")));

        ScopedMarker? observed = null;
        var sender = _CreateSender(
            storage,
            serializer,
            transport,
            new MessagingOptions
            {
                RetryPolicy =
                {
                    RetryStrategy = TestRetryStrategies.ZeroDelay(0),
                    MaxPersistedRetries = 0,
                    OnTerminal = (info, _) =>
                    {
                        observed = info.ServiceProvider.GetRequiredService<ScopedMarker>();
                        return Task.CompletedTask;
                    },
                },
            }
        );

        // when
        await sender.SendAsync(_CreateMediumMessage(), dispatchScope.ServiceProvider);

        // then — same scope means same Scoped instance
        observed.Should().NotBeNull();
        observed.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task should_stop_without_publishing_when_lease_rejects_terminal_row()
    {
        // given — lease returns false (storage proves the row is terminal). Sender must short-circuit
        // without invoking the transport and without writing any state.
        var storage = Substitute.For<IDataStorage>();
        var serializer = Substitute.For<IMessageSerializer>();
        var transport = Substitute.For<ITransport>();
        transport.BrokerAddress.Returns(new BrokerAddress("Test", "localhost"));

        var sender = _CreateSender(
            storage,
            serializer,
            transport,
            new MessagingOptions
            {
                RetryPolicy = { RetryStrategy = TestRetryStrategies.ZeroDelay(0), MaxPersistedRetries = 0 },
            }
        );

        // Override the happy-path stub from _CreateSender so the fresh-dispatch combined
        // lease+reserve write reports lease contention.
        storage
            .LeasePublishAndReserveAttemptAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(false));

        // when
        var result = await sender.SendAsync(_CreateMediumMessage());

        // then — transport publish, serialization, and state writes must all be skipped.
        // Use ReceivedCalls() rather than DidNotReceive() because NSubstitute's argument matcher
        // invokes TransportMessage.Equals during call enumeration, which throws on default record
        // values without first being populated.
        result.Succeeded.Should().BeTrue();
        transport.ReceivedCalls().Should().BeEmpty();
        serializer.ReceivedCalls().Should().BeEmpty();
        storage.ReceivedCalls().Select(c => c.GetMethodInfo().Name).Should().NotContain("ChangePublishStateAsync");
    }

    [Fact]
    public async Task should_dispatch_bus_message_through_bus_transport_only()
    {
        // given
        var storage = Substitute.For<IDataStorage>();
        storage
            .ChangePublishStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<MessageContentWrite>(),
                Arg.Any<System.Data.Common.DbTransaction?>(),
                Arg.Any<RetryDelay?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));

        var transportMessage = _CreateTransportMessage();
        var serializer = Substitute.For<IMessageSerializer>();
        serializer
            .SerializeToTransportMessageAsync(Arg.Any<Message>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(transportMessage));

        var busTransport = Substitute.For<IBusTransport>();
        busTransport.BrokerAddress.Returns(new BrokerAddress("bus", "localhost"));
        busTransport.SendAsync(transportMessage, Arg.Any<CancellationToken>()).Returns(OperateResult.Success);

        var queueTransport = Substitute.For<IQueueTransport>();
        queueTransport.BrokerAddress.Returns(new BrokerAddress("queue", "localhost"));

        var sender = _CreateSenderWithTransports(
            storage,
            serializer,
            new MessagingOptions(),
            busTransport,
            queueTransport
        );

        // when
        var result = await sender.SendAsync(_CreateMediumMessage(MessageLane.Bus));

        // then
        result.Succeeded.Should().BeTrue();
        busTransport
            .ReceivedCalls()
            .Count(call =>
                string.Equals(call.GetMethodInfo().Name, nameof(IBusTransport.SendAsync), StringComparison.Ordinal)
            )
            .Should()
            .Be(1);
        queueTransport
            .ReceivedCalls()
            .Count(call =>
                string.Equals(call.GetMethodInfo().Name, nameof(IQueueTransport.SendAsync), StringComparison.Ordinal)
            )
            .Should()
            .Be(0);
    }

    [Fact]
    public async Task should_dispatch_queue_message_through_queue_transport_only()
    {
        // given
        var storage = Substitute.For<IDataStorage>();
        storage
            .ChangePublishStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<MessageContentWrite>(),
                Arg.Any<System.Data.Common.DbTransaction?>(),
                Arg.Any<RetryDelay?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));

        var transportMessage = _CreateTransportMessage();
        var serializer = Substitute.For<IMessageSerializer>();
        serializer
            .SerializeToTransportMessageAsync(Arg.Any<Message>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(transportMessage));

        var busTransport = Substitute.For<IBusTransport>();
        busTransport.BrokerAddress.Returns(new BrokerAddress("bus", "localhost"));

        var queueTransport = Substitute.For<IQueueTransport>();
        queueTransport.BrokerAddress.Returns(new BrokerAddress("queue", "localhost"));
        queueTransport.SendAsync(transportMessage, Arg.Any<CancellationToken>()).Returns(OperateResult.Success);

        var sender = _CreateSenderWithTransports(
            storage,
            serializer,
            new MessagingOptions(),
            busTransport,
            queueTransport
        );

        // when
        var result = await sender.SendAsync(_CreateMediumMessage(MessageLane.Queue));

        // then
        result.Succeeded.Should().BeTrue();
        queueTransport
            .ReceivedCalls()
            .Count(call =>
                string.Equals(call.GetMethodInfo().Name, nameof(IQueueTransport.SendAsync), StringComparison.Ordinal)
            )
            .Should()
            .Be(1);
        busTransport
            .ReceivedCalls()
            .Count(call =>
                string.Equals(call.GetMethodInfo().Name, nameof(IBusTransport.SendAsync), StringComparison.Ordinal)
            )
            .Should()
            .Be(0);
    }

    [Fact]
    public async Task should_mark_row_terminal_failed_when_stored_intent_is_invalid()
    {
        // given
        var storage = Substitute.For<IDataStorage>();
        storage
            .ChangePublishRetryStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<MessageContentWrite>(),
                Arg.Any<RetryDelay?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));

        var serializer = Substitute.For<IMessageSerializer>();
        serializer
            .SerializeToTransportMessageAsync(Arg.Any<Message>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(_CreateTransportMessage()));

        var sender = _CreateSenderWithTransports(storage, serializer, new MessagingOptions());
        var message = _CreateMediumMessage((MessageLane)42);
        message.LockedUntil = DateTimeOffset.UnixEpoch.AddMinutes(1);
        message.Owner = "store-owner";

        // when
        var result = await sender.SendAsync(message);

        // then
        result.Succeeded.Should().BeFalse();
        await storage
            .Received(1)
            .ChangePublishRetryStateAsync(
                message,
                StatusName.Failed,
                Arg.Any<MessageContentWrite>(),
                null,
                null,
                message.Retries,
                message.InlineAttempts,
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_mark_row_terminal_failed_when_bus_transport_is_missing()
    {
        // given
        var storage = Substitute.For<IDataStorage>();
        storage
            .ChangePublishRetryStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<MessageContentWrite>(),
                Arg.Any<RetryDelay?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));

        var serializer = Substitute.For<IMessageSerializer>();
        serializer
            .SerializeToTransportMessageAsync(Arg.Any<Message>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(_CreateTransportMessage()));

        var queueTransport = Substitute.For<IQueueTransport>();
        queueTransport.BrokerAddress.Returns(new BrokerAddress("queue", "localhost"));

        var sender = _CreateSenderWithTransports(
            storage,
            serializer,
            new MessagingOptions(),
            queueTransport: queueTransport
        );
        var message = _CreateMediumMessage(MessageLane.Bus);
        message.LockedUntil = DateTimeOffset.UnixEpoch.AddMinutes(1);
        message.Owner = "store-owner";

        // when
        var result = await sender.SendAsync(message);

        // then
        result.Succeeded.Should().BeFalse();
        await storage
            .Received(1)
            .ChangePublishRetryStateAsync(
                message,
                StatusName.Failed,
                Arg.Any<MessageContentWrite>(),
                null,
                null,
                message.Retries,
                message.InlineAttempts,
                Arg.Any<CancellationToken>()
            );
        queueTransport
            .ReceivedCalls()
            .Count(call =>
                string.Equals(call.GetMethodInfo().Name, nameof(IQueueTransport.SendAsync), StringComparison.Ordinal)
            )
            .Should()
            .Be(0);
    }

    [Fact]
    public async Task should_mark_row_terminal_failed_when_queue_transport_is_missing()
    {
        // given
        var storage = Substitute.For<IDataStorage>();
        storage
            .ChangePublishRetryStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<MessageContentWrite>(),
                Arg.Any<RetryDelay?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));

        var serializer = Substitute.For<IMessageSerializer>();
        serializer
            .SerializeToTransportMessageAsync(Arg.Any<Message>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(_CreateTransportMessage()));

        var busTransport = Substitute.For<IBusTransport>();
        busTransport.BrokerAddress.Returns(new BrokerAddress("bus", "localhost"));

        var sender = _CreateSenderWithTransports(storage, serializer, new MessagingOptions(), busTransport);
        var message = _CreateMediumMessage(MessageLane.Queue);
        message.LockedUntil = DateTimeOffset.UnixEpoch.AddMinutes(1);
        message.Owner = "store-owner";

        // when
        var result = await sender.SendAsync(message);

        // then
        result.Succeeded.Should().BeFalse();
        await storage
            .Received(1)
            .ChangePublishRetryStateAsync(
                message,
                StatusName.Failed,
                Arg.Any<MessageContentWrite>(),
                null,
                null,
                message.Retries,
                message.InlineAttempts,
                Arg.Any<CancellationToken>()
            );
        busTransport
            .ReceivedCalls()
            .Count(call =>
                string.Equals(call.GetMethodInfo().Name, nameof(IBusTransport.SendAsync), StringComparison.Ordinal)
            )
            .Should()
            .Be(0);
    }

    private sealed class TestBusTransportAdapter(ITransport transport) : IBusTransport
    {
        public BrokerAddress BrokerAddress => transport.BrokerAddress;

        public Task<OperateResult> SendAsync(TransportMessage message, CancellationToken cancellationToken = default)
        {
            return transport.SendAsync(message, cancellationToken);
        }

        public ValueTask DisposeAsync()
        {
            return transport.DisposeAsync();
        }
    }

    private sealed class TestQueueTransportAdapter(ITransport transport) : IQueueTransport
    {
        public BrokerAddress BrokerAddress => transport.BrokerAddress;

        public Task<OperateResult> SendAsync(TransportMessage message, CancellationToken cancellationToken = default)
        {
            return transport.SendAsync(message, cancellationToken);
        }

        public ValueTask DisposeAsync()
        {
            return transport.DisposeAsync();
        }
    }

    private sealed class ScopedMarker;

    [Fact]
    public async Task should_not_invoke_transport_when_crash_recovery_finds_reserved_inline_budget_consumed()
    {
        var storage = Substitute.For<IDataStorage>();
        storage
            .ChangePublishStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<MessageContentWrite>(),
                Arg.Any<System.Data.Common.DbTransaction?>(),
                Arg.Any<RetryDelay?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));
        var serializer = Substitute.For<IMessageSerializer>();
        var transport = Substitute.For<ITransport>();
        var options = new MessagingOptions
        {
            RetryPolicy =
            {
                RetryStrategy = TestRetryStrategies.ZeroDelay(2),
                MaxPersistedRetries = 1,
                DispatchTimeout = TimeSpan.FromSeconds(17),
            },
        };
        var sender = _CreateSender(storage, serializer, transport, options);
        var message = _CreateMediumMessage();
        message.InlineAttempts = 3;

        await sender.SendAsync(message);

        await transport.DidNotReceiveWithAnyArgs().SendAsync(default!, AbortToken);
        message.Retries.Should().Be(1);
        message.InlineAttempts.Should().Be(0);
        await storage
            .Received(1)
            .ChangePublishRetryStateAsync(
                Arg.Is<MediumMessage>(value => value.Retries == 1 && value.InlineAttempts == 0),
                StatusName.Failed,
                Arg.Any<MessageContentWrite>(),
                Arg.Is<RetryDelay?>(value => value.HasValue),
                Arg.Is<DateTimeOffset?>(value => value == null),
                Arg.Is(0),
                Arg.Is(3),
                Arg.Any<CancellationToken>()
            );
        await storage
            .Received(1)
            .LeasePublishAsync(message, options.RetryPolicy.DispatchTimeout, Arg.Any<CancellationToken>());
        await storage
            .DidNotReceiveWithAnyArgs()
            .LeasePublishAndReserveAttemptAsync(default!, default, default, AbortToken);
        await storage.DidNotReceiveWithAnyArgs().ReservePublishAttemptAsync(default!, default, AbortToken);
    }

    [Fact]
    public async Task should_not_invoke_transport_or_write_state_when_attempt_reservation_is_lost()
    {
        var storage = Substitute.For<IDataStorage>();
        var serializer = Substitute.For<IMessageSerializer>();
        var transport = Substitute.For<ITransport>();
        var sender = _CreateSender(storage, serializer, transport, new MessagingOptions());
        storage
            .ReservePublishAttemptAsync(Arg.Any<MediumMessage>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(false));
        var message = _CreateMediumMessage();
        // Storage already acquired this lease. Its absolute expiry is deliberately behind the
        // application clock: core must reserve under the returned identity without reacquiring.
        message.LockedUntil = DateTimeOffset.UnixEpoch.AddMinutes(1);
        message.Owner = "store-owner";

        var result = await sender.SendAsync(message);

        result.Succeeded.Should().BeTrue();
        message.InlineAttempts.Should().Be(0);
        await serializer.DidNotReceiveWithAnyArgs().SerializeToTransportMessageAsync(default!, AbortToken);
        await transport.DidNotReceiveWithAnyArgs().SendAsync(default!, AbortToken);
        await storage
            .DidNotReceiveWithAnyArgs()
            .ChangePublishRetryStateAsync(default!, default, default, default, default, default, default, AbortToken);
        await storage.DidNotReceiveWithAnyArgs().LeasePublishAsync(default!, default, AbortToken);
        await storage
            .DidNotReceiveWithAnyArgs()
            .LeasePublishAndReserveAttemptAsync(default!, default, default, AbortToken);
    }

    [Fact]
    public async Task should_renew_the_lease_while_a_slow_send_runs()
    {
        // given: a send that outlasts several renewal intervals
        var storage = Substitute.For<IDataStorage>();
        var renewals = 0;
        storage
            .RenewPublishLeaseAsync(Arg.Any<MediumMessage>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Interlocked.Increment(ref renewals);
                return ValueTask.FromResult(true);
            });
        var transportMessage = _CreateTransportMessage();
        var serializer = Substitute.For<IMessageSerializer>();
        serializer
            .SerializeToTransportMessageAsync(Arg.Any<Message>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(transportMessage));
        var transport = Substitute.For<ITransport>();
        transport.BrokerAddress.Returns(new BrokerAddress("Test", "localhost"));
        transport
            .SendAsync(transportMessage, Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                while (Volatile.Read(ref renewals) < 2)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(5), AbortToken);
                }

                return OperateResult.Success;
            });
        var sender = _CreateSender(
            storage,
            serializer,
            transport,
            new MessagingOptions { RetryPolicy = { LeaseRenewalInterval = TimeSpan.FromMilliseconds(20) } }
        );

        // when
        var result = await sender.SendAsync(_CreateMediumMessage());

        // then: the send kept its row and wrote its success
        result.Succeeded.Should().BeTrue();
        await storage
            .Received(1)
            .ChangePublishRetryStateAsync(
                Arg.Any<MediumMessage>(),
                StatusName.Succeeded,
                Arg.Any<MessageContentWrite>(),
                Arg.Any<RetryDelay?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_cancel_the_send_and_write_no_state_when_the_lease_is_lost()
    {
        // given: the first renewal finds the lease gone while the broker has not answered
        var storage = Substitute.For<IDataStorage>();
        storage
            .RenewPublishLeaseAsync(Arg.Any<MediumMessage>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(false));
        var transportMessage = _CreateTransportMessage();
        var serializer = Substitute.For<IMessageSerializer>();
        serializer
            .SerializeToTransportMessageAsync(Arg.Any<Message>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(transportMessage));
        var transport = Substitute.For<ITransport>();
        transport.BrokerAddress.Returns(new BrokerAddress("Test", "localhost"));
        transport
            .SendAsync(transportMessage, Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, call.Arg<CancellationToken>());
                return OperateResult.Success;
            });
        var sender = _CreateSender(
            storage,
            serializer,
            transport,
            new MessagingOptions { RetryPolicy = { LeaseRenewalInterval = TimeSpan.FromMilliseconds(20) } },
            new FakeHostApplicationLifetime(AbortToken)
        );

        // when
        var result = await sender.SendAsync(_CreateMediumMessage());

        // then: another node may own the row now, so nothing is written to it
        result.Succeeded.Should().BeFalse();
        storage
            .ReceivedCalls()
            .Select(call => call.GetMethodInfo().Name)
            .Should()
            .NotContain(name => name.StartsWith("Change", StringComparison.Ordinal));
    }

    [Fact]
    public async Task should_issue_single_combined_storage_write_before_fresh_dispatch()
    {
        // given
        var storage = Substitute.For<IDataStorage>();
        var transportMessage = _CreateTransportMessage();
        var serializer = Substitute.For<IMessageSerializer>();
        serializer
            .SerializeToTransportMessageAsync(Arg.Any<Message>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(transportMessage));
        var transport = Substitute.For<ITransport>();
        transport.BrokerAddress.Returns(new BrokerAddress("Test", "localhost"));
        // Stub with the exact instance - an Arg.Any<TransportMessage> matcher makes NSubstitute
        // invoke TransportMessage.Equals against a default record value, which throws.
        transport.SendAsync(transportMessage, Arg.Any<CancellationToken>()).Returns(OperateResult.Success);
        var options = new MessagingOptions { RetryPolicy = { DispatchTimeout = TimeSpan.FromSeconds(17) } };
        var sender = _CreateSender(storage, serializer, transport, options);

        // when
        var result = await sender.SendAsync(_CreateMediumMessage());

        // then — a fresh (never-leased) dispatch must pay exactly one pre-attempt storage write:
        // the combined lease+reserve statement, never the two-step lease-then-reserve pair.
        result.Succeeded.Should().BeTrue();
        await storage
            .Received(1)
            .LeasePublishAndReserveAttemptAsync(
                Arg.Any<MediumMessage>(),
                options.RetryPolicy.DispatchTimeout,
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            );
        await storage.DidNotReceiveWithAnyArgs().LeasePublishAsync(default!, default, AbortToken);
        await storage.DidNotReceiveWithAnyArgs().ReservePublishAttemptAsync(default!, default, AbortToken);
    }

    [Fact]
    public async Task should_end_over_cap_row_as_exhausted_without_sending_when_retries_exceed_max_persisted_retries()
    {
        // given — the cap was lowered after this row was scheduled, so it already used more retries than allowed
        var storage = Substitute.For<IDataStorage>();
        var serializer = Substitute.For<IMessageSerializer>();
        var transport = Substitute.For<ITransport>();
        var exhaustedCalls = 0;
        var options = new MessagingOptions
        {
            RetryPolicy =
            {
                RetryStrategy = TestRetryStrategies.ZeroDelay(2),
                MaxPersistedRetries = 1,
                OnTerminal = (_, _) =>
                {
                    Interlocked.Increment(ref exhaustedCalls);
                    return Task.CompletedTask;
                },
            },
        };
        var sender = _CreateSender(storage, serializer, transport, options);
        var message = _CreateMediumMessage();
        message.Retries = 3;

        // when
        var result = await sender.SendAsync(message);

        // then
        result.Succeeded.Should().BeFalse();
        exhaustedCalls.Should().Be(1);
        message.Retries.Should().Be(3, "an exhausted row does not consume another persisted retry");
        await transport.DidNotReceiveWithAnyArgs().SendAsync(default!, AbortToken);
        await serializer.DidNotReceiveWithAnyArgs().SerializeToTransportMessageAsync(default!, AbortToken);
        await storage
            .Received(1)
            .ChangePublishRetryStateAsync(
                message,
                StatusName.Failed,
                Arg.Any<MessageContentWrite>(),
                Arg.Is<RetryDelay?>(value => value == null),
                Arg.Is<DateTimeOffset?>(value => value == null),
                3,
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            );
        await storage
            .DidNotReceiveWithAnyArgs()
            .LeasePublishAndReserveAttemptAsync(default!, default, default, AbortToken);
        await storage.DidNotReceiveWithAnyArgs().ReservePublishAttemptAsync(default!, default, AbortToken);
    }

    [Fact]
    public async Task should_cut_off_a_hung_on_terminal_callback_on_the_backoff_clock_while_the_app_clock_is_frozen()
    {
        // given a frozen app clock, the default real back-off clock, and a callback that never returns
        var storage = Substitute.For<IDataStorage>();
        var serializer = Substitute.For<IMessageSerializer>();
        var transport = Substitute.For<ITransport>();
        var callbackEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hung = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = new MessagingOptions
        {
            RetryPolicy =
            {
                RetryStrategy = TestRetryStrategies.ZeroDelay(2),
                MaxPersistedRetries = 1,
                OnTerminalTimeout = TimeSpan.FromMilliseconds(50),
                OnTerminal = (_, _) =>
                {
                    callbackEntered.TrySetResult();
                    return hung.Task;
                },
            },
        };
        var sender = _CreateSender(storage, serializer, transport, options, timeProvider: new FakeTimeProvider());
        var message = _CreateMediumMessage();
        message.Retries = 3;

        try
        {
            // when the over-cap row ends as exhausted
            var send = sender.SendAsync(message);
            await callbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

            // then the send finishes once the timeout passes in real time
            var result = await send.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);
            result.Succeeded.Should().BeFalse();
        }
        finally
        {
            hung.TrySetResult();
        }
    }

    [Fact]
    public async Task should_write_terminal_state_without_inline_backoff_when_over_cap_row_has_inline_budget_left()
    {
        // given — two inline retries remain and each would wait an hour on a clock the test never advances, so the
        // send can only finish if the over-cap row goes terminal without entering the inline burst
        var storage = Substitute.For<IDataStorage>();
        var transportMessage = _CreateTransportMessage();
        var serializer = Substitute.For<IMessageSerializer>();
        serializer
            .SerializeToTransportMessageAsync(Arg.Any<Message>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(transportMessage));
        var transport = Substitute.For<ITransport>();
        transport.BrokerAddress.Returns(new BrokerAddress("Test", "localhost"));
        transport
            .SendAsync(transportMessage, Arg.Any<CancellationToken>())
            .Returns(OperateResult.Failed(new TimeoutException("transient")));
        var options = new MessagingOptions
        {
            RetryPolicy =
            {
                RetryStrategy = TestRetryStrategies.FixedDelay(2, TimeSpan.FromHours(1)),
                MaxPersistedRetries = 1,
                DispatchTimeout = TimeSpan.FromHours(3),
            },
        };
        var sender = _CreateSender(storage, serializer, transport, options, timeProvider: new FakeTimeProvider());
        var message = _CreateMediumMessage();
        message.Retries = 2;
        message.InlineAttempts = 0;

        // when
        var result = await sender.SendAsync(message).WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

        // then
        result.Succeeded.Should().BeFalse();
        await transport.DidNotReceiveWithAnyArgs().SendAsync(default!, AbortToken);
        await storage
            .Received(1)
            .ChangePublishRetryStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<MessageContentWrite>(),
                Arg.Any<RetryDelay?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            );
        await storage
            .Received(1)
            .ChangePublishRetryStateAsync(
                message,
                StatusName.Failed,
                Arg.Any<MessageContentWrite>(),
                Arg.Is<RetryDelay?>(value => value == null),
                Arg.Any<DateTimeOffset?>(),
                2,
                0,
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_still_attempt_the_send_when_retries_equal_max_persisted_retries()
    {
        // given
        var storage = Substitute.For<IDataStorage>();
        var transportMessage = _CreateTransportMessage();
        var serializer = Substitute.For<IMessageSerializer>();
        serializer
            .SerializeToTransportMessageAsync(Arg.Any<Message>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(transportMessage));
        var transport = Substitute.For<ITransport>();
        transport.BrokerAddress.Returns(new BrokerAddress("Test", "localhost"));
        transport.SendAsync(transportMessage, Arg.Any<CancellationToken>()).Returns(OperateResult.Success);
        var options = new MessagingOptions
        {
            RetryPolicy = { RetryStrategy = TestRetryStrategies.ZeroDelay(2), MaxPersistedRetries = 1 },
        };
        var sender = _CreateSender(storage, serializer, transport, options);
        var message = _CreateMediumMessage();
        message.Retries = 1;

        // when
        var result = await sender.SendAsync(message);

        // then
        result.Succeeded.Should().BeTrue();
        await transport.Received(1).SendAsync(transportMessage, Arg.Any<CancellationToken>());
        await storage
            .Received(1)
            .ChangePublishRetryStateAsync(
                message,
                StatusName.Succeeded,
                Arg.Any<MessageContentWrite>(),
                Arg.Any<RetryDelay?>(),
                Arg.Any<DateTimeOffset?>(),
                1,
                1,
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_not_invoke_on_terminal_when_another_sender_already_ended_the_over_cap_row()
    {
        // given — a second sender raced the same over-cap row and its terminal write lost the compare-and-set
        var storage = Substitute.For<IDataStorage>();
        var serializer = Substitute.For<IMessageSerializer>();
        var transport = Substitute.For<ITransport>();
        var exhaustedCalls = 0;
        var options = new MessagingOptions
        {
            RetryPolicy =
            {
                RetryStrategy = TestRetryStrategies.ZeroDelay(2),
                MaxPersistedRetries = 1,
                OnTerminal = (_, _) =>
                {
                    Interlocked.Increment(ref exhaustedCalls);
                    return Task.CompletedTask;
                },
            },
        };
        var sender = _CreateSender(storage, serializer, transport, options);
        storage
            .ChangePublishRetryStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<MessageContentWrite>(),
                Arg.Any<RetryDelay?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(false));
        var message = _CreateMediumMessage();
        message.Retries = 3;

        // when
        var result = await sender.SendAsync(message);

        // then
        result.Succeeded.Should().BeFalse();
        exhaustedCalls.Should().Be(0);
        await transport.DidNotReceiveWithAnyArgs().SendAsync(default!, AbortToken);
    }

    public static TheoryData<string, int> SuccessWriteMissCases =>
        new()
        {
            // The row reached a terminal state before this success write: the existing terminal event.
            { "terminal", 69 },
            // Another claimer holds the row under a different lease.
            { "lease-lost", 4121 },
            // A claimed delayed publish is live with no NextRetryAt, so only its status tells it is not terminal.
            { "queued-lease-lost", 4121 },
            { "missing", 4122 },
        };

    [Theory]
    [MemberData(nameof(SuccessWriteMissCases))]
    public async Task should_log_success_write_miss_by_cause_when_the_fenced_success_write_misses(
        string rowState,
        int expectedEventId
    )
    {
        // given
        var storage = Substitute.For<IDataStorage>();
        var message = _CreateMediumMessage();
        var monitoring = Substitute.For<IMonitoringApi>();
        storage.GetMonitoringApi().Returns(monitoring);
        monitoring
            .GetPublishedMessageAsync(message.StorageId, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(_RowFor(message, rowState)));
        using var log = new CapturingLoggerProvider();
        var (sender, transportMessage, transport) = _CreateSucceedingSenderWithMissedWrite(storage, log);

        // when
        var result = await sender.SendAsync(message);

        // then
        result.Succeeded.Should().BeTrue();
        await transport.Received(1).SendAsync(transportMessage, Arg.Any<CancellationToken>());
        log.Entries.Select(e => e.EventId.Id).Where(id => id is 69 or 4121 or 4122).Should().Equal(expectedEventId);
    }

    [Fact]
    public async Task should_complete_publish_and_log_lease_lost_or_unknown_when_the_diagnostic_read_throws()
    {
        // given
        var storage = Substitute.For<IDataStorage>();
        var message = _CreateMediumMessage();
        var monitoring = Substitute.For<IMonitoringApi>();
        storage.GetMonitoringApi().Returns(monitoring);
        var readFailure = new InvalidOperationException("database unavailable");
        monitoring
            .GetPublishedMessageAsync(message.StorageId, Arg.Any<CancellationToken>())
            .Returns<ValueTask<MediumMessage?>>(_ => throw readFailure);
        using var log = new CapturingLoggerProvider();
        var (sender, _, _) = _CreateSucceedingSenderWithMissedWrite(storage, log);

        // when
        var result = await sender.SendAsync(message);

        // then
        result.Succeeded.Should().BeTrue();
        log.Entries.Should().ContainSingle(e => e.EventId.Id == 4121).Which.Exception.Should().BeSameAs(readFailure);
        log.Entries.Should().NotContain(e => e.EventId.Id == 69 || e.EventId.Id == 4122);
    }

    private static (
        MessageSender Sender,
        TransportMessage TransportMessage,
        ITransport Transport
    ) _CreateSucceedingSenderWithMissedWrite(IDataStorage storage, CapturingLoggerProvider log)
    {
        var transportMessage = _CreateTransportMessage();
        var serializer = Substitute.For<IMessageSerializer>();
        serializer
            .SerializeToTransportMessageAsync(Arg.Any<Message>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(transportMessage));
        var transport = Substitute.For<ITransport>();
        transport.BrokerAddress.Returns(new BrokerAddress("Test", "localhost"));
        transport.SendAsync(transportMessage, Arg.Any<CancellationToken>()).Returns(OperateResult.Success);
        var sender = _CreateSender(storage, serializer, transport, new MessagingOptions(), loggerProvider: log);
        storage
            .ChangePublishRetryStateAsync(
                Arg.Any<MediumMessage>(),
                StatusName.Succeeded,
                Arg.Any<MessageContentWrite>(),
                Arg.Any<RetryDelay?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(false));
        return (sender, transportMessage, transport);
    }

    private static MediumMessage? _RowFor(MediumMessage message, string rowState)
    {
        if (string.Equals(rowState, "missing", StringComparison.Ordinal))
        {
            return null;
        }

        var row = _CreateMediumMessage();
        row.StorageId = message.StorageId;
        switch (rowState)
        {
            case "terminal":
                row.StatusName = StatusName.Failed;
                row.NextRetryAt = null;
                row.LockedUntil = null;
                break;
            case "lease-lost":
                row.StatusName = StatusName.Scheduled;
                row.NextRetryAt = DateTimeOffset.UtcNow.AddMinutes(1);
                row.LockedUntil = DateTimeOffset.UtcNow.AddMinutes(5);
                break;
            case "queued-lease-lost":
                row.StatusName = StatusName.Queued;
                row.NextRetryAt = null;
                row.LockedUntil = DateTimeOffset.UtcNow.AddMinutes(5);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(rowState), rowState, null);
        }

        return row;
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<(LogLevel Level, EventId EventId, Exception? Exception)> _entries = new();

        public IReadOnlyList<(LogLevel Level, EventId EventId, Exception? Exception)> Entries => [.. _entries];

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_entries);

        public void Dispose() { }

        private sealed class CapturingLogger(
            ConcurrentQueue<(LogLevel Level, EventId EventId, Exception? Exception)> entries
        ) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter
            )
            {
                entries.Enqueue((logLevel, eventId, exception));
            }
        }
    }

    [Fact]
    public async Task should_mark_row_failed_without_sending_when_delivery_deadline_passed()
    {
        // given — a stored row whose delivery deadline passed while it waited for the relay
        var storage = Substitute.For<IDataStorage>();
        var serializer = Substitute.For<IMessageSerializer>();
        var transport = Substitute.For<ITransport>();
        transport.BrokerAddress.Returns(new BrokerAddress("Test", "localhost"));
        MessageTerminalReason? reason = null;
        var sender = _CreateSender(
            storage,
            serializer,
            transport,
            new MessagingOptions
            {
                RetryPolicy =
                {
                    RetryStrategy = TestRetryStrategies.ZeroDelay(3),
                    MaxPersistedRetries = 2,
                    OnTerminal = (info, _) =>
                    {
                        reason = info.Reason;
                        return Task.CompletedTask;
                    },
                },
            }
        );
        var message = _CreateMediumMessage(MessageLane.Queue);
        message.Origin.Headers[Headers.DeliverBy] = DateTimeOffset
            .UtcNow.AddSeconds(-1)
            .ToString("O", CultureInfo.InvariantCulture);
        var expired = new List<string?>();
        using var listener = _ListenToSendExpired(expired);

        // when
        var result = await sender.SendAsync(message);

        // then — terminal Failed with the reason, no serialization, no transport attempt, no retry; reported as expired
        result.Succeeded.Should().BeFalse();
        result.Exception.Should().BeOfType<MessageDeliverByExceededException>();
        await serializer
            .DidNotReceiveWithAnyArgs()
            .SerializeToTransportMessageAsync(default!, Arg.Any<CancellationToken>());
        await transport.DidNotReceiveWithAnyArgs().SendAsync(default!, Arg.Any<CancellationToken>());
        await storage
            .Received(1)
            .ChangePublishRetryStateAsync(
                message,
                StatusName.Failed,
                MessageContentWrite.Refresh,
                null,
                null,
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            );
        message.Origin.Headers[Headers.Exception].Should().Be(nameof(MessageDeliverByExceededException));
        message.ExceptionInfo.Should().Contain("deliver_by_exceeded");
        message.FailureFingerprint.Should().Be("v1:expired");
        message.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);
        reason.Should().Be(MessageTerminalReason.DeliveryDeadlineExpired);
        expired.Should().Contain("queue");
    }

    [Fact]
    public async Task should_not_count_or_log_the_drop_when_another_worker_already_settled_the_expired_row()
    {
        // given — the deadline passed, but the Failed write loses its fence to another worker that settled the row
        var storage = Substitute.For<IDataStorage>();
        var transport = Substitute.For<ITransport>();
        transport.BrokerAddress.Returns(new BrokerAddress("Test", "localhost"));
        using var logs = new CapturingLoggerProvider();
        var sender = _CreateSender(
            storage,
            Substitute.For<IMessageSerializer>(),
            transport,
            new MessagingOptions { RetryPolicy = { RetryStrategy = TestRetryStrategies.ZeroDelay(3) } },
            loggerProvider: logs
        );
        storage
            .ChangePublishRetryStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<MessageContentWrite>(),
                Arg.Any<RetryDelay?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(false));
        var message = _CreateMediumMessage(MessageLane.Queue);
        message.Origin.Headers[Headers.DeliverBy] = DateTimeOffset
            .UtcNow.AddSeconds(-1)
            .ToString("O", CultureInfo.InvariantCulture);
        var expired = new List<string?>();
        using var listener = _ListenToSendExpired(expired);

        // when
        var result = await sender.SendAsync(message);

        // then — the attempt still ends unsent, but the drop is the winner's to count and log
        result.Exception.Should().BeOfType<MessageDeliverByExceededException>();
        await transport.DidNotReceiveWithAnyArgs().SendAsync(default!, Arg.Any<CancellationToken>());
        expired.Should().BeEmpty();
        logs.Entries.Should().NotContain(e => e.EventId.Id == 4125);
    }

    [Fact]
    public async Task should_end_as_expired_without_exhausting_when_crash_recovery_finds_deadline_passed()
    {
        // given — a crashed dispatch already reserved its final inline attempt, and the deadline passed since
        var storage = Substitute.For<IDataStorage>();
        var serializer = Substitute.For<IMessageSerializer>();
        var transport = Substitute.For<ITransport>();
        transport.BrokerAddress.Returns(new BrokerAddress("Test", "localhost"));
        MessageTerminalReason? reason = null;
        var sender = _CreateSender(
            storage,
            serializer,
            transport,
            new MessagingOptions
            {
                RetryPolicy =
                {
                    RetryStrategy = TestRetryStrategies.ZeroDelay(0),
                    MaxPersistedRetries = 0,
                    OnTerminal = (info, _) =>
                    {
                        reason = info.Reason;
                        return Task.CompletedTask;
                    },
                },
            }
        );
        var message = _CreateMediumMessage();
        message.InlineAttempts = 1;
        message.LockedUntil = DateTimeOffset.UtcNow.AddMinutes(1);
        message.Origin.Headers[Headers.DeliverBy] = DateTimeOffset
            .UtcNow.AddSeconds(-1)
            .ToString("O", CultureInfo.InvariantCulture);

        // when
        var result = await sender.SendAsync(message);

        // then — the would-be exhaustion is reported as an expiry instead
        result.Exception.Should().BeOfType<MessageDeliverByExceededException>();
        reason.Should().Be(MessageTerminalReason.DeliveryDeadlineExpired);
        await transport.DidNotReceiveWithAnyArgs().SendAsync(default!, Arg.Any<CancellationToken>());
        await storage
            .Received(1)
            .ChangePublishRetryStateAsync(
                message,
                StatusName.Failed,
                MessageContentWrite.Refresh,
                null,
                null,
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_send_normally_when_delivery_deadline_is_still_ahead()
    {
        // given
        var storage = Substitute.For<IDataStorage>();
        var transportMessage = _CreateTransportMessage();
        var serializer = Substitute.For<IMessageSerializer>();
        serializer
            .SerializeToTransportMessageAsync(Arg.Any<Message>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(transportMessage));
        var transport = Substitute.For<ITransport>();
        transport.BrokerAddress.Returns(new BrokerAddress("Test", "localhost"));
        transport.SendAsync(transportMessage, Arg.Any<CancellationToken>()).Returns(OperateResult.Success);
        var sender = _CreateSender(storage, serializer, transport, new MessagingOptions());
        var message = _CreateMediumMessage();
        message.Origin.Headers[Headers.DeliverBy] = DateTimeOffset
            .UtcNow.AddMinutes(5)
            .ToString("O", CultureInfo.InvariantCulture);

        // when
        var result = await sender.SendAsync(message);

        // then
        result.Succeeded.Should().BeTrue();
        await transport.Received(1).SendAsync(transportMessage, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_count_one_terminal_publish_when_the_failure_is_not_retried()
    {
        // given: the strategy classifies the failure as permanent
        var name = $"terminal.permanent.{Guid.NewGuid():N}";
        var sender = _CreateTerminalSender(
            // ReSharper disable once NotResolvedInText
            OperateResult.Failed(new ArgumentNullException("param")),
            TestRetryStrategies.PermanentArgument(0),
            maxPersistedRetries: 0
        );
        var terminal = new List<(string? ErrorType, string? Lane)>();
        using var listener = _ListenToOutboxTerminal(name, terminal);

        // when
        var result = await sender.SendAsync(_CreateMediumMessage(name: name));

        // then
        result.Succeeded.Should().BeFalse();
        terminal
            .Should()
            .Equal((typeof(ArgumentNullException).FullName, LaneTagEnricher.ToTagValues(MessageLane.Bus).Lane));
    }

    [Fact]
    public async Task should_count_one_terminal_publish_when_the_retries_are_spent()
    {
        // given: no persisted retry is left after the inline burst
        var name = $"terminal.exhausted.{Guid.NewGuid():N}";
        var sender = _CreateTerminalSender(
            OperateResult.Failed(new TimeoutException("transient")),
            TestRetryStrategies.FixedDelay(0, TimeSpan.Zero),
            maxPersistedRetries: 0
        );
        var terminal = new List<(string? ErrorType, string? Lane)>();
        using var listener = _ListenToOutboxTerminal(name, terminal);

        // when
        await sender.SendAsync(_CreateMediumMessage(MessageLane.Queue, name));

        // then
        terminal
            .Should()
            .Equal((typeof(TimeoutException).FullName, LaneTagEnricher.ToTagValues(MessageLane.Queue).Lane));
    }

    [Fact]
    public async Task should_not_count_a_terminal_publish_when_the_failure_will_be_retried()
    {
        // given: a persisted retry is still left
        var name = $"terminal.retried.{Guid.NewGuid():N}";
        var sender = _CreateTerminalSender(
            OperateResult.Failed(new TimeoutException("transient")),
            TestRetryStrategies.FixedDelay(0, TimeSpan.FromSeconds(5)),
            maxPersistedRetries: 1
        );
        var terminal = new List<(string? ErrorType, string? Lane)>();
        using var listener = _ListenToOutboxTerminal(name, terminal);

        // when
        await sender.SendAsync(_CreateMediumMessage(name: name));

        // then
        terminal.Should().BeEmpty();
    }

    [Fact]
    public async Task should_not_count_a_terminal_publish_when_another_writer_already_ended_the_row()
    {
        // given: the terminal write is refused because the row is already terminal
        var name = $"terminal.lost.{Guid.NewGuid():N}";
        var storage = Substitute.For<IDataStorage>();
        var sender = _CreateTerminalSender(
            OperateResult.Failed(new TimeoutException("transient")),
            TestRetryStrategies.FixedDelay(0, TimeSpan.Zero),
            maxPersistedRetries: 0,
            storage
        );
        storage
            .ChangePublishRetryStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<MessageContentWrite>(),
                Arg.Any<RetryDelay?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(false));
        var terminal = new List<(string? ErrorType, string? Lane)>();
        using var listener = _ListenToOutboxTerminal(name, terminal);

        // when
        await sender.SendAsync(_CreateMediumMessage(name: name));

        // then
        terminal.Should().BeEmpty();
    }

    [Fact]
    [Guarantee("MSG-OBS-02")]
    public async Task should_report_retries_exhausted_with_the_envelope_tenant_when_the_publish_budget_is_spent()
    {
        // given: no inline or persisted retry is left after the first failed send
        var calls = new ConcurrentQueue<TerminalMessageInfo>();
        var sender = _CreateRecordingTerminalSender(
            OperateResult.Failed(new TimeoutException("transient")),
            TestRetryStrategies.FixedDelay(0, TimeSpan.Zero),
            calls
        );
        var message = _CreateTenantMessage("tenant-a");

        // when
        await sender.SendAsync(message);

        // then
        var info = calls.Should().ContainSingle().Subject;
        info.Reason.Should().Be(MessageTerminalReason.RetriesExhausted);
        info.TenantId.Should().Be("tenant-a");
        info.MessageType.Should().Be(MessageType.Publish);
        info.StorageId.Should().Be(message.StorageId);
        info.Exception.Should().BeOfType<TimeoutException>();
    }

    [Fact]
    [Guarantee("MSG-OBS-02")]
    public async Task should_report_permanent_failure_when_the_retry_classification_ends_the_publish_at_once()
    {
        // given: the strategy treats the argument failure as non-retryable, so the budget is never spent
        var calls = new ConcurrentQueue<TerminalMessageInfo>();
        var sender = _CreateRecordingTerminalSender(
            // ReSharper disable once NotResolvedInText
            OperateResult.Failed(new ArgumentNullException("param")),
            TestRetryStrategies.PermanentArgument(3),
            calls,
            maxPersistedRetries: 5
        );

        // when
        await sender.SendAsync(_CreateTenantMessage("tenant-a"));

        // then
        var info = calls.Should().ContainSingle().Subject;
        info.Reason.Should().Be(MessageTerminalReason.PermanentFailure);
        info.TenantId.Should().Be("tenant-a");
        info.Exception.Should().BeOfType<ArgumentNullException>();
    }

    [Fact]
    [Guarantee("MSG-OBS-02")]
    public async Task should_report_delivery_deadline_expired_when_a_stored_publish_outlives_its_deadline()
    {
        // given: the row comes due after its delivery deadline
        var calls = new ConcurrentQueue<TerminalMessageInfo>();
        var sender = _CreateRecordingTerminalSender(OperateResult.Success, TestRetryStrategies.ZeroDelay(3), calls);
        var message = _CreateTenantMessage("tenant-a");
        message.Origin.Headers[Headers.DeliverBy] = DateTimeOffset
            .UtcNow.AddSeconds(-1)
            .ToString("O", CultureInfo.InvariantCulture);

        // when
        await sender.SendAsync(message);

        // then
        var info = calls.Should().ContainSingle().Subject;
        info.Reason.Should().Be(MessageTerminalReason.DeliveryDeadlineExpired);
        info.TenantId.Should().Be("tenant-a");
        info.Exception.Should().BeOfType<MessageDeliverByExceededException>();
    }

    [Fact]
    [Guarantee("MSG-OBS-02")]
    public async Task should_report_no_transport_when_no_registered_transport_serves_the_stored_lane()
    {
        // given: a host with a queue transport only, and a stored bus message
        var calls = new ConcurrentQueue<TerminalMessageInfo>();
        var storage = Substitute.For<IDataStorage>();
        var serializer = Substitute.For<IMessageSerializer>();
        serializer
            .SerializeToTransportMessageAsync(Arg.Any<Message>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(_CreateTransportMessage()));
        var sender = _CreateSenderWithTransports(
            storage,
            serializer,
            new MessagingOptions { RetryPolicy = { OnTerminal = _Record(calls) } },
            busTransport: null,
            queueTransport: Substitute.For<IQueueTransport>()
        );
        var message = _CreateTenantMessage("tenant-a");

        // when
        await sender.SendAsync(message);

        // then
        var info = calls.Should().ContainSingle().Subject;
        info.Reason.Should().Be(MessageTerminalReason.NoTransport);
        info.TenantId.Should().Be("tenant-a");
        info.Lane.Should().Be(MessageLane.Bus);
    }

    [Fact]
    [Guarantee("MSG-OBS-02")]
    public async Task should_not_report_a_terminal_publish_when_another_writer_already_ended_the_row()
    {
        // given: the expiry write is refused because the row is already terminal
        var calls = new ConcurrentQueue<TerminalMessageInfo>();
        var storage = Substitute.For<IDataStorage>();
        var sender = _CreateRecordingTerminalSender(
            OperateResult.Success,
            TestRetryStrategies.ZeroDelay(3),
            calls,
            storage: storage
        );
        storage
            .ChangePublishRetryStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<MessageContentWrite>(),
                Arg.Any<RetryDelay?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(false));
        var message = _CreateTenantMessage("tenant-a");
        message.Origin.Headers[Headers.DeliverBy] = DateTimeOffset
            .UtcNow.AddSeconds(-1)
            .ToString("O", CultureInfo.InvariantCulture);

        // when
        await sender.SendAsync(message);

        // then
        calls.Should().BeEmpty();
    }

    private static MediumMessage _CreateTenantMessage(string tenantId)
    {
        var message = _CreateMediumMessage();
        message.Origin.Headers[Headers.TenantId] = tenantId;
        return message;
    }

    private static Func<TerminalMessageInfo, CancellationToken, Task> _Record(
        ConcurrentQueue<TerminalMessageInfo> calls
    )
    {
        return (info, _) =>
        {
            calls.Enqueue(info);
            return Task.CompletedTask;
        };
    }

    private static MessageSender _CreateRecordingTerminalSender(
        OperateResult sendResult,
        Polly.Retry.RetryStrategyOptions retryStrategy,
        ConcurrentQueue<TerminalMessageInfo> calls,
        int maxPersistedRetries = 0,
        IDataStorage? storage = null
    )
    {
        var transportMessage = _CreateTransportMessage();
        var serializer = Substitute.For<IMessageSerializer>();
        serializer
            .SerializeToTransportMessageAsync(Arg.Any<Message>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(transportMessage));

        var transport = Substitute.For<ITransport>();
        transport.BrokerAddress.Returns(new BrokerAddress("Test", "localhost"));
        transport.SendAsync(transportMessage, Arg.Any<CancellationToken>()).Returns(sendResult);

        return _CreateSender(
            storage ?? Substitute.For<IDataStorage>(),
            serializer,
            transport,
            new MessagingOptions
            {
                RetryPolicy =
                {
                    RetryStrategy = retryStrategy,
                    MaxPersistedRetries = maxPersistedRetries,
                    OnTerminal = _Record(calls),
                },
            }
        );
    }

    private static MessageSender _CreateTerminalSender(
        OperateResult sendResult,
        Polly.Retry.RetryStrategyOptions retryStrategy,
        int maxPersistedRetries,
        IDataStorage? storage = null
    )
    {
        var transportMessage = _CreateTransportMessage();
        var serializer = Substitute.For<IMessageSerializer>();
        serializer
            .SerializeToTransportMessageAsync(Arg.Any<Message>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(transportMessage));

        var transport = Substitute.For<ITransport>();
        transport.BrokerAddress.Returns(new BrokerAddress("Test", "localhost"));
        transport.SendAsync(transportMessage, Arg.Any<CancellationToken>()).Returns(sendResult);

        return _CreateSender(
            storage ?? Substitute.For<IDataStorage>(),
            serializer,
            transport,
            new MessagingOptions
            {
                RetryPolicy = { RetryStrategy = retryStrategy, MaxPersistedRetries = maxPersistedRetries },
            }
        );
    }

    // The counter is process-wide and other tests publish too, so only measurements of this test's message count.
    private static System.Diagnostics.Metrics.MeterListener _ListenToOutboxTerminal(
        string destinationName,
        List<(string? ErrorType, string? Lane)> terminal
    )
    {
        var listener = new System.Diagnostics.Metrics.MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (string.Equals(instrument.Name, MessagingMetrics.OutboxTerminalName, StringComparison.Ordinal))
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>(
            (_, _, tags, _) =>
            {
                string? destination = null;
                string? errorType = null;
                string? lane = null;
                foreach (var tag in tags)
                {
                    switch (tag.Key)
                    {
                        case MessagingMetrics.TagDestinationName:
                            destination = tag.Value?.ToString();
                            break;
                        case MessagingMetrics.TagErrorType:
                            errorType = tag.Value?.ToString();
                            break;
                        case MessagingTags.Lane:
                            lane = tag.Value?.ToString();
                            break;
                    }
                }

                if (string.Equals(destination, destinationName, StringComparison.Ordinal))
                {
                    lock (terminal)
                    {
                        terminal.Add((errorType, lane));
                    }
                }
            }
        );
        listener.Start();
        return listener;
    }

    private static System.Diagnostics.Metrics.MeterListener _ListenToSendExpired(List<string?> lanes)
    {
        var listener = new System.Diagnostics.Metrics.MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (string.Equals(instrument.Name, MessagingMetrics.SendExpiredName, StringComparison.Ordinal))
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>(
            (_, _, tags, _) =>
            {
                foreach (var tag in tags)
                {
                    if (string.Equals(tag.Key, MessagingTags.Lane, StringComparison.Ordinal))
                    {
                        lock (lanes)
                        {
                            lanes.Add(tag.Value?.ToString());
                        }
                    }
                }
            }
        );
        listener.Start();
        return listener;
    }
}
