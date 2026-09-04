// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.DistributedLocks;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Persistence;
using Headless.Messaging.Processor;
using Headless.Messaging.Runtime;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

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
    public async Task synchronous_dispose_should_not_wait_for_async_processor_cleanup()
    {
        await using var processor = new BlockingDisposeProcessingServer();
        await using var provider = _CreateProvider(beforeMessaging: processor);
        var bootstrapper = provider.GetRequiredService<IBootstrapper>();

        await bootstrapper.BootstrapAsync(AbortToken);

        var disposeTask = Task.Run(() => ((IDisposable)bootstrapper).Dispose(), AbortToken);
        await processor.WaitUntilDisposeStartedAsync(AbortToken);

        try
        {
            await disposeTask.WaitAsync(TimeSpan.FromSeconds(2), AbortToken);
        }
        finally
        {
            processor.ReleaseDispose();
            await disposeTask.WaitAsync(TimeSpan.FromSeconds(2), AbortToken);
        }
    }

    [Fact]
    public async Task concurrent_stop_and_dispose_should_stop_processors_once()
    {
        await using var processor = new TrackingProcessingServer();
        await using var provider = _CreateProvider(beforeMessaging: processor);
        var bootstrapper = provider.GetRequiredService<IBootstrapper>();

        await bootstrapper.BootstrapAsync(AbortToken);

        var hostedService = (IHostedService)bootstrapper;
        await Task.WhenAll(
            hostedService.StopAsync(AbortToken),
            bootstrapper.DisposeAsync().AsTask(),
            hostedService.StopAsync(AbortToken)
        );

        processor.DisposeCount.Should().Be(1);
        bootstrapper.IsStarted.Should().BeFalse();
    }

    [Fact]
    public async Task stop_should_return_at_configured_shutdown_boundary_when_processor_cleanup_blocks()
    {
        var timeProvider = new FakeTimeProvider();
        await using var processor = new BlockingDisposeProcessingServer();
        await using var provider = _CreateProvider(
            beforeMessaging: processor,
            configureOptions: options => options.ShutdownTimeout = TimeSpan.FromSeconds(2),
            extraSetup: services => services.AddSingleton<TimeProvider>(timeProvider)
        );
        var bootstrapper = provider.GetRequiredService<IBootstrapper>();

        await bootstrapper.BootstrapAsync(AbortToken);

        var stopTask = ((IHostedService)bootstrapper).StopAsync(CancellationToken.None);
        await processor.WaitUntilDisposeStartedAsync(AbortToken);
        stopTask.IsCompleted.Should().BeFalse();

        await Task.Yield();
        timeProvider.Advance(TimeSpan.FromSeconds(2));

        try
        {
            await stopTask.WaitAsync(TimeSpan.FromSeconds(2), AbortToken);
        }
        finally
        {
            processor.ReleaseDispose();
        }
    }

    [Fact]
    public async Task shutdown_quiesces_all_processors_before_concurrent_drain_and_uses_one_deadline()
    {
        var timeProvider = new FakeTimeProvider();
        await using var later = new PhasedProcessingServer();
        await using var first = new PhasedProcessingServer(blockDrain: true);
        first.AllQuiesced = () => first.IsQuiesced && later.IsQuiesced;
        later.AllQuiesced = first.AllQuiesced;
        await using var provider = _CreateProvider(
            beforeMessaging: later,
            afterMessaging: first,
            configureOptions: options => options.ShutdownTimeout = TimeSpan.FromSeconds(2),
            extraSetup: services => services.AddSingleton<TimeProvider>(timeProvider)
        );
        var bootstrapper = provider.GetRequiredService<IBootstrapper>();
        await bootstrapper.BootstrapAsync(AbortToken);

        var stopTask = ((IHostedService)bootstrapper).StopAsync(CancellationToken.None);
        await Task.WhenAll(first.WaitUntilStopStartedAsync(AbortToken), later.WaitUntilStopStartedAsync(AbortToken));

        try
        {
            first.SawAllQuiescedAtDrain.Should().BeTrue();
            later.SawAllQuiescedAtDrain.Should().BeTrue();
            first.Timeout.Should().Be(TimeSpan.FromSeconds(2));
            later.Timeout.Should().Be(TimeSpan.FromSeconds(2));

            timeProvider.Advance(TimeSpan.FromSeconds(2));
            await stopTask.WaitAsync(TimeSpan.FromSeconds(2), AbortToken);
        }
        finally
        {
            first.ReleaseDrain();
        }
    }

    [Fact]
    public async Task blocking_third_party_teardown_starts_after_built_in_quiesce_without_starving_drain()
    {
        var timeProvider = new FakeTimeProvider();
        await using var builtIn = new PhasedProcessingServer();
        await using var thirdParty = new SynchronouslyBlockingDisposeProcessingServer
        {
            AllBuiltInsQuiesced = () => builtIn.IsQuiesced,
        };
        await using var provider = _CreateProvider(
            beforeMessaging: builtIn,
            afterMessaging: thirdParty,
            configureOptions: options => options.ShutdownTimeout = TimeSpan.FromSeconds(2),
            extraSetup: services => services.AddSingleton<TimeProvider>(timeProvider)
        );
        var bootstrapper = provider.GetRequiredService<IBootstrapper>();
        await bootstrapper.BootstrapAsync(AbortToken);

        var stopTask = ((IHostedService)bootstrapper).StopAsync(CancellationToken.None);
        await Task.WhenAll(
            thirdParty.WaitUntilDisposeEnteredAsync(AbortToken),
            builtIn.WaitUntilStopStartedAsync(AbortToken)
        );

        try
        {
            thirdParty.SawAllBuiltInsQuiesced.Should().BeTrue();
            builtIn.SawAllQuiescedAtDrain.Should().BeTrue();
            timeProvider.Advance(TimeSpan.FromSeconds(2));
            await stopTask.WaitAsync(TimeSpan.FromSeconds(2), AbortToken);
        }
        finally
        {
            thirdParty.ReleaseDispose();
        }
    }

    [Fact]
    public async Task shutdown_quiesces_published_processors_while_storage_initialization_is_stuck()
    {
        var timeProvider = new FakeTimeProvider();
        await using var processor = new PhasedProcessingServer();
        var initializer = new BlockingStorageInitializer();
        await using var provider = _CreateProvider(
            beforeMessaging: processor,
            configureOptions: options => options.ShutdownTimeout = TimeSpan.FromSeconds(2),
            extraSetup: services =>
            {
                services.AddSingleton<TimeProvider>(timeProvider);
                services.RemoveAll<IStorageInitializer>();
                services.AddSingleton<IStorageInitializer>(initializer);
            }
        );
        var bootstrapper = provider.GetRequiredService<IBootstrapper>();
        var bootstrapTask = bootstrapper.BootstrapAsync(CancellationToken.None);
        await initializer.WaitUntilStartedAsync(AbortToken);

        try
        {
            var stopTask = ((IHostedService)bootstrapper).StopAsync(CancellationToken.None);
            await processor.WaitUntilStopStartedAsync(AbortToken);

            processor.IsQuiesced.Should().BeTrue();
            processor.StartCount.Should().Be(0);

            timeProvider.Advance(TimeSpan.FromSeconds(2));
            await stopTask.WaitAsync(TimeSpan.FromSeconds(2), AbortToken);
        }
        finally
        {
            initializer.Release();
        }

        try
        {
            await bootstrapTask;
        }
        catch (OperationCanceledException)
        {
            // Shutdown may cancel the initializer before or immediately after its release.
        }
        processor.StartCount.Should().Be(0);
    }

    [Fact]
    public async Task stop_should_aggregate_every_processor_stop_failure()
    {
        var firstFailure = new InvalidOperationException("first stop boom");
        var secondFailure = new InvalidOperationException("second stop boom");
        await using var first = new FailingStopProcessingServer(firstFailure);
        await using var healthy = new TrackingProcessingServer();
        await using var second = new FailingStopProcessingServer(secondFailure);
        var provider = _CreateProvider(
            beforeMessaging: first,
            afterMessaging: second,
            extraSetup: services => services.AddSingleton<IProcessingServer>(healthy)
        );
        var bootstrapper = provider.GetRequiredService<IBootstrapper>();
        await bootstrapper.BootstrapAsync(AbortToken);

        var act = async () => await ((IHostedService)bootstrapper).StopAsync(CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<AggregateException>();
        thrown.Which.InnerExceptions.Should().BeEquivalentTo([firstFailure, secondFailure]);
        healthy.DisposeCount.Should().Be(1, "a faulting sibling must not prevent the other processors from stopping");
        bootstrapper.IsStarted.Should().BeFalse();

        // Shutdown is one shared idempotent operation, so disposing the provider observes the same
        // aggregated outcome instead of retrying the stop.
        var dispose = async () => await provider.DisposeAsync();
        await dispose.Should().ThrowAsync<AggregateException>();
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

    [Theory]
    [InlineData(120, false)]
    [InlineData(121, true)]
    public async Task should_warn_only_when_dispatch_timeout_exceeds_initial_grace_by_more_than_two_minutes(
        int differenceSeconds,
        bool shouldWarn
    )
    {
        var captured = new List<(LogLevel Level, EventId EventId)>();
        await using var provider = _CreateProvider(
            captureLog: captured,
            configureOptions: options =>
            {
                options.RetryPolicy.InitialDispatchGrace = TimeSpan.FromMinutes(1);
                options.RetryPolicy.DispatchTimeout =
                    options.RetryPolicy.InitialDispatchGrace + TimeSpan.FromSeconds(differenceSeconds);
            }
        );
        var bootstrapper = provider.GetRequiredService<IBootstrapper>();

        await bootstrapper.BootstrapAsync(AbortToken);

        captured.Any(entry => entry.Level == LogLevel.Warning && entry.EventId.Id == 97).Should().Be(shouldWarn);
    }

    [Fact]
    public async Task should_warn_at_one_tick_past_dispatch_timeout_threshold()
    {
        var captured = new List<(LogLevel Level, EventId EventId)>();
        await using var provider = _CreateProvider(
            captureLog: captured,
            configureOptions: options =>
            {
                options.RetryPolicy.InitialDispatchGrace = TimeSpan.FromMinutes(1);
                options.RetryPolicy.DispatchTimeout =
                    options.RetryPolicy.InitialDispatchGrace + TimeSpan.FromMinutes(2) + TimeSpan.FromTicks(1);
            }
        );

        await provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);

        captured.Should().Contain(entry => entry.Level == LogLevel.Warning && entry.EventId.Id == 97);
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
            services.AddMessagingProviderCapabilities(
                MessagingProviderCapabilities.Storage(
                    "OtherStorage",
                    [MessageLane.Bus, MessageLane.Queue],
                    supportsDelayedScheduling: true
                )
            )
        );
        var bootstrapper = provider.GetRequiredService<IBootstrapper>();

        var act = async () => await bootstrapper.BootstrapAsync(AbortToken);

        await act.Should().ThrowAsync<MessagingConfigurationException>().WithMessage("*exactly one storage provider*");
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

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FailingStopProcessingServer(Exception exception) : IProcessingServer, IProcessingServerShutdown
    {
        public ValueTask StartAsync(CancellationToken stoppingToken)
        {
            return ValueTask.CompletedTask;
        }

        public void Quiesce() { }

        public ValueTask StopAsync(TimeSpan timeout)
        {
            return ValueTask.FromException(exception);
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
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

    private sealed class BlockingDisposeProcessingServer : IProcessingServer
    {
        private readonly TaskCompletionSource _disposeStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _disposeRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask StartAsync(CancellationToken stoppingToken)
        {
            return ValueTask.CompletedTask;
        }

        public async ValueTask WaitUntilDisposeStartedAsync(CancellationToken cancellationToken)
        {
            await _disposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
        }

        public void ReleaseDispose()
        {
            _disposeRelease.TrySetResult();
        }

        public ValueTask DisposeAsync()
        {
            _disposeStarted.TrySetResult();
            return new ValueTask(_disposeRelease.Task);
        }
    }

    private sealed class SynchronouslyBlockingDisposeProcessingServer : IProcessingServer
    {
        private readonly TaskCompletionSource _disposeEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim _disposeRelease = new(initialState: false);
        private int _disposed;

        public required Func<bool> AllBuiltInsQuiesced { get; init; }
        public bool SawAllBuiltInsQuiesced { get; private set; }

        public ValueTask StartAsync(CancellationToken stoppingToken)
        {
            return ValueTask.CompletedTask;
        }

        public Task WaitUntilDisposeEnteredAsync(CancellationToken cancellationToken)
        {
            return _disposeEntered.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
        }

        public void ReleaseDispose()
        {
            _disposeRelease.Set();
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return ValueTask.CompletedTask;
            }

            SawAllBuiltInsQuiesced = AllBuiltInsQuiesced();
            _disposeEntered.TrySetResult();
            _disposeRelease.Wait();
            _disposeRelease.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class PhasedProcessingServer(bool blockDrain = false) : IProcessingServer, IProcessingServerShutdown
    {
        private readonly TaskCompletionSource _stopStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _stopRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Func<bool>? AllQuiesced { get; set; }
        public bool IsQuiesced => Volatile.Read(ref _isQuiesced) != 0;
        public bool SawAllQuiescedAtDrain { get; private set; }
        public int StartCount => Volatile.Read(ref _startCount);
        public TimeSpan? Timeout { get; private set; }
        private int _isQuiesced;
        private int _startCount;

        public ValueTask StartAsync(CancellationToken stoppingToken)
        {
            Interlocked.Increment(ref _startCount);
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            return StopAsync(TimeSpan.MaxValue);
        }

        public void Quiesce()
        {
            Interlocked.Exchange(ref _isQuiesced, 1);
        }

        public async ValueTask StopAsync(TimeSpan timeout)
        {
            Timeout = timeout;
            SawAllQuiescedAtDrain = AllQuiesced?.Invoke() ?? IsQuiesced;
            _stopStarted.TrySetResult();
            if (blockDrain)
            {
                await _stopRelease.Task.ConfigureAwait(false);
            }
        }

        public Task WaitUntilStopStartedAsync(CancellationToken cancellationToken)
        {
            return _stopStarted.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
        }

        public void ReleaseDrain()
        {
            _stopRelease.TrySetResult();
        }
    }

    private sealed class BlockingStorageInitializer : IStorageInitializer
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            _started.TrySetResult();
            await _release.Task.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }

        public string GetPublishedTableName()
        {
            return "published";
        }

        public string GetReceivedTableName()
        {
            return "received";
        }

        public Task WaitUntilStartedAsync(CancellationToken cancellationToken)
        {
            return _started.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
        }

        public void Release()
        {
            _release.TrySetResult();
        }
    }

    private sealed class CapturingLoggerProvider(List<(LogLevel Level, EventId EventId)> log) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName)
        {
            return new CapturingLogger(log);
        }

        public void Dispose() { }

        private sealed class CapturingLogger(List<(LogLevel Level, EventId EventId)> log) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
            {
                return null;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

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
