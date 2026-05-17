// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Messaging.Retry;
using Headless.Messaging.Serialization;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tests.Helpers;

namespace Tests;

public sealed class MessageSenderTests : TestBase
{
    private static MediumMessage _CreateMediumMessage()
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = Guid.NewGuid().ToString(),
            [Headers.MessageName] = "test.topic",
        };

        return new MediumMessage
        {
            StorageId = 1L,
            Origin = new Message(headers, "{}"),
            Content = "{}",
            Added = DateTime.UtcNow,
        };
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
            .LeasePublishAsync(Arg.Any<MediumMessage>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(true));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(storage);
        services.AddSingleton(serializer);
        services.AddSingleton(transport);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(options));
        if (lifetime is not null)
        {
            services.AddSingleton(lifetime);
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
            .ChangePublishStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<object?>(),
                Arg.Any<DateTime?>(),
                Arg.Any<DateTime?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));

        var transportMessage = new TransportMessage(
            new Dictionary<string, string?>(StringComparer.Ordinal),
            ReadOnlyMemory<byte>.Empty
        );
        var serializer = Substitute.For<ISerializer>();
        serializer.SerializeToTransportMessageAsync(Arg.Any<Message>()).Returns(ValueTask.FromResult(transportMessage));

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
                RetryPolicy =
                {
                    MaxInlineRetries = 4,
                    MaxPersistedRetries = 0,
                    BackoffStrategy = new ZeroDelayRetryBackoffStrategy(),
                },
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
                Arg.Any<object?>(),
                Arg.Any<DateTime?>(),
                Arg.Any<DateTime?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));

        var transportMessage = new TransportMessage(
            new Dictionary<string, string?>(StringComparer.Ordinal),
            ReadOnlyMemory<byte>.Empty
        );
        var serializer = Substitute.For<ISerializer>();
        serializer.SerializeToTransportMessageAsync(Arg.Any<Message>()).Returns(ValueTask.FromResult(transportMessage));

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
                    MaxInlineRetries = 1,
                    MaxPersistedRetries = 0,
                    BackoffStrategy = new FixedDelayRetryBackoffStrategy(TimeSpan.FromMilliseconds(40)),
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
                Arg.Any<object?>(),
                Arg.Any<DateTime?>(),
                Arg.Any<DateTime?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));

        var transportMessage = new TransportMessage(
            new Dictionary<string, string?>(StringComparer.Ordinal),
            ReadOnlyMemory<byte>.Empty
        );
        var serializer = Substitute.For<ISerializer>();
        serializer.SerializeToTransportMessageAsync(Arg.Any<Message>()).Returns(ValueTask.FromResult(transportMessage));

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
                    MaxInlineRetries = 0,
                    MaxPersistedRetries = 1,
                    BackoffStrategy = new FixedDelayRetryBackoffStrategy(TimeSpan.FromSeconds(5)),
                },
            }
        );

        // when
        var result = await sender.SendAsync(_CreateMediumMessage());

        // then
        result.Succeeded.Should().BeFalse();
        await storage
            .Received(1)
            .ChangePublishStateAsync(
                Arg.Any<MediumMessage>(),
                StatusName.Failed,
                Arg.Any<object?>(),
                Arg.Is<DateTime?>(value => value.HasValue),
                Arg.Any<DateTime?>(),
                Arg.Is<int?>(value => value == 0),
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
                Arg.Any<object?>(),
                Arg.Any<DateTime?>(),
                Arg.Any<DateTime?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));

        var transportMessage = new TransportMessage(
            new Dictionary<string, string?>(StringComparer.Ordinal),
            ReadOnlyMemory<byte>.Empty
        );
        var serializer = Substitute.For<ISerializer>();
        serializer.SerializeToTransportMessageAsync(Arg.Any<Message>()).Returns(ValueTask.FromResult(transportMessage));

        var transport = Substitute.For<ITransport>();
        transport.BrokerAddress.Returns(new BrokerAddress("Test", "localhost"));
        transport
            .SendAsync(transportMessage, Arg.Any<CancellationToken>())
            .Returns(OperateResult.Failed(new ArgumentNullException("param")));

        var backoffStrategy = Substitute.For<IRetryBackoffStrategy>();
        backoffStrategy.Compute(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<Exception>()).Returns(RetryDecision.Stop);

        var callbackInvoked = false;
        var sender = _CreateSender(
            storage,
            serializer,
            transport,
            new MessagingOptions
            {
                RetryPolicy =
                {
                    MaxInlineRetries = 0,
                    MaxPersistedRetries = 0,
                    BackoffStrategy = backoffStrategy,
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
            .ChangePublishStateAsync(
                Arg.Any<MediumMessage>(),
                StatusName.Failed,
                Arg.Any<object?>(),
                Arg.Is<DateTime?>(v => v == null),
                Arg.Any<DateTime?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_treat_shutdown_oce_as_cancellation_without_writing_state()
    {
        // given — IHostApplicationLifetime.ApplicationStopping is signalled and the transport
        // surfaces an OCE bound to that same token. The sender must classify this as cancellation
        // (RetryDecision.Stop), NOT invoke OnExhausted, and NOT write a state transition. The row's
        // existing NextRetryAt remains and the persisted retry processor picks it up on restart.
        var storage = Substitute.For<IDataStorage>();
        storage
            .ChangePublishStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<object?>(),
                Arg.Any<DateTime?>(),
                Arg.Any<DateTime?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));

        var transportMessage = new TransportMessage(
            new Dictionary<string, string?>(StringComparer.Ordinal),
            ReadOnlyMemory<byte>.Empty
        );
        var serializer = Substitute.For<ISerializer>();
        serializer.SerializeToTransportMessageAsync(Arg.Any<Message>()).Returns(ValueTask.FromResult(transportMessage));

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
                    MaxInlineRetries = 0,
                    MaxPersistedRetries = 4,
                    BackoffStrategy = new FixedIntervalBackoffStrategy(TimeSpan.Zero),
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
                Arg.Any<object?>(),
                Arg.Any<DateTime?>(),
                Arg.Any<DateTime?>(),
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
                Arg.Any<object?>(),
                Arg.Any<DateTime?>(),
                Arg.Any<DateTime?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(false));

        var transportMessage = new TransportMessage(
            new Dictionary<string, string?>(StringComparer.Ordinal),
            ReadOnlyMemory<byte>.Empty
        );
        var serializer = Substitute.For<ISerializer>();
        serializer.SerializeToTransportMessageAsync(Arg.Any<Message>()).Returns(ValueTask.FromResult(transportMessage));

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
                    MaxInlineRetries = 0,
                    MaxPersistedRetries = 0,
                    BackoffStrategy = new FixedDelayRetryBackoffStrategy(TimeSpan.Zero),
                    OnExhausted = (_, _) =>
                    {
                        callbackInvoked = true;
                        return Task.CompletedTask;
                    },
                },
            }
        );

        // when — config forces Exhausted on the first failure
        var result = await sender.SendAsync(_CreateMediumMessage());

        // then — conditional UPDATE returned false: callback skipped despite the Exhausted decision
        result.Succeeded.Should().BeFalse();
        callbackInvoked.Should().BeFalse();
        await storage
            .Received(1)
            .ChangePublishStateAsync(
                Arg.Any<MediumMessage>(),
                StatusName.Failed,
                Arg.Any<object?>(),
                Arg.Is<DateTime?>(v => v == null),
                Arg.Any<DateTime?>(),
                Arg.Any<int?>(),
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
    public async Task on_exhausted_callback_should_resolve_same_scoped_service_as_dispatch_scope()
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
                Arg.Any<object?>(),
                Arg.Any<DateTime?>(),
                Arg.Any<DateTime?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));

        var transportMessage = new TransportMessage(
            new Dictionary<string, string?>(StringComparer.Ordinal),
            ReadOnlyMemory<byte>.Empty
        );
        var serializer = Substitute.For<ISerializer>();
        serializer.SerializeToTransportMessageAsync(Arg.Any<Message>()).Returns(ValueTask.FromResult(transportMessage));

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
                    MaxInlineRetries = 0,
                    MaxPersistedRetries = 0,
                    BackoffStrategy = new ZeroDelayRetryBackoffStrategy(),
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
            new MessagingOptions { RetryPolicy = { MaxInlineRetries = 0, MaxPersistedRetries = 0 } }
        );

        // Override the happy-path lease stub from _CreateSender so the lease returns false.
        storage
            .LeasePublishAsync(Arg.Any<MediumMessage>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
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

    private sealed class ScopedMarker;
}
