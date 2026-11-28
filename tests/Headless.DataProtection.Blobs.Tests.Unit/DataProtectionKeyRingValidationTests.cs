// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Headless.Blobs;
using Headless.DataProtection;
using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class DataProtectionKeyRingValidationTests : TestBase
{
    private static readonly TimeSpan _WaitLimit = TimeSpan.FromSeconds(30);

    #region Probe behavior

    [Fact]
    public async Task should_read_the_key_ring_without_writing_when_the_container_and_a_key_exist()
    {
        // given: a provisioned key ring.
        var stored = await _SeedKeyRingAsync();
        var services = _CreateServices();
        var storage = InMemoryKeyStorage.Fake(stored);
        var manager = _Manager();
        services.AddDataProtection().PersistKeysToBlobStorage(storage, manager).ValidateKeyRing();

        await using var provider = services.BuildServiceProvider();

        // when
        await _GetWarmup(provider).WarmUpAsync(AbortToken);

        // then: one existence read and the key-ring read; nothing created, uploaded, or deleted.
        await manager.Received(1).ContainerExistsAsync("DataProtection", Arg.Any<CancellationToken>());
        await manager.DidNotReceiveWithAnyArgs().EnsureContainerAsync(default!, CancellationToken.None);
        await storage.Received().ListAsync(Arg.Any<BlobQuery>(), Arg.Any<CancellationToken>());
        _AssertNothingWritten(storage);
    }

    [Fact]
    public async Task should_fail_naming_resources_provision_without_reading_keys_when_the_container_is_missing()
    {
        // given
        var services = _CreateServices();
        var storage = InMemoryKeyStorage.Fake();
        services
            .AddDataProtection()
            .PersistKeysToBlobStorage(storage, _Manager(containerExists: false))
            .ValidateKeyRing();

        await using var provider = services.BuildServiceProvider();
        var sut = _GetWarmup(provider);

        // when
        var act = async () => await sut.WarmUpAsync(AbortToken);

        // then
        sut.GatesReadiness.Should().BeTrue();
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*'DataProtection' blob container does not exist*")
            .WithMessage("*resources provision -k DataProtectionKeys*");
        await storage.DidNotReceiveWithAnyArgs().ListAsync(default!, CancellationToken.None);
    }

    [Fact]
    public async Task should_fail_without_generating_a_key_when_the_key_ring_is_empty_and_auto_generate_keys_is_on()
    {
        // given: a fresh deployment whose provision step never ran. AutoGenerateKeys keeps its default (true), which
        // used to make the validation create and upload the first key itself.
        var services = _CreateServices();
        var storage = InMemoryKeyStorage.Fake();
        services.AddDataProtection().PersistKeysToBlobStorage(storage, _Manager()).ValidateKeyRing();

        await using var provider = services.BuildServiceProvider();

        // when
        var act = async () => await _GetWarmup(provider).WarmUpAsync(AbortToken);

        // then
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*'DataProtection' blob container holds no key*")
            .WithMessage("*resources provision -k DataProtectionKeys*");
        _AssertNothingWritten(storage);
    }

    [Fact]
    public async Task should_throw_actionable_error_with_cause_when_the_store_cannot_be_read_and_mode_is_gate_readiness()
    {
        // given
        var services = _CreateServices();
        services.AddDataProtection().PersistKeysToBlobStorage(_CreateUnreadableStorage(), _Manager()).ValidateKeyRing();

        await using var provider = services.BuildServiceProvider();

        // when
        var act = async () => await _GetWarmup(provider).WarmUpAsync(AbortToken);

        // then: the readiness check reports this message, so it names the container, the remediation, and the
        // backend's own reason.
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*key-ring validation failed*")
            .WithMessage("*'DataProtection'*")
            .WithMessage("*resources provision -k DataProtectionKeys*")
            .WithMessage("*Simulated read failure*");
    }

    [Fact]
    public async Task should_log_critical_and_complete_without_gating_when_the_store_cannot_be_read_and_mode_is_log_only()
    {
        // given
        var services = _CreateServices();
        var logger = _RegisterCapturingLogger(services);
        services
            .AddDataProtection()
            .PersistKeysToBlobStorage(_CreateUnreadableStorage(), _Manager())
            .ValidateKeyRing(options => options.Check = KeyRingCheck.LogOnly);

        await using var provider = services.BuildServiceProvider();
        var sut = _GetWarmup(provider);

        // when
        var act = async () => await sut.WarmUpAsync(AbortToken);

        // then: the warm-up completes (so the runner never retries it) and the failure is logged at Critical.
        sut.GatesReadiness.Should().BeFalse();
        await act.Should().NotThrowAsync();
        _CriticalCount(logger).Should().Be(1);
    }

    [Fact]
    public async Task should_log_critical_when_the_key_ring_is_empty_and_mode_is_log_only()
    {
        // given
        var services = _CreateServices();
        var logger = _RegisterCapturingLogger(services);
        services
            .AddDataProtection()
            .PersistKeysToBlobStorage(InMemoryKeyStorage.Fake(), _Manager())
            .ValidateKeyRing(options => options.Check = KeyRingCheck.LogOnly);

        await using var provider = services.BuildServiceProvider();

        // when
        var act = async () => await _GetWarmup(provider).WarmUpAsync(AbortToken);

        // then
        await act.Should().NotThrowAsync();
        _CriticalCount(logger).Should().Be(1);
    }

    [Fact]
    public async Task should_propagate_cancellation_when_the_host_stops_during_validation()
    {
        // given: LogOnly would swallow a validation failure, but a stop request is not one.
        var services = _CreateServices();
        var logger = _RegisterCapturingLogger(services);
        var manager = _Manager();
        manager
            .ContainerExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                call.Arg<CancellationToken>().ThrowIfCancellationRequested();

                return ValueTask.FromResult(true);
            });
        services
            .AddDataProtection()
            .PersistKeysToBlobStorage(InMemoryKeyStorage.Fake(), manager)
            .ValidateKeyRing(options => options.Check = KeyRingCheck.LogOnly);

        await using var provider = services.BuildServiceProvider();
        using var stopping = new CancellationTokenSource();
        await stopping.CancelAsync();

        // when
        var act = async () => await _GetWarmup(provider).WarmUpAsync(stopping.Token);

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
        _CriticalCount(logger).Should().Be(0);
    }

    [Fact]
    public void should_register_a_single_warmup_and_no_hosted_service_when_called_twice()
    {
        // given: wiring the validation twice (e.g. from two composition helpers) must not run it twice.
        var services = _CreateServices();
        var builder = services.AddDataProtection().PersistKeysToBlobStorage(InMemoryKeyStorage.Fake(), _Manager());

        // when
        builder.ValidateKeyRing();
        builder.ValidateKeyRing(options => options.Check = KeyRingCheck.LogOnly);

        // then: one warm-up, nothing that runs during the host start, and the second call's configure applied.
        services
            .Where(d => d.ServiceType == typeof(IWarmup))
            .Should()
            .ContainSingle(d => d.ImplementationType == typeof(DataProtectionKeyRingValidationWarmup));
        services
            .Where(d => d.ServiceType == typeof(IHostedService))
            .Should()
            .NotContain(d => d.ImplementationType == typeof(DataProtectionKeyRingValidationWarmup));
        using var provider = services.BuildServiceProvider();
        provider
            .GetRequiredService<IOptions<DataProtectionKeyRingValidationOptions>>()
            .Value.Check.Should()
            .Be(KeyRingCheck.LogOnly);
    }

    #endregion

    #region Host behavior

    [Fact]
    public async Task should_start_the_host_without_touching_the_key_store_and_report_unready_until_validation_finishes()
    {
        // given: every key-store read hangs until released. A validation that ran during the start would hang the
        // start with it. ASP.NET Core's own data-protection hosted service also loads the key ring during the start,
        // so it is removed here to isolate this package's contribution.
        var stored = await _SeedKeyRingAsync();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var storage = InMemoryKeyStorage.Fake(stored, readGate: release.Task);
        using var host = _BuildHost(
            builder =>
                builder.Services.AddDataProtection().PersistKeysToBlobStorage(storage, _Manager()).ValidateKeyRing(),
            removeFrameworkKeyRingLoad: true
        );

        // when
        await host.StartAsync(AbortToken).WaitAsync(_WaitLimit, AbortToken);

        // then: the start returned while the store still hangs, and readiness waits for the validation.
        var meanwhile = await _CheckReadinessAsync(host.Services);
        meanwhile.Status.Should().Be(HealthStatus.Unhealthy);
        meanwhile.Exception!.Message.Should().Contain(DataProtectionKeyRingValidationWarmup.WarmupName);

        release.SetResult();
        await _WaitUntilWarmAsync(host.Services);
        (await _CheckReadinessAsync(host.Services)).Status.Should().Be(HealthStatus.Healthy);
        _AssertNothingWritten(storage);
        await host.StopAsync(AbortToken);
    }

    [Fact]
    public async Task should_keep_readiness_unhealthy_with_the_error_until_the_store_works_when_mode_is_gate_readiness()
    {
        // given: the store rejects every read until it is fixed.
        var stored = await _SeedKeyRingAsync();
        var broken = new StrongBox<bool>(true);
        var storage = InMemoryKeyStorage.Fake(stored, readsFail: broken);
        using var logs = new RecordingLoggerProvider();
        using var host = _BuildHost(builder =>
        {
            builder.Logging.AddProvider(logs);
            builder.Services.AddDataProtection().PersistKeysToBlobStorage(storage, _Manager()).ValidateKeyRing();
        });

        // when: the runner records each failure as the readiness check's last error before it logs the failed attempt
        await host.StartAsync(AbortToken).WaitAsync(_WaitLimit, AbortToken);
        await logs.WaitForAsync("WarmupFailed").Bounded();
        var failing = await _CheckReadinessAsync(host.Services);

        // then: unready, naming the validation and its actionable error, while the store is broken.
        failing.Status.Should().Be(HealthStatus.Unhealthy);
        failing.Exception!.Message.Should().Contain(DataProtectionKeyRingValidationWarmup.WarmupName);
        // The key ring's own startup check reads the same store, so either warm-up's error may be the last one.
        failing.Exception.Message.Should().Contain("Simulated read failure");

        // when: the store is fixed, the retried validation succeeds.
        broken.Value = false;
        await _WaitUntilWarmAsync(host.Services);

        // then
        (await _CheckReadinessAsync(host.Services))
            .Status.Should()
            .Be(HealthStatus.Healthy);
        _AssertNothingWritten(storage);
        await host.StopAsync(AbortToken);
    }

    [Fact]
    public async Task should_stay_ready_and_log_the_failure_once_without_retry_when_mode_is_log_only()
    {
        // given: a store that never answers a read. The key ring's own startup check is turned off, so only the
        // LogOnly validation sees the failure.
        var storage = _CreateUnreadableStorage();
        using var logs = new RecordingLoggerProvider();
        using var host = _BuildHost(builder =>
        {
            builder.Logging.AddProvider(logs);
            builder
                .Services.AddDataProtection()
                .PersistKeysToBlobStorage(storage, _Manager())
                .ValidateKeyRing(options => options.Check = KeyRingCheck.LogOnly);
            builder.Services.ConfigureResourceProvisioning(options =>
                options.StartupChecks[BlobStorageDataProtectionOptions.KeysResourceKind] = ResourceStartupCheck.None
            );
        });

        // when
        await host.StartAsync(AbortToken).WaitAsync(_WaitLimit, AbortToken);
        await _WaitUntilWarmAsync(host.Services);

        // then: ready, because the failed validation does not gate readiness.
        (await _CheckReadinessAsync(host.Services))
            .Status.Should()
            .Be(HealthStatus.Healthy);

        // The runner logs completion once the warm-up returns; a completed warm-up is never run again.
        await logs.WaitForAsync("WarmupCompleted").WaitAsync(_WaitLimit, AbortToken);
        logs.Count("WarmupFailed").Should().Be(0);
        logs.Count("KeyRingValidationFailed").Should().Be(1);
        await host.StopAsync(AbortToken);
    }

    #endregion

    #region Helper Methods

    private ServiceCollection _CreateServices()
    {
        var services = new ServiceCollection();
        // Route framework logs to the xUnit output; individual tests may override ILoggerFactory afterwards.
        services.AddLogging(logging => logging.AddProvider(LoggerProvider));

        return services;
    }

    private IHost _BuildHost(Action<HostApplicationBuilder> configure, bool removeFrameworkKeyRingLoad = false)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.AddProvider(LoggerProvider);
        configure(builder);

        if (removeFrameworkKeyRingLoad)
        {
            var frameworkLoads = builder
                .Services.Where(d =>
                    d.ServiceType == typeof(IHostedService)
                    && string.Equals(
                        d.ImplementationType?.FullName,
                        "Microsoft.AspNetCore.DataProtection.Internal.DataProtectionHostedService",
                        StringComparison.Ordinal
                    )
                )
                .ToList();
            frameworkLoads
                .Should()
                .ContainSingle("the framework's own key-ring load must be isolated from this package's");
            builder.Services.Remove(frameworkLoads[0]);
        }

        return builder.Build();
    }

    private static IBlobContainerManager _Manager(bool containerExists = true)
    {
        var manager = Substitute.For<IBlobContainerManager>();
        manager
            .ContainerExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(containerExists));

        return manager;
    }

    private static void _AssertNothingWritten(IBlobStorage storage)
    {
        storage
            .ReceivedCalls()
            .Select(call => call.GetMethodInfo().Name)
            .Should()
            .NotContain([nameof(IBlobStorage.UploadAsync), nameof(IBlobStorage.DeleteAsync)]);
    }

    private static DataProtectionKeyRingValidationWarmup _GetWarmup(IServiceProvider provider)
    {
        return provider.GetServices<IWarmup>().OfType<DataProtectionKeyRingValidationWarmup>().Single();
    }

    private static async Task<HealthReportEntry> _CheckReadinessAsync(IServiceProvider services)
    {
        var report = await services
            .GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(
                r => string.Equals(r.Name, HeadlessHealthCheckNames.Warmup, StringComparison.Ordinal),
                AbortToken
            );

        return report.Entries[HeadlessHealthCheckNames.Warmup];
    }

    private static Task _WaitUntilWarmAsync(IServiceProvider services)
    {
        return services.WaitForWarmupAsync(AbortToken).WaitAsync(_WaitLimit, AbortToken);
    }

    /// <summary>Registers a substitute logger factory whose logger records Critical-level calls.</summary>
    private static ILogger _RegisterCapturingLogger(ServiceCollection services)
    {
        var logger = Substitute.For<ILogger>();
        logger.IsEnabled(LogLevel.Critical).Returns(true);
        var loggerFactory = Substitute.For<ILoggerFactory>();
        loggerFactory.CreateLogger(Arg.Any<string>()).Returns(logger);
        services.AddSingleton(loggerFactory);

        return logger;
    }

    private static int _CriticalCount(ILogger logger)
    {
        return logger
            .ReceivedCalls()
            .Count(call =>
                string.Equals(call.GetMethodInfo().Name, nameof(ILogger.Log), StringComparison.Ordinal)
                && Equals(call.GetArguments()[0], LogLevel.Critical)
            );
    }

    /// <summary>
    /// Runs the deploy step against a fresh working backend so a REAL key (generated and persisted by the actual key
    /// manager) exists in the returned store.
    /// </summary>
    private async Task<ConcurrentDictionary<string, byte[]>> _SeedKeyRingAsync()
    {
        var stored = new ConcurrentDictionary<string, byte[]>(StringComparer.Ordinal);
        var services = _CreateServices();
        services.AddDataProtection().PersistKeysToBlobStorage(InMemoryKeyStorage.Fake(stored), _Manager());

        await using var provider = services.BuildServiceProvider();
        await provider.ProvisionResourcesAsync(AbortToken);

        stored.Should().ContainSingle("the provision step creates exactly one key on an empty ring");

        return stored;
    }

    /// <summary>A storage substitute whose every listing fails.</summary>
    private static IBlobStorage _CreateUnreadableStorage()
    {
        return InMemoryKeyStorage.Fake(readsFail: new StrongBox<bool>(true));
    }

    /// <summary>Records each log entry's event name so a test can count events and wait for one.</summary>
    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _events = new();
        private readonly ConcurrentDictionary<string, TaskCompletionSource> _waiters = new(StringComparer.Ordinal);

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(this);

        public int Count(string eventName) => _events.Count(e => string.Equals(e, eventName, StringComparison.Ordinal));

        public Task WaitForAsync(string eventName) => _Waiter(eventName).Task;

        public void Dispose() { }

        private void _Record(string? eventName)
        {
            if (eventName is null)
            {
                return;
            }

            _events.Enqueue(eventName);
            _Waiter(eventName).TrySetResult();
        }

        private TaskCompletionSource _Waiter(string eventName) =>
            _waiters.GetOrAdd(
                eventName,
                _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
            );

        private sealed class RecordingLogger(RecordingLoggerProvider owner) : ILogger
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
            ) => owner._Record(eventId.Name);
        }
    }

    #endregion
}
