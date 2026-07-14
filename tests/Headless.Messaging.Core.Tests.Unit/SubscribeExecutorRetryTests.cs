// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Reflection;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Messaging.Retry;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tests.Helpers;

#pragma warning disable MA0015 // Specify the parameter name in ArgumentException
namespace Tests;

public sealed class SubscribeExecutorRetryTests : TestBase
{
    private static readonly IServiceProvider _EmptyScope = new ServiceCollection().BuildServiceProvider();

    private static MediumMessage _CreateMediumMessage()
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = Guid.NewGuid().ToString(),
            [Headers.MessageName] = "test.messageName",
            [Headers.Group] = "test-group",
        };

        return new MediumMessage
        {
            StorageId = Guid.NewGuid(),
            Origin = new Message(headers, "{}"),
            Content = "{}",
            IntentType = IntentType.Bus,
            Added = DateTime.UtcNow,
        };
    }

    private static ConsumerExecutorDescriptor _CreateDescriptor()
    {
        var consumeMethod = typeof(IConsume<CancellationExecutorTestMessage>).GetMethod(
            nameof(IConsume<>.ConsumeAsync),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            null,
            [typeof(ConsumeContext<CancellationExecutorTestMessage>), typeof(CancellationToken)],
            null
        )!;

        return new ConsumerExecutorDescriptor
        {
            IntentType = IntentType.Bus,
            ServiceTypeInfo = typeof(CancellationExecutorTestConsumer).GetTypeInfo(),
            ImplTypeInfo = typeof(CancellationExecutorTestConsumer).GetTypeInfo(),
            MethodInfo = consumeMethod,
            MessageName = "test.messageName",
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
            .LeaseReceiveAsync(Arg.Any<MediumMessage>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(true));
        storage
            .LeaseReceiveAndReserveAttemptAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));
        storage
            .ReserveReceiveAttemptAsync(Arg.Any<MediumMessage>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(true));
        storage
            .ChangeReceiveRetryStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<DateTime?>(),
                Arg.Any<DateTime?>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup =>
        {
            setup.ForMessage<CancellationExecutorTestMessage>(message =>
                message
                    .MessageName("test.messageName")
                    .OnBus<CancellationExecutorTestConsumer>(consumer => consumer.Group("test-group"))
            );
            setup.UseInMemory();
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
                Arg.Any<DateTime?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
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

                return Task.FromResult(new ConsumerExecutedResult(null, null, Guid.NewGuid().ToString(), null, null));
            });

        var executor = _CreateExecutor(
            invoker,
            storage,
            new MessagingOptions
            {
                RetryPolicy = { RetryStrategy = TestRetryStrategies.ZeroDelay(4), MaxPersistedRetries = 0 },
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
                Arg.Any<DateTime?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
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

                return Task.FromResult(new ConsumerExecutedResult(null, null, Guid.NewGuid().ToString(), null, null));
            });

        var executor = _CreateExecutor(
            invoker,
            storage,
            new MessagingOptions
            {
                RetryPolicy =
                {
                    RetryStrategy = TestRetryStrategies.FixedDelay(1, TimeSpan.FromMilliseconds(40)),
                    MaxPersistedRetries = 0,
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
                Arg.Any<DateTime?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
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
                    RetryStrategy = TestRetryStrategies.FixedDelay(0, TimeSpan.FromSeconds(5)),
                    MaxPersistedRetries = 1,
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
            .ChangeReceiveRetryStateAsync(
                Arg.Any<MediumMessage>(),
                StatusName.Failed,
                Arg.Is<DateTime?>(value => value.HasValue),
                Arg.Any<DateTime?>(),
                Arg.Is<int>(value => value == 0),
                Arg.Any<int>(),
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
                Arg.Any<DateTime?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
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
                Arg.Any<DateTime?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
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
                RetryPolicy = { RetryStrategy = TestRetryStrategies.PermanentArgument(3), MaxPersistedRetries = 4 },
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
            .ChangeReceiveRetryStateAsync(
                Arg.Any<MediumMessage>(),
                StatusName.Failed,
                Arg.Is<DateTime?>(v => v == null),
                Arg.Any<DateTime?>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
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
                Arg.Any<DateTime?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
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
                    RetryStrategy = TestRetryStrategies.ZeroDelay(0),
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
            .ChangeReceiveRetryStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<DateTime?>(),
                Arg.Any<DateTime?>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(false));

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
            new MessagingOptions
            {
                RetryPolicy = { RetryStrategy = TestRetryStrategies.ZeroDelay(0), MaxPersistedRetries = 0 },
            }
        );

        // Override the happy-path stub from _CreateExecutor so the fresh-dispatch combined
        // lease+reserve write reports lease contention.
        storage
            .LeaseReceiveAndReserveAttemptAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
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
                RetryStrategy = TestRetryStrategies.FixedDelay(2, TimeSpan.FromSeconds(1)),
                MaxPersistedRetries = 1,
                InitialDispatchGrace = TimeSpan.FromSeconds(5),
            },
        };

        var storage = Substitute.For<IDataStorage>();
        storage
            .ChangeReceiveStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<DateTime?>(),
                Arg.Any<DateTime?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
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

                return Task.FromResult(new ConsumerExecutedResult(null, null, Guid.NewGuid().ToString(), null, null));
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
            .ChangeReceiveRetryStateAsync(
                Arg.Any<MediumMessage>(),
                StatusName.Scheduled,
                Arg.Is<DateTime?>(v => v > nowBefore.Add(options.RetryPolicy.InitialDispatchGrace)),
                Arg.Any<DateTime?>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_increment_retries_by_one_when_persisted_path_routes_via_change_receive_state()
    {
        // #11 — positive counterpart to the inline-path invariant: when the inline budget is fully
        // consumed (MaxRetryAttempts = 0) and the persisted budget still has slots, ResolveNextState
        // routes through persistence and the executor MUST increment MediumMessage.Retries by
        // exactly one. Without the increment the persisted budget would never be consumed and
        // OnExhausted would never fire.
        var storage = Substitute.For<IDataStorage>();
        storage
            .ChangeReceiveStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<DateTime?>(),
                Arg.Any<DateTime?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
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
                    RetryStrategy = TestRetryStrategies.FixedDelay(0, TimeSpan.FromSeconds(5)),
                    MaxPersistedRetries = 5,
                },
            }
        );

        var message = _CreateMediumMessage();
        var startingRetries = message.Retries;

        // when
        var result = await executor.ExecuteAsync(message, _EmptyScope, _CreateDescriptor(), CancellationToken.None);

        // then — persisted-retry transition: Retries advanced from 0 to 1 and the storage write
        // was issued with originalRetries == 0 (CAS predicate on the pre-increment value).
        result.Succeeded.Should().BeFalse();
        message
            .Retries.Should()
            .Be(
                startingRetries + 1,
                "persisted pickup must advance MediumMessage.Retries by exactly 1 so the budget is consumed"
            );
        await storage
            .Received(1)
            .ChangeReceiveRetryStateAsync(
                Arg.Any<MediumMessage>(),
                StatusName.Failed,
                Arg.Is<DateTime?>(v => v.HasValue),
                Arg.Any<DateTime?>(),
                Arg.Is<int>(v => v == startingRetries),
                Arg.Any<int>(),
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
                Arg.Any<DateTime?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
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
                    RetryStrategy = TestRetryStrategies.FixedDelay(3, TimeSpan.FromMilliseconds(1)),
                    MaxPersistedRetries = 5,
                    // Default strategy — relies on RetryExceptionClassifier to detect permanent
                    // failures. The classifier sees the wrapped exception and must unwrap it.
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
            .ChangeReceiveRetryStateAsync(
                Arg.Any<MediumMessage>(),
                StatusName.Failed,
                Arg.Is<DateTime?>(v => v == null),
                Arg.Any<DateTime?>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_not_invoke_consumer_when_recovery_finds_reserved_inline_budget_consumed()
    {
        var storage = Substitute.For<IDataStorage>();
        storage
            .ChangeReceiveStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<DateTime?>(),
                Arg.Any<DateTime?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));
        var invoker = Substitute.For<ISubscribeInvoker>();
        var executor = _CreateExecutor(
            invoker,
            storage,
            new MessagingOptions
            {
                RetryPolicy = { RetryStrategy = TestRetryStrategies.ZeroDelay(2), MaxPersistedRetries = 1 },
            }
        );
        var message = _CreateMediumMessage();
        message.InlineAttempts = 3;

        await executor.ExecuteAsync(message, _EmptyScope, _CreateDescriptor(), CancellationToken.None);

        await invoker.DidNotReceiveWithAnyArgs().InvokeAsync(default!, AbortToken);
        message.Retries.Should().Be(1);
        message.InlineAttempts.Should().Be(0);
        await storage
            .Received(1)
            .ChangeReceiveRetryStateAsync(
                Arg.Is<MediumMessage>(value => value.Retries == 1 && value.InlineAttempts == 0),
                StatusName.Failed,
                Arg.Is<DateTime?>(value => value.HasValue),
                Arg.Is<DateTime?>(value => value == null),
                Arg.Is(0),
                Arg.Is(3),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_not_invoke_consumer_or_write_state_when_attempt_reservation_is_lost()
    {
        var storage = Substitute.For<IDataStorage>();
        var invoker = Substitute.For<ISubscribeInvoker>();
        var executor = _CreateExecutor(invoker, storage, new MessagingOptions());
        storage
            .ReserveReceiveAttemptAsync(Arg.Any<MediumMessage>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(false));
        var message = _CreateMediumMessage();
        // Pre-leased row (as after an atomic pickup) so the standalone mid-burst reservation path
        // is exercised rather than the fresh-dispatch combined lease+reserve write.
        message.LockedUntil = DateTime.UtcNow.AddMinutes(5);

        var result = await executor.ExecuteAsync(message, _EmptyScope, _CreateDescriptor(), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        message.InlineAttempts.Should().Be(0);
        await invoker.DidNotReceiveWithAnyArgs().InvokeAsync(default!, AbortToken);
        await storage
            .DidNotReceiveWithAnyArgs()
            .ChangeReceiveRetryStateAsync(default!, default, default, default, default, default, AbortToken);
    }

    [Fact]
    public async Task should_issue_single_combined_storage_write_before_fresh_consume()
    {
        var storage = Substitute.For<IDataStorage>();
        var invoker = Substitute.For<ISubscribeInvoker>();
        invoker
            .InvokeAsync(Arg.Any<ConsumerContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ConsumerExecutedResult(null, null, Guid.NewGuid().ToString(), null, null)));
        var executor = _CreateExecutor(invoker, storage, new MessagingOptions());
        var message = _CreateMediumMessage();

        var result = await executor.ExecuteAsync(message, _EmptyScope, _CreateDescriptor(), CancellationToken.None);

        // A fresh (never-leased) consume must pay exactly one pre-attempt storage write: the
        // combined lease+reserve statement, never the two-step lease-then-reserve pair.
        result.Succeeded.Should().BeTrue();
        await storage
            .Received(1)
            .LeaseReceiveAndReserveAttemptAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            );
        await storage.DidNotReceiveWithAnyArgs().LeaseReceiveAsync(default!, default, AbortToken);
        await storage.DidNotReceiveWithAnyArgs().ReserveReceiveAttemptAsync(default!, default, AbortToken);
    }

    private sealed class ScopedMarker;
}
