// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Reflection;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Messaging.Retry;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tests.Helpers;

namespace Tests;

public sealed class SubscribeExecutorRetryTests : TestBase
{
    private static readonly IServiceProvider _EmptyScope = new ServiceCollection().BuildServiceProvider();

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
            StorageId = 1L,
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
        storage
            .LeaseReceiveAsync(Arg.Any<MediumMessage>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(true));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup =>
        {
            setup.Subscribe<CancellationExecutorTestConsumer>().Topic("test.topic").Group("test-group");
            setup.UseInMemoryMessageQueue();
            setup.UseInMemoryStorage();
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
            .ChangeReceiveStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<DateTime?>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));

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

        var message = _CreateMediumMessage();

        // when
        var result = await executor.ExecuteAsync(message, _EmptyScope, _CreateDescriptor(), CancellationToken.None);

        // then
        result.Succeeded.Should().BeTrue();
        attempts.Should().Be(5);
        // Inline retries do not increment MediumMessage.Retries (which now counts persisted pickups
        // only). The 5th attempt succeeded inline, so no persist-transition ever happened.
        message.Retries.Should().Be(0);
    }

    [Fact]
    public async Task should_apply_backoff_delay_before_retrying()
    {
        // given
        var storage = Substitute.For<IDataStorage>();
        storage
            .ChangeReceiveStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<DateTime?>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));

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
                RetryPolicy =
                {
                    MaxInlineRetries = 1,
                    MaxPersistedRetries = 0,
                    BackoffStrategy = new FixedDelayRetryBackoffStrategy(TimeSpan.FromMilliseconds(40)),
                },
            }
        );

        var message = _CreateMediumMessage();
        var stopwatch = Stopwatch.StartNew();

        // when
        var result = await executor.ExecuteAsync(message, _EmptyScope, _CreateDescriptor(), CancellationToken.None);

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
            .ChangeReceiveStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<DateTime?>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));

        var invoker = Substitute.For<ISubscribeInvoker>();
        invoker
            .InvokeAsync(Arg.Any<ConsumerContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ConsumerExecutedResult>(new TimeoutException("boom")));

        var executor = _CreateExecutor(
            invoker,
            storage,
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

        var message = _CreateMediumMessage();

        // when
        var result = await executor.ExecuteAsync(message, _EmptyScope, _CreateDescriptor(), CancellationToken.None);

        // then
        result.Succeeded.Should().BeFalse();
        await storage
            .Received(1)
            .ChangeReceiveStateAsync(
                Arg.Any<MediumMessage>(),
                StatusName.Failed,
                Arg.Is<DateTime?>(value => value.HasValue),
                Arg.Any<DateTime?>(),
                Arg.Is<int?>(value => value == 0),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task on_exhausted_callback_should_resolve_same_scoped_service_as_dispatch_scope()
    {
        // given — a Scoped marker service. The caller (Dispatcher) creates a scope and
        // passes its IServiceProvider; the executor must surface that SAME provider through
        // FailedInfo.ServiceProvider so OnExhausted sees the live per-message scope.
        var rootServices = new ServiceCollection();
        rootServices.AddScoped<ScopedMarker>();
        var rootProvider = rootServices.BuildServiceProvider();
        using var dispatchScope = rootProvider.CreateScope();
        var expected = dispatchScope.ServiceProvider.GetRequiredService<ScopedMarker>();

        var storage = Substitute.For<IDataStorage>();
        storage
            .ChangeReceiveStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<DateTime?>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));

        var invoker = Substitute.For<ISubscribeInvoker>();
        invoker
            .InvokeAsync(Arg.Any<ConsumerContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ConsumerExecutedResult>(new TimeoutException("boom")));

        ScopedMarker? observed = null;
        var executor = _CreateExecutor(
            invoker,
            storage,
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
        await executor.ExecuteAsync(
            _CreateMediumMessage(),
            dispatchScope.ServiceProvider,
            _CreateDescriptor(),
            CancellationToken.None
        );

        // then — same scope means same Scoped instance
        observed.Should().NotBeNull();
        observed.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task should_not_retry_on_permanent_exception()
    {
        // A strategy that classifies ArgumentException as permanent (returns Stop) must result
        // in exactly one invocation and a Failed state update with no NextRetryAt — mirroring the
        // Stop outcome produced by RetryHelper for non-retryable exceptions.
        var storage = Substitute.For<IDataStorage>();
        storage
            .ChangeReceiveStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<DateTime?>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));

        var invoker = Substitute.For<ISubscribeInvoker>();
        invoker
            .InvokeAsync(Arg.Any<ConsumerContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ConsumerExecutedResult>(new ArgumentException("invalid param")));

        var executor = _CreateExecutor(
            invoker,
            storage,
            new MessagingOptions
            {
                RetryPolicy =
                {
                    MaxInlineRetries = 3,
                    MaxPersistedRetries = 4,
                    BackoffStrategy = new PermanentForArgumentExceptionStrategy(),
                },
            }
        );

        var message = _CreateMediumMessage();

        // when
        var result = await executor.ExecuteAsync(message, _EmptyScope, _CreateDescriptor(), CancellationToken.None);

        // then — permanent failure: no retries, Failed state persisted with no next-retry timestamp
        result.Succeeded.Should().BeFalse();
        await invoker.Received(1).InvokeAsync(Arg.Any<ConsumerContext>(), Arg.Any<CancellationToken>());
        message.Retries.Should().Be(0, "permanent failures must not advance the retry counter");
        await storage
            .Received(1)
            .ChangeReceiveStateAsync(
                Arg.Any<MediumMessage>(),
                StatusName.Failed,
                Arg.Is<DateTime?>(v => v == null),
                cancellationToken: Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_skip_on_exhausted_when_status_already_failed_when_redelivered()
    {
        // given — simulate redelivery where storage is already terminal (Succeeded/Failed).
        // ChangeReceiveStateAsync returns false; executor must skip OnExhausted.
        var storage = Substitute.For<IDataStorage>();
        storage
            .ChangeReceiveStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<DateTime?>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(false));

        var invoker = Substitute.For<ISubscribeInvoker>();
        invoker
            .InvokeAsync(Arg.Any<ConsumerContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ConsumerExecutedResult>(new TimeoutException("boom")));

        var callbackInvoked = false;
        var executor = _CreateExecutor(
            invoker,
            storage,
            new MessagingOptions
            {
                RetryPolicy =
                {
                    MaxInlineRetries = 0,
                    MaxPersistedRetries = 0,
                    BackoffStrategy = new ZeroDelayRetryBackoffStrategy(),
                    OnExhausted = (_, _) =>
                    {
                        callbackInvoked = true;
                        return Task.CompletedTask;
                    },
                },
            }
        );

        // when
        await executor.ExecuteAsync(_CreateMediumMessage(), _EmptyScope, _CreateDescriptor(), CancellationToken.None);

        // then
        callbackInvoked.Should().BeFalse("OnExhausted must be skipped if storage update returned false");
    }

    [Fact]
    public async Task should_stop_without_invoking_consumer_when_lease_rejects_terminal_row()
    {
        // given — lease returns false (storage proves the row is terminal). Executor must
        // short-circuit without invoking the consumer body and without writing any state.
        var invoker = Substitute.For<ISubscribeInvoker>();
        var storage = Substitute.For<IDataStorage>();

        var executor = _CreateExecutor(
            invoker,
            storage,
            new MessagingOptions { RetryPolicy = { MaxInlineRetries = 0, MaxPersistedRetries = 0 } }
        );

        // Override the happy-path lease stub from _CreateExecutor so the lease returns false.
        storage
            .LeaseReceiveAsync(Arg.Any<MediumMessage>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(false));

        // when
        var result = await executor.ExecuteAsync(
            _CreateMediumMessage(),
            _EmptyScope,
            _CreateDescriptor(),
            CancellationToken.None
        );

        // then
        result.Succeeded.Should().BeTrue();
        await invoker.DidNotReceive().InvokeAsync(Arg.Any<ConsumerContext>(), Arg.Any<CancellationToken>());
        await storage
            .DidNotReceive()
            .ChangeReceiveStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<DateTime?>(),
                Arg.Any<DateTime?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_persist_inline_in_flight_state_with_scheduled_status_and_padded_next_retry_at()
    {
        // ResolveNextState inline-in-flight branch: after the first failure but BEFORE inline
        // exhausts, the executor must call ChangeReceiveStateAsync with Scheduled (not Failed)
        // and a NextRetryAt at least InitialDispatchGrace in the future, so a crash mid-delay
        // leaves the row pickup-eligible by the polling query.
        var options = new MessagingOptions
        {
            RetryPolicy =
            {
                MaxInlineRetries = 2,
                MaxPersistedRetries = 1,
                BackoffStrategy = new FixedDelayRetryBackoffStrategy(TimeSpan.FromSeconds(1)),
                InitialDispatchGrace = TimeSpan.FromSeconds(5),
            },
        };

        var storage = Substitute.For<IDataStorage>();
        storage
            .ChangeReceiveStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<DateTime?>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));

        var invoker = Substitute.For<ISubscribeInvoker>();
        var attempt = 0;
        invoker
            .InvokeAsync(Arg.Any<ConsumerContext>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                attempt++;
                if (attempt == 1)
                {
                    return Task.FromException<ConsumerExecutedResult>(new TimeoutException("boom"));
                }

                return Task.FromResult(new ConsumerExecutedResult(null, Guid.NewGuid().ToString(), null, null));
            });

        var executor = _CreateExecutor(invoker, storage, options);
        var nowBefore = DateTime.UtcNow;
        var message = _CreateMediumMessage();

        // when
        var result = await executor.ExecuteAsync(message, _EmptyScope, _CreateDescriptor(), CancellationToken.None);

        // then — inline-in-flight write happened exactly once with Scheduled status and a
        // padded NextRetryAt (≥ InitialDispatchGrace past first-failure resume point).
        result.Succeeded.Should().BeTrue();
        await storage
            .Received(1)
            .ChangeReceiveStateAsync(
                Arg.Any<MediumMessage>(),
                StatusName.Scheduled,
                Arg.Is<DateTime?>(v => v.HasValue && v.Value > nowBefore.Add(options.RetryPolicy.InitialDispatchGrace)),
                Arg.Any<DateTime?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_treat_wrapped_argument_exception_as_permanent_with_default_strategy()
    {
        // #6: SubscribeExecutor wraps every consumer exception in SubscriberExecutionFailedException.
        // The default backoff strategies (FixedInterval / Exponential) classify via
        // RetryExceptionClassifier. The classifier MUST unwrap the wrapper so consumer code throwing
        // ArgumentException terminates after 1 attempt (Stop), not 48× retries.
        var storage = Substitute.For<IDataStorage>();
        storage
            .ChangeReceiveStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<DateTime?>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));

        var invoker = Substitute.For<ISubscribeInvoker>();
        invoker
            .InvokeAsync(Arg.Any<ConsumerContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ConsumerExecutedResult>(new ArgumentException("user error")));

        var callbackInvoked = false;
        var executor = _CreateExecutor(
            invoker,
            storage,
            new MessagingOptions
            {
                RetryPolicy =
                {
                    MaxInlineRetries = 3,
                    MaxPersistedRetries = 5,
                    // Default strategy — relies on RetryExceptionClassifier to detect permanent
                    // failures. The classifier sees the wrapped exception and must unwrap it.
                    BackoffStrategy = new FixedIntervalBackoffStrategy(TimeSpan.FromMilliseconds(1)),
                    OnExhausted = (_, _) =>
                    {
                        callbackInvoked = true;
                        return Task.CompletedTask;
                    },
                },
            }
        );

        var message = _CreateMediumMessage();

        // when
        var result = await executor.ExecuteAsync(message, _EmptyScope, _CreateDescriptor(), CancellationToken.None);

        // then — Stop path: exactly one invocation, OnExhausted does NOT fire.
        result.Succeeded.Should().BeFalse();
        await invoker.Received(1).InvokeAsync(Arg.Any<ConsumerContext>(), Arg.Any<CancellationToken>());
        message.Retries.Should().Be(0);
        callbackInvoked.Should().BeFalse("Stop path skips OnExhausted — only Exhausted fires it");
        await storage
            .Received(1)
            .ChangeReceiveStateAsync(
                Arg.Any<MediumMessage>(),
                StatusName.Failed,
                Arg.Is<DateTime?>(v => v == null),
                cancellationToken: Arg.Any<CancellationToken>()
            );
    }

    private sealed class ScopedMarker;
}
