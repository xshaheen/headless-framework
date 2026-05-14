// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Messaging.Serialization;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
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
        MessagingOptions options
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(storage);
        services.AddSingleton(serializer);
        services.AddSingleton(transport);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(options));

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

    private sealed class ZeroDelayRetryBackoffStrategy : IRetryBackoffStrategy
    {
        public TimeSpan? GetNextDelay(int retryAttempt, Exception? exception = null) => TimeSpan.Zero;

        public bool ShouldRetry(Exception exception) => true;
    }

    private sealed class FixedDelayRetryBackoffStrategy(TimeSpan delay) : IRetryBackoffStrategy
    {
        public TimeSpan? GetNextDelay(int retryAttempt, Exception? exception = null) => delay;

        public bool ShouldRetry(Exception exception) => true;
    }
}
