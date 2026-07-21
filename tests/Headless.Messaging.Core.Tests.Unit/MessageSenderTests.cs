// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Messaging.Serialization;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

#pragma warning disable MA0015 // Specify the parameter name in ArgumentException
namespace Tests;

public sealed class MessageSenderTests : TestBase
{
    private static MediumMessage _CreateMediumMessage(IntentType intentType = IntentType.Bus)
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = Guid.NewGuid().ToString(),
            [Headers.MessageName] = "test.messageName",
        };

        return new MediumMessage
        {
            StorageId = Guid.NewGuid(),
            Origin = new Message(headers, "{}"),
            Content = "{}",
            IntentType = intentType,
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

    private static MessageSender _CreateSender(
        IDataStorage storage,
        ISerializer serializer,
        ITransport transport,
        MessagingOptions options,
        IHostApplicationLifetime? lifetime = null
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
                Arg.Any<System.Data.Common.DbTransaction?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));
        storage
            .ChangePublishRetryStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<DateTimeOffset?>(),
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
        services.AddSingleton(transport);
        services.AddSingleton<IBusTransport>(_ => new TestBusTransportAdapter(transport));
        services.AddSingleton<IQueueTransport>(_ => new TestQueueTransportAdapter(transport));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(options));
        if (lifetime is not null)
        {
            services.AddSingleton(lifetime);
        }

        var provider = services.BuildServiceProvider();
        return new MessageSender(provider.GetRequiredService<ILogger<MessageSender>>(), provider);
    }

    private static MessageSender _CreateSenderWithTransports(
        IDataStorage storage,
        ISerializer serializer,
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
                Arg.Any<DateTimeOffset?>(),
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
    public async Task should_honor_failed_retry_count_above_three()
    {
        // given
        var storage = Substitute.For<IDataStorage>();
        storage
            .ChangePublishRetryStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));

        var transportMessage = _CreateTransportMessage();
        var serializer = Substitute.For<ISerializer>();
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
        var result = await sender.SendAsync(_CreateMediumMessage());

        // then
        result.Succeeded.Should().BeTrue();
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
                Arg.Any<System.Data.Common.DbTransaction?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));

        var transportMessage = _CreateTransportMessage();
        var serializer = Substitute.For<ISerializer>();
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
                Arg.Any<System.Data.Common.DbTransaction?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));

        var transportMessage = _CreateTransportMessage();
        var serializer = Substitute.For<ISerializer>();
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
        var result = await sender.SendAsync(_CreateMediumMessage());

        // then
        result.Succeeded.Should().BeFalse();
        await storage
            .Received(1)
            .ChangePublishRetryStateAsync(
                Arg.Any<MediumMessage>(),
                StatusName.Failed,
                Arg.Is<DateTimeOffset?>(value => value.HasValue),
                Arg.Any<DateTimeOffset?>(),
                Arg.Is<int>(value => value == 0),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_persist_failed_without_invoking_on_exhausted_when_exception_is_permanent()
    {
        // given — strategy classifies the exception as permanent (returns Stop). The sender
        // must persist Failed with nextRetryAt=null (no future retry scheduled) and must NOT
        // invoke OnExhausted (Stop is a non-exhaustion terminal state — the retry budget was
        // never consumed because the exception is non-retryable).
        var storage = Substitute.For<IDataStorage>();
        storage
            .ChangePublishStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<System.Data.Common.DbTransaction?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));

        var transportMessage = _CreateTransportMessage();
        var serializer = Substitute.For<ISerializer>();
        serializer
            .SerializeToTransportMessageAsync(Arg.Any<Message>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(transportMessage));

        var transport = Substitute.For<ITransport>();
        transport.BrokerAddress.Returns(new BrokerAddress("Test", "localhost"));
        transport
            .SendAsync(transportMessage, Arg.Any<CancellationToken>())
            .Returns(OperateResult.Failed(new ArgumentNullException("param")));

        var callbackInvoked = false;
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
                    OnExhausted = (_, _) =>
                    {
                        callbackInvoked = true;
                        return Task.CompletedTask;
                    },
                },
            }
        );

        // when
        var result = await sender.SendAsync(_CreateMediumMessage());

        // then — Stop classification: persisted as Failed with null nextRetryAt, no OnExhausted.
        result.Succeeded.Should().BeFalse();
        callbackInvoked.Should().BeFalse();
        await storage
            .Received()
            .ChangePublishRetryStateAsync(
                Arg.Any<MediumMessage>(),
                StatusName.Failed,
                Arg.Is<DateTimeOffset?>(v => v == null),
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
        // (non-retryable), NOT invoke OnExhausted, and NOT write a state transition. The row's
        // existing NextRetryAt remains and the persisted retry processor picks it up on restart.
        var storage = Substitute.For<IDataStorage>();
        storage
            .ChangePublishStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<System.Data.Common.DbTransaction?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));

        var transportMessage = _CreateTransportMessage();
        var serializer = Substitute.For<ISerializer>();
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
                    OnExhausted = (_, _) =>
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

        // then — Shutdown OCE: no state write, no OnExhausted. Row keeps prior NextRetryAt/Status.
        result.Succeeded.Should().BeFalse();
        callbackInvoked.Should().BeFalse();
        await storage
            .DidNotReceive()
            .ChangePublishStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<System.Data.Common.DbTransaction?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_skip_on_exhausted_when_publish_state_change_reports_already_terminal()
    {
        // given — X1 contract: a broker redelivery (or any path where the row reached terminal
        // state by another worker) means ChangePublishStateAsync's conditional UPDATE matches zero
        // rows and returns false. The sender MUST skip OnExhausted in that case so the callback is
        // not invoked twice for the same logical exhaustion. The Subscribe path has the same
        // guarantee; this test pins the publish-side mirror.
        var storage = Substitute.For<IDataStorage>();
        storage
            .ChangePublishStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<System.Data.Common.DbTransaction?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(false));

        var transportMessage = _CreateTransportMessage();
        var serializer = Substitute.For<ISerializer>();
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
                    OnExhausted = (_, _) =>
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
                Arg.Any<DateTimeOffset?>(),
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
                Arg.Is<DateTimeOffset?>(v => v == null),
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
    public async Task should_resolve_same_scoped_service_as_dispatch_scope_when_on_exhausted_callback()
    {
        // given — a Scoped marker service. The Dispatcher creates a per-message scope and
        // passes its IServiceProvider; MessageSender must surface that SAME provider via
        // FailedInfo.ServiceProvider so OnExhausted sees the live scope.
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
                Arg.Any<System.Data.Common.DbTransaction?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));

        var transportMessage = _CreateTransportMessage();
        var serializer = Substitute.For<ISerializer>();
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
                    OnExhausted = (info, _) =>
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
        var serializer = Substitute.For<ISerializer>();
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
                Arg.Any<System.Data.Common.DbTransaction?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));

        var transportMessage = _CreateTransportMessage();
        var serializer = Substitute.For<ISerializer>();
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
        var result = await sender.SendAsync(_CreateMediumMessage(IntentType.Bus));

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
                Arg.Any<System.Data.Common.DbTransaction?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));

        var transportMessage = _CreateTransportMessage();
        var serializer = Substitute.For<ISerializer>();
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
        var result = await sender.SendAsync(_CreateMediumMessage(IntentType.Queue));

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
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));

        var serializer = Substitute.For<ISerializer>();
        serializer
            .SerializeToTransportMessageAsync(Arg.Any<Message>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(_CreateTransportMessage()));

        var sender = _CreateSenderWithTransports(storage, serializer, new MessagingOptions());
        var message = _CreateMediumMessage((IntentType)42);
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
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));

        var serializer = Substitute.For<ISerializer>();
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
        var message = _CreateMediumMessage(IntentType.Bus);
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
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));

        var serializer = Substitute.For<ISerializer>();
        serializer
            .SerializeToTransportMessageAsync(Arg.Any<Message>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(_CreateTransportMessage()));

        var busTransport = Substitute.For<IBusTransport>();
        busTransport.BrokerAddress.Returns(new BrokerAddress("bus", "localhost"));

        var sender = _CreateSenderWithTransports(storage, serializer, new MessagingOptions(), busTransport);
        var message = _CreateMediumMessage(IntentType.Queue);
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
                Arg.Any<System.Data.Common.DbTransaction?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));
        var serializer = Substitute.For<ISerializer>();
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
                Arg.Is<DateTimeOffset?>(value => value.HasValue),
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
        var serializer = Substitute.For<ISerializer>();
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
            .ChangePublishRetryStateAsync(default!, default, default, default, default, default, AbortToken);
        await storage.DidNotReceiveWithAnyArgs().LeasePublishAsync(default!, default, AbortToken);
        await storage
            .DidNotReceiveWithAnyArgs()
            .LeasePublishAndReserveAttemptAsync(default!, default, default, AbortToken);
    }

    [Fact]
    public async Task should_issue_single_combined_storage_write_before_fresh_dispatch()
    {
        // given
        var storage = Substitute.For<IDataStorage>();
        var transportMessage = _CreateTransportMessage();
        var serializer = Substitute.For<ISerializer>();
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
}
