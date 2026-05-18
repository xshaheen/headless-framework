using Headless.DistributedLocks;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tests;

public sealed class BootstrapperTests : TestBase
{
    [Fact]
    public async Task should_report_started_only_after_bootstrap_completes()
    {
        var blocker = new BlockingProcessingServer();
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
        var blocker = new BlockingProcessingServer();
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
        await using var provider = _CreateProvider(beforeMessaging: new FailingProcessingServer(failure));
        var bootstrapper = provider.GetRequiredService<IBootstrapper>();

        var act = async () => await bootstrapper.BootstrapAsync(AbortToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("processor boom");
        bootstrapper.IsStarted.Should().BeFalse();
    }

    [Fact]
    public async Task should_not_stop_runtime_when_owner_bootstrap_token_is_canceled_after_startup()
    {
        var processor = new TrackingProcessingServer();
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
        var startedProcessor = new TrackingProcessingServer();
        var failure = new InvalidOperationException("processor boom");
        await using var provider = _CreateProvider(
            beforeMessaging: startedProcessor,
            afterMessaging: new FailingProcessingServer(failure)
        );
        var bootstrapper = provider.GetRequiredService<IBootstrapper>();

        var act = async () => await bootstrapper.BootstrapAsync(AbortToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("processor boom");
        startedProcessor.DisposeCount.Should().BeGreaterThan(0);
        bootstrapper.IsStarted.Should().BeFalse();
    }

    [Fact]
    public async Task should_log_warning_when_use_storage_lock_is_true_and_no_real_lock_provider_registered()
    {
        var captured = new List<(LogLevel Level, EventId EventId)>();
        await using var provider = _CreateProvider(captureLog: captured, configureOptions: o => o.UseStorageLock = true);
        var bootstrapper = provider.GetRequiredService<IBootstrapper>();

        await bootstrapper.BootstrapAsync(AbortToken);

        captured.Should().Contain(
            e => e.Level == LogLevel.Warning && e.EventId.Id == 77,
            "UseStorageLockWithNoOpProvider warning must fire when only NoOpDistributedLockProvider is registered"
        );
    }

    [Fact]
    public async Task should_not_log_warning_when_use_storage_lock_is_false()
    {
        var captured = new List<(LogLevel Level, EventId EventId)>();
        await using var provider = _CreateProvider(captureLog: captured, configureOptions: o => o.UseStorageLock = false);
        var bootstrapper = provider.GetRequiredService<IBootstrapper>();

        await bootstrapper.BootstrapAsync(AbortToken);

        captured.Should().NotContain(
            e => e.Level == LogLevel.Warning && e.EventId.Id == 77,
            "warning must be silent when UseStorageLock is false, even with NoOpDistributedLockProvider"
        );
    }

    [Fact]
    public async Task should_not_log_warning_when_real_lock_provider_is_registered()
    {
        var captured = new List<(LogLevel Level, EventId EventId)>();
        var realProvider = Substitute.For<IDistributedLockProvider>();
        await using var provider = _CreateProvider(
            captureLog: captured,
            configureOptions: o => o.UseStorageLock = true,
            extraSetup: services => services.AddSingleton(realProvider)
        );
        var bootstrapper = provider.GetRequiredService<IBootstrapper>();

        await bootstrapper.BootstrapAsync(AbortToken);

        captured.Should().NotContain(
            e => e.Level == LogLevel.Warning && e.EventId.Id == 77,
            "warning must be silent when a real IDistributedLockProvider is registered"
        );
    }

    private ServiceProvider _CreateProvider(
        IProcessingServer? beforeMessaging = null,
        IProcessingServer? afterMessaging = null,
        List<(LogLevel Level, EventId EventId)>? captureLog = null,
        Action<MessagingOptions>? configureOptions = null,
        Action<IServiceCollection>? extraSetup = null
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

        services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemoryMessageQueue();
            setup.UseInMemoryStorage();
            setup.UseConventions(c =>
            {
                c.UseApplicationId("bootstrap-tests");
                c.UseVersion("v1");
            });
        });

        if (configureOptions is not null)
        {
            services.Configure<MessagingOptions>(configureOptions);
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
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

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
