// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Reflection;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class SubscribeExecutorRetryTests : TestBase
{
    private static MediumMessage _CreateMediumMessage()
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = Guid.NewGuid().ToString(),
            [Headers.MessageName] = "test.topic",
            [Headers.Group] = "test-group",
        };

        return new MediumMessage
        {
            DbId = Guid.NewGuid().ToString(),
            Origin = new Message(headers, "{}"),
            Content = "{}",
            Added = DateTime.UtcNow,
        };
    }

    private static ConsumerExecutorDescriptor _CreateDescriptor()
    {
        var consumeMethod = typeof(IConsume<CancellationExecutorTestMessage>).GetMethod(
            nameof(IConsume<CancellationExecutorTestMessage>.Consume),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            null,
            [typeof(ConsumeContext<CancellationExecutorTestMessage>), typeof(CancellationToken)],
            null
        )!;

        return new ConsumerExecutorDescriptor
        {
            ServiceTypeInfo = typeof(CancellationExecutorTestConsumer).GetTypeInfo(),
            ImplTypeInfo = typeof(CancellationExecutorTestConsumer).GetTypeInfo(),
            MethodInfo = consumeMethod,
            TopicName = "test.topic",
            GroupName = "test-group",
            Parameters = consumeMethod
                .GetParameters()
                .Select(p => new ParameterDescriptor
                {
                    Name = p.Name!,
                    ParameterType = p.ParameterType,
                    IsFromMessaging = p.ParameterType == typeof(CancellationToken),
                })
                .ToList(),
        };
    }

    private static SubscribeExecutor _CreateExecutor(
        ISubscribeInvoker invoker,
        IDataStorage storage,
        MessagingOptions options
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(x =>
        {
            x.Subscribe<CancellationExecutorTestConsumer>().Topic("test.topic").Group("test-group");
            x.UseInMemoryMessageQueue();
            x.UseInMemoryStorage();
        });

        var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILogger<SubscribeExecutor>>();

        return new SubscribeExecutor(provider, storage, invoker, TimeProvider.System, logger, Options.Create(options));
    }

    [Fact]
    public async Task should_honor_failed_retry_count_above_three()
    {
        // given
        var storage = Substitute.For<IDataStorage>();
        storage
            .ChangeReceiveStateAsync(Arg.Any<MediumMessage>(), Arg.Any<StatusName>())
            .Returns(ValueTask.CompletedTask);

        var invoker = Substitute.For<ISubscribeInvoker>();
        var attempts = 0;
        invoker
            .InvokeAsync(Arg.Any<ConsumerContext>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                attempts++;
                if (attempts <= 4)
                {
                    return Task.FromException<ConsumerExecutedResult>(new TimeoutException("boom"));
                }

                return Task.FromResult(new ConsumerExecutedResult(null, Guid.NewGuid().ToString(), null, null));
            });

        var executor = _CreateExecutor(
            invoker,
            storage,
            new MessagingOptions { FailedRetryCount = 5, RetryBackoffStrategy = new ZeroDelayRetryBackoffStrategy() }
        );

        var message = _CreateMediumMessage();

        // when
        var result = await executor.ExecuteAsync(message, _CreateDescriptor(), CancellationToken.None);

        // then
        result.Succeeded.Should().BeTrue();
        attempts.Should().Be(5);
        message.Retries.Should().Be(4);
    }

    [Fact]
    public async Task should_apply_backoff_delay_before_retrying()
    {
        // given
        var storage = Substitute.For<IDataStorage>();
        storage
            .ChangeReceiveStateAsync(Arg.Any<MediumMessage>(), Arg.Any<StatusName>())
            .Returns(ValueTask.CompletedTask);

        var invoker = Substitute.For<ISubscribeInvoker>();
        var attempts = 0;
        invoker
            .InvokeAsync(Arg.Any<ConsumerContext>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                attempts++;
                if (attempts == 1)
                {
                    return Task.FromException<ConsumerExecutedResult>(new TimeoutException("boom"));
                }

                return Task.FromResult(new ConsumerExecutedResult(null, Guid.NewGuid().ToString(), null, null));
            });

        var executor = _CreateExecutor(
            invoker,
            storage,
            new MessagingOptions
            {
                FailedRetryCount = 2,
                RetryBackoffStrategy = new FixedDelayRetryBackoffStrategy(TimeSpan.FromMilliseconds(40)),
            }
        );

        var message = _CreateMediumMessage();
        var stopwatch = Stopwatch.StartNew();

        // when
        var result = await executor.ExecuteAsync(message, _CreateDescriptor(), CancellationToken.None);

        // then
        stopwatch.Stop();
        result.Succeeded.Should().BeTrue();
        attempts.Should().Be(2);
        stopwatch.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(30));
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
