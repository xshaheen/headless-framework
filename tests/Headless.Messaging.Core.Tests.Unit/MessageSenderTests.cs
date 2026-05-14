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
            .ChangePublishStateAsync(Arg.Any<MediumMessage>(), Arg.Any<StatusName>())
            .Returns(ValueTask.CompletedTask);

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
            .SendAsync(transportMessage, CancellationToken.None)
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
                    MaxAttempts = 5,
                    MaxInlineRetries = 4,
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
            .ChangePublishStateAsync(Arg.Any<MediumMessage>(), Arg.Any<StatusName>())
            .Returns(ValueTask.CompletedTask);

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
            .SendAsync(transportMessage, CancellationToken.None)
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
                    MaxAttempts = 2,
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
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.CompletedTask);

        var transportMessage = new TransportMessage(
            new Dictionary<string, string?>(StringComparer.Ordinal),
            ReadOnlyMemory<byte>.Empty
        );
        var serializer = Substitute.For<ISerializer>();
        serializer.SerializeToTransportMessageAsync(Arg.Any<Message>()).Returns(ValueTask.FromResult(transportMessage));

        var transport = Substitute.For<ITransport>();
        transport.BrokerAddress.Returns(new BrokerAddress("Test", "localhost"));
        transport
            .SendAsync(transportMessage, CancellationToken.None)
            .Returns(OperateResult.Failed(new TimeoutException("boom")));

        var sender = _CreateSender(
            storage,
            serializer,
            transport,
            new MessagingOptions
            {
                RetryPolicy =
                {
                    MaxAttempts = 2,
                    MaxInlineRetries = 0,
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
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.CompletedTask);

        var transportMessage = new TransportMessage(
            new Dictionary<string, string?>(StringComparer.Ordinal),
            ReadOnlyMemory<byte>.Empty
        );
        var serializer = Substitute.For<ISerializer>();
        serializer.SerializeToTransportMessageAsync(Arg.Any<Message>()).Returns(ValueTask.FromResult(transportMessage));

        var transport = Substitute.For<ITransport>();
        transport.BrokerAddress.Returns(new BrokerAddress("Test", "localhost"));
        transport
            .SendAsync(transportMessage, CancellationToken.None)
            .Returns(OperateResult.Failed(new ArgumentNullException("param")));

        var backoffStrategy = Substitute.For<IRetryBackoffStrategy>();
        backoffStrategy.Compute(Arg.Any<int>(), Arg.Any<Exception>()).Returns(RetryDecision.Stop);

        var callbackInvoked = false;
        var sender = _CreateSender(
            storage,
            serializer,
            transport,
            new MessagingOptions
            {
                RetryPolicy =
                {
                    MaxAttempts = 1,
                    MaxInlineRetries = 0,
                    BackoffStrategy = backoffStrategy,
                    OnExhausted = _ => callbackInvoked = true,
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
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_treat_shutdown_oce_as_cancellation_and_skip_on_exhausted()
    {
        // given — IHostApplicationLifetime.ApplicationStopping is signalled and the transport
        // surfaces an OCE bound to that same token. The sender must classify this as cancellation
        // (RetryDecision.Stop) and NOT invoke OnExhausted.
        var storage = Substitute.For<IDataStorage>();
        storage
            .ChangePublishStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<object?>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.CompletedTask);

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
        transport.SendAsync(transportMessage, CancellationToken.None).Returns(OperateResult.Failed(shutdownOce));

        var callbackInvoked = false;
        var sender = _CreateSender(
            storage,
            serializer,
            transport,
            new MessagingOptions
            {
                RetryPolicy =
                {
                    MaxAttempts = 5,
                    MaxInlineRetries = 0,
                    BackoffStrategy = new FixedIntervalBackoffStrategy(TimeSpan.Zero),
                    OnExhausted = _ => callbackInvoked = true,
                },
            },
            new FakeHostApplicationLifetime(cts.Token)
        );

        // when
        var result = await sender.SendAsync(_CreateMediumMessage());

        // then — Stop classification: persisted as Failed, no OnExhausted, no Retries increment.
        result.Succeeded.Should().BeFalse();
        callbackInvoked.Should().BeFalse();
        await storage
            .Received()
            .ChangePublishStateAsync(
                Arg.Any<MediumMessage>(),
                StatusName.Failed,
                Arg.Any<object?>(),
                Arg.Is<DateTime?>(v => v == null),
                Arg.Any<CancellationToken>()
            );
    }

    private sealed class ZeroDelayRetryBackoffStrategy : IRetryBackoffStrategy
    {
        public RetryDecision Compute(int retryCount, Exception exception) => RetryDecision.Continue(TimeSpan.Zero);
    }

    private sealed class FixedDelayRetryBackoffStrategy(TimeSpan delay) : IRetryBackoffStrategy
    {
        public RetryDecision Compute(int retryCount, Exception exception) => RetryDecision.Continue(delay);
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
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.CompletedTask);

        var transportMessage = new TransportMessage(
            new Dictionary<string, string?>(StringComparer.Ordinal),
            ReadOnlyMemory<byte>.Empty
        );
        var serializer = Substitute.For<ISerializer>();
        serializer.SerializeToTransportMessageAsync(Arg.Any<Message>()).Returns(ValueTask.FromResult(transportMessage));

        var transport = Substitute.For<ITransport>();
        transport.BrokerAddress.Returns(new BrokerAddress("Test", "localhost"));
        transport
            .SendAsync(transportMessage, CancellationToken.None)
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
                    MaxAttempts = 1,
                    MaxInlineRetries = 0,
                    BackoffStrategy = new ZeroDelayRetryBackoffStrategy(),
                    OnExhausted = info => observed = info.ServiceProvider.GetRequiredService<ScopedMarker>(),
                },
            }
        );

        // when
        await sender.SendAsync(_CreateMediumMessage(), dispatchScope.ServiceProvider);

        // then — same scope means same Scoped instance
        observed.Should().NotBeNull();
        observed.Should().BeSameAs(expected);
    }

    private sealed class ScopedMarker;
}
