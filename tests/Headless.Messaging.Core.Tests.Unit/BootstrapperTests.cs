// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.DistributedLocks;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Processor;
using Headless.Messaging.Runtime;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Tests;

public sealed class BootstrapperTests : TestBase
{
    [Fact]
    public async Task should_report_started_only_after_bootstrap_completes()
    {
        await using var blocker = new BlockingProcessingServer();
        await using var provider = _CreateProvider(beforeMessaging: blocker);
        var bootstrapper = provider.GetRequiredService<IBootstrapper>();

        var bootstrapTask = bootstrapper.BootstrapAsync(AbortToken);
        await blocker.WaitUntilStartedAsync(AbortToken);

        bootstrapper.IsStarted.Should().BeFalse();

        blocker.Release();
        await bootstrapTask;

        bootstrapper.IsStarted.Should().BeTrue();
    }

    [Fact]
    public async Task should_allow_non_owner_callers_to_cancel_wait_without_canceling_shared_bootstrap()
    {
        await using var blocker = new BlockingProcessingServer();
        await using var provider = _CreateProvider(beforeMessaging: blocker);
        var bootstrapper = provider.GetRequiredService<IBootstrapper>();

        var ownerTask = bootstrapper.BootstrapAsync(AbortToken);
        await blocker.WaitUntilStartedAsync(AbortToken);

        using var waiterCts = new CancellationTokenSource();
        var waiterTask = bootstrapper.BootstrapAsync(waiterCts.Token);
        await waiterCts.CancelAsync();

        var act = async () => await waiterTask;
        await act.Should().ThrowAsync<OperationCanceledException>();

        ownerTask.IsCompleted.Should().BeFalse();
        bootstrapper.IsStarted.Should().BeFalse();

        blocker.Release();
        await ownerTask;

        bootstrapper.IsStarted.Should().BeTrue();
    }

    [Fact]
    public async Task should_fail_bootstrap_when_required_processor_fails_to_start()
    {
        var failure = new InvalidOperationException("processor boom");
        await using var beforeMessaging = new FailingProcessingServer(failure);
        await using var provider = _CreateProvider(beforeMessaging: beforeMessaging);
        var bootstrapper = provider.GetRequiredService<IBootstrapper>();

        var act = async () => await bootstrapper.BootstrapAsync(AbortToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("processor boom");
        bootstrapper.IsStarted.Should().BeFalse();
    }

    [Fact]
    public async Task should_not_stop_runtime_when_owner_bootstrap_token_is_canceled_after_startup()
    {
        await using var processor = new TrackingProcessingServer();
        await using var provider = _CreateProvider(beforeMessaging: processor);
        var bootstrapper = provider.GetRequiredService<IBootstrapper>();
        using var ownerCts = new CancellationTokenSource();

        await bootstrapper.BootstrapAsync(ownerCts.Token);
        bootstrapper.IsStarted.Should().BeTrue();

        await ownerCts.CancelAsync();

        await Task.Delay(100, AbortToken);

        processor.DisposeCount.Should().Be(0);
        bootstrapper.IsStarted.Should().BeTrue();
    }

    [Fact]
    public async Task should_stop_started_processors_when_later_processor_fails_during_bootstrap()
    {
        await using var startedProcessor = new TrackingProcessingServer();
        var failure = new InvalidOperationException("processor boom");
        await using var afterMessaging = new FailingProcessingServer(failure);
        await using var provider = _CreateProvider(beforeMessaging: startedProcessor, afterMessaging: afterMessaging);
        var bootstrapper = provider.GetRequiredService<IBootstrapper>();

        var act = async () => await bootstrapper.BootstrapAsync(AbortToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("processor boom");
        startedProcessor.DisposeCount.Should().BePositive();
        bootstrapper.IsStarted.Should().BeFalse();
    }

    [Fact]
    public async Task should_log_warning_when_use_storage_lock_is_true_and_no_real_lock_provider_registered()
    {
        var captured = new List<(LogLevel Level, EventId EventId)>();
        await using var provider = _CreateProvider(
            captureLog: captured,
            configureOptions: o => o.UseStorageLock = true
        );
        var bootstrapper = provider.GetRequiredService<IBootstrapper>();

        await bootstrapper.BootstrapAsync(AbortToken);

        captured
            .Should()
            .Contain(
                e => e.Level == LogLevel.Warning && e.EventId.Id == 77,
                "UseStorageLockWithNoOpProvider warning must fire when only NullDistributedLock is registered"
            );
    }

    [Fact]
    public async Task should_not_log_warning_when_use_storage_lock_is_false()
    {
        var captured = new List<(LogLevel Level, EventId EventId)>();
        await using var provider = _CreateProvider(
            captureLog: captured,
            configureOptions: o => o.UseStorageLock = false
        );
        var bootstrapper = provider.GetRequiredService<IBootstrapper>();

        await bootstrapper.BootstrapAsync(AbortToken);

        captured
            .Should()
            .NotContain(
                e => e.Level == LogLevel.Warning && e.EventId.Id == 77,
                "warning must be silent when UseStorageLock is false, even with NullDistributedLock"
            );
    }

    [Fact]
    public async Task should_not_fall_back_to_floor_only_when_storage_lock_disabled_but_membership_is_real()
    {
        // Recovery is always-on (KTD3): with a real INodeMembership the DeadOwnerRecoveryBridge reclaims
        // dead owners regardless of UseStorageLock, so the bootstrapper must neither warn that recovery is
        // disabled (the removed EventId 92) nor emit the floor-only fallback info (EventId 88).
        var captured = new List<(LogLevel Level, EventId EventId)>();
        var membership = Substitute.For<INodeMembership>();
        membership.Identity.Returns(new NodeIdentity(new NodeId("node-a"), new NodeIncarnation(7)));

        await using var provider = _CreateProvider(
            captureLog: captured,
            configureOptions: o => o.UseStorageLock = false,
            extraSetup: services =>
            {
                services.RemoveAll<INodeMembership>();
                services.AddSingleton(membership);
            }
        );
        var bootstrapper = provider.GetRequiredService<IBootstrapper>();

        await bootstrapper.BootstrapAsync(AbortToken);

        captured
            .Should()
            .NotContain(
                e => e.EventId.Id == 88 || e.EventId.Id == 92,
                "a real membership means recovery is active via the always-on bridge, independent of UseStorageLock"
            );
    }

    [Fact]
    public async Task should_warn_with_eventid_78_when_unkeyed_real_provider_exists_but_use_distributed_lock_not_called()
    {
        // Misconfiguration repro: user wired up a real IDistributedLock (e.g. via
        // Headless.DistributedLocks.Redis) but forgot to call MessagingBuilder.UseDistributedLock(...).
        // The bootstrapper must emit EventId 78 (UseStorageLockWithNoOpProviderButRealUnkeyed) so the
        // operator can distinguish this case from EventId 77 (no provider at all).
        var captured = new List<(LogLevel Level, EventId EventId)>();
        var unkeyedRealProvider = Substitute.For<IDistributedLock>();

        await using var provider = _CreateProvider(
            captureLog: captured,
            configureOptions: o => o.UseStorageLock = true,
            extraSetup: services => services.AddSingleton(unkeyedRealProvider)
        );
        var bootstrapper = provider.GetRequiredService<IBootstrapper>();

        await bootstrapper.BootstrapAsync(AbortToken);

        captured
            .Should()
            .Contain(
                e => e.Level == LogLevel.Warning && e.EventId.Id == 78,
                "EventId 78 must fire when a real un-keyed IDistributedLock exists but UseDistributedLock(...) was not called"
            );
        captured
            .Should()
            .NotContain(
                e => e.Level == LogLevel.Warning && e.EventId.Id == 77,
                "EventId 77 (the no-provider-at-all case) must NOT fire when an un-keyed real provider exists"
            );
    }

    [Fact]
    public async Task should_not_log_warning_when_real_lock_provider_is_registered()
    {
        var captured = new List<(LogLevel Level, EventId EventId)>();
        var realProvider = Substitute.For<IDistributedLock>();
        await using var provider = _CreateProvider(
            captureLog: captured,
            builderAction: builder => builder.UseDistributedLock(realProvider)
        );
        var bootstrapper = provider.GetRequiredService<IBootstrapper>();

        await bootstrapper.BootstrapAsync(AbortToken);

        captured
            .Should()
            .NotContain(
                e => e.Level == LogLevel.Warning && e.EventId.Id == 77,
                "warning must be silent when a real IDistributedLock is registered"
            );
    }

    [Fact]
    public async Task should_fail_bootstrap_when_multiple_storage_providers_are_registered()
    {
        await using var provider = _CreateProvider(extraSetup: static services =>
            services.AddSingleton(new MessageStorageMarkerService("OtherStorage"))
        );
        var bootstrapper = provider.GetRequiredService<IBootstrapper>();

        var act = async () => await bootstrapper.BootstrapAsync(AbortToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*exactly one storage provider*");
        bootstrapper.IsStarted.Should().BeFalse();
    }

    [Fact]
    public async Task should_log_info_when_coordination_membership_is_null_and_storage_lock_is_enabled()
    {
        var captured = new List<(LogLevel Level, EventId EventId)>();
        await using var provider = _CreateProvider(
            captureLog: captured,
            configureOptions: o => o.UseStorageLock = true
        );
        var bootstrapper = provider.GetRequiredService<IBootstrapper>();

        await bootstrapper.BootstrapAsync(AbortToken);

        captured.Should().Contain(e => e.Level == LogLevel.Information && e.EventId.Id == 88);
    }

    [Fact]
    public async Task should_not_log_coordination_fallback_info_when_real_membership_is_registered()
    {
        var captured = new List<(LogLevel Level, EventId EventId)>();
        var membership = Substitute.For<INodeMembership>();
        membership.Identity.Returns(new NodeIdentity(new NodeId("node-a"), new NodeIncarnation(7)));

        await using var provider = _CreateProvider(
            captureLog: captured,
            configureOptions: o => o.UseStorageLock = true,
            extraSetup: services =>
            {
                services.RemoveAll<INodeMembership>();
                services.AddSingleton(membership);
            }
        );
        var bootstrapper = provider.GetRequiredService<IBootstrapper>();

        await bootstrapper.BootstrapAsync(AbortToken);

        captured.Should().NotContain(e => e.EventId.Id == 88);
    }

    [Fact]
    public async Task should_warn_when_dead_threshold_is_below_dispatch_timeout_with_real_membership()
    {
        // given — recovery active (real membership) but DeadThreshold (30s) < DispatchTimeout (5m): a still-alive
        // node crossing the dead threshold mid-dispatch would be reclaimed and re-dispatched.
        var captured = new List<(LogLevel Level, EventId EventId)>();
        var membership = Substitute.For<INodeMembership>();
        membership.Identity.Returns(new NodeIdentity(new NodeId("node-a"), new NodeIncarnation(7)));

        await using var provider = _CreateProvider(
            captureLog: captured,
            configureOptions: o => o.RetryPolicy.DispatchTimeout = TimeSpan.FromMinutes(5),
            extraSetup: services =>
            {
                services.RemoveAll<INodeMembership>();
                services.AddSingleton(membership);
                services.Configure<CoordinationOptions>(c => c.DeadThreshold = TimeSpan.FromSeconds(30));
            }
        );
        var bootstrapper = provider.GetRequiredService<IBootstrapper>();

        await bootstrapper.BootstrapAsync(AbortToken);

        captured.Should().Contain(e => e.Level == LogLevel.Warning && e.EventId.Id == 94);
    }

    [Fact]
    public async Task should_not_warn_when_dead_threshold_meets_dispatch_timeout()
    {
        // given — DeadThreshold (10m) >= DispatchTimeout (5m): the invariant holds, no duplicate-delivery window.
        var captured = new List<(LogLevel Level, EventId EventId)>();
        var membership = Substitute.For<INodeMembership>();
        membership.Identity.Returns(new NodeIdentity(new NodeId("node-a"), new NodeIncarnation(7)));

        await using var provider = _CreateProvider(
            captureLog: captured,
            configureOptions: o => o.RetryPolicy.DispatchTimeout = TimeSpan.FromMinutes(5),
            extraSetup: services =>
            {
                services.RemoveAll<INodeMembership>();
                services.AddSingleton(membership);
                services.Configure<CoordinationOptions>(c => c.DeadThreshold = TimeSpan.FromMinutes(10));
            }
        );
        var bootstrapper = provider.GetRequiredService<IBootstrapper>();

        await bootstrapper.BootstrapAsync(AbortToken);

        captured.Should().NotContain(e => e.EventId.Id == 94);
    }

    [Fact]
    public async Task should_isolate_messaging_lock_provider_from_unkeyed_app_level_provider()
    {
        // given — an app-level un-keyed provider AND a messaging-keyed provider
        var appLevelProvider = Substitute.For<IDistributedLock>();
        var messagingProvider = Substitute.For<IDistributedLock>();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(appLevelProvider);

        var messagingBuilder = services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            setup.UseInMemoryStorage();
        });
        messagingBuilder.UseDistributedLock(messagingProvider);

        await using var provider = services.BuildServiceProvider();

        // when — resolve the retry processor which injects the messaging-keyed provider via attribute
        var processor = provider.GetRequiredService<MessageNeedToRetryProcessor>();

        // then — un-keyed remains visible to app code, keyed remains messaging's
        provider.GetRequiredService<IDistributedLock>().Should().BeSameAs(appLevelProvider);
        provider
            .GetRequiredKeyedService<IDistributedLock>(MessagingKeys.LockProvider)
            .Should()
            .BeSameAs(messagingProvider);

        // The processor type itself is what we care about — it must hold the messaging-keyed one.
        // Exposed via internal helper (InternalsVisibleTo) instead of reflection so the test stays
        // resilient to private-field renames.
        var injected = processor.LockProvider;
        injected
            .Should()
            .BeSameAs(
                messagingProvider,
                "the processor must receive the messaging-keyed provider, not the un-keyed app-level one"
            );
        injected.Should().NotBeSameAs(appLevelProvider);
    }

    private ServiceProvider _CreateProvider(
        IProcessingServer? beforeMessaging = null,
        IProcessingServer? afterMessaging = null,
        List<(LogLevel Level, EventId EventId)>? captureLog = null,
        Action<MessagingOptions>? configureOptions = null,
        Action<IServiceCollection>? extraSetup = null,
        Action<MessagingBuilder>? builderAction = null
    )
    {
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.AddProvider(LoggerProvider);
            builder.SetMinimumLevel(LogLevel.Debug);

            if (captureLog is not null)
            {
                builder.AddProvider(new CapturingLoggerProvider(captureLog));
            }
        });

        if (beforeMessaging is not null)
        {
            services.AddSingleton(beforeMessaging);
        }

        var messagingBuilder = services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            setup.UseInMemoryStorage();
            setup.UseConventions(c =>
            {
                c.UseApplicationId("bootstrap-tests");
                c.UseVersion("v1");
            });
        });

        builderAction?.Invoke(messagingBuilder);

        if (configureOptions is not null)
        {
            services.Configure(configureOptions);
        }

        extraSetup?.Invoke(services);

        if (afterMessaging is not null)
        {
            services.AddSingleton(afterMessaging);
        }

        return services.BuildServiceProvider();
    }

    private sealed class BlockingProcessingServer : IProcessingServer
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask StartAsync(CancellationToken stoppingToken)
        {
            _started.TrySetResult();
            await _release.Task.WaitAsync(stoppingToken);
        }

        public async ValueTask WaitUntilStartedAsync(CancellationToken cancellationToken)
        {
            await _started.Task.WaitAsync(cancellationToken);
        }

        public void Release()
        {
            _release.TrySetResult();
        }

        public ValueTask DisposeAsync()
        {
            _release.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FailingProcessingServer(Exception exception) : IProcessingServer
    {
        public ValueTask StartAsync(CancellationToken stoppingToken)
        {
            return ValueTask.FromException(exception);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TrackingProcessingServer : IProcessingServer
    {
        public int DisposeCount => Volatile.Read(ref _disposeCount);

        private int _disposeCount;

        public ValueTask StartAsync(CancellationToken stoppingToken)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CapturingLoggerProvider(List<(LogLevel Level, EventId EventId)> log) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(log);

        public void Dispose() { }

        private sealed class CapturingLogger(List<(LogLevel Level, EventId EventId)> log) : ILogger
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
                lock (log)
                {
                    log.Add((logLevel, eventId));
                }
            }
        }
    }
}
