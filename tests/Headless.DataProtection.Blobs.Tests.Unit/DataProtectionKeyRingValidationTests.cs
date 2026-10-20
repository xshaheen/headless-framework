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
    public async Task should_persist_key_through_real_path_and_round_trip_when_auto_generate_keys_enabled()
    {
        // given: a working blob backend and a fresh deployment (no keys yet).
        var services = _CreateServices();
        var storage = _CreateWorkingStorage();
        var manager = Substitute.For<IBlobContainerManager>();
        services.AddDataProtection().PersistKeysToBlobStorage(storage, manager).ValidateKeyRing();

        await using var provider = services.BuildServiceProvider();
        var sut = _GetWarmup(provider);

        // when
        await sut.WarmUpAsync(AbortToken);

        // then: the round-trip forced key generation through the REAL persistence path — the container was
        // ensured and StoreElement reached the storage — and the write probe uploaded AND deleted its sentinel.
        await manager.Received().EnsureContainerAsync("DataProtection", Arg.Any<CancellationToken>());
        await storage
            .Received(1)
            .UploadAsync(
                Arg.Is<BlobLocation>(l => l.Path != BlobStorageDataProtectionXmlRepository.WriteProbeBlobName),
                Arg.Any<Stream>(),
                Arg.Any<string?>(),
                Arg.Any<IReadOnlyDictionary<string, string>?>(),
                Arg.Any<CancellationToken>()
            );
        await storage
            .Received(1)
            .UploadAsync(
                Arg.Is<BlobLocation>(l => l.Path == BlobStorageDataProtectionXmlRepository.WriteProbeBlobName),
                Arg.Any<Stream>(),
                Arg.Any<string?>(),
                Arg.Any<IReadOnlyDictionary<string, string>?>(),
                Arg.Any<CancellationToken>()
            );
        await storage
            .Received(1)
            .DeleteAsync(
                Arg.Is<BlobLocation>(l => l.Path == BlobStorageDataProtectionXmlRepository.WriteProbeBlobName),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_throw_actionable_error_with_cause_when_store_fails_and_mode_is_gate_readiness()
    {
        // given: a fresh-deployment-shaped failure — the key write to the backend always fails.
        var services = _CreateServices();
        services
            .AddDataProtection()
            .PersistKeysToBlobStorage(_CreateFailingStorage(), Substitute.For<IBlobContainerManager>())
            .ValidateKeyRing();

        await using var provider = services.BuildServiceProvider();
        var sut = _GetWarmup(provider);

        // when
        var act = async () => await sut.WarmUpAsync(AbortToken);

        // then: the readiness check reports this message, so it names the container, the remediation, and the
        // backend's own reason.
        sut.GatesReadiness.Should().BeTrue();
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*key-ring validation failed*")
            .WithMessage("*'DataProtection'*")
            .WithMessage("*IBlobContainerManager*")
            .WithMessage("*Simulated fresh-deployment write failure*");
    }

    [Fact]
    public async Task should_log_critical_and_complete_without_gating_when_store_fails_and_mode_is_log_only()
    {
        // given: the same failure, but the consumer opted into LogOnly.
        var services = _CreateServices();
        var logger = _RegisterCapturingLogger(services);
        services
            .AddDataProtection()
            .PersistKeysToBlobStorage(_CreateFailingStorage(), Substitute.For<IBlobContainerManager>())
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
    public async Task should_probe_read_path_without_generating_keys_when_auto_generate_keys_disabled()
    {
        // given: a designated-key-writer topology with a key already written by the designated writer.
        var stored = await _SeedKeyRingAsync();
        var services = _CreateServices();
        var storage = _CreateWorkingStorage(stored);
        services
            .AddDataProtection()
            .PersistKeysToBlobStorage(storage, Substitute.For<IBlobContainerManager>())
            .ValidateKeyRing(options => options.ProbeWritePath = false);
        services.Configure<KeyManagementOptions>(options => options.AutoGenerateKeys = false);

        await using var provider = services.BuildServiceProvider();

        // when
        await _GetWarmup(provider).WarmUpAsync(AbortToken);

        // then: the read-only GetAllKeys probe exercised the repository read path and passed (keys present),
        // and the protect path was never taken (no key generated, nothing uploaded).
        await storage.Received(1).ListAsync(Arg.Any<BlobQuery>(), Arg.Any<CancellationToken>());
        await storage.DidNotReceiveWithAnyArgs().UploadAsync(default, null!, null, null, CancellationToken.None);
    }

    [Fact]
    public async Task should_fail_when_key_ring_empty_and_auto_generate_keys_disabled()
    {
        // given: the backend is reachable but holds NO keys — this node cannot create one (AutoGenerateKeys is
        // false), so its first protected operation would fail later. That must fail validation, not pass it.
        var services = _CreateServices();
        services
            .AddDataProtection()
            .PersistKeysToBlobStorage(_CreateWorkingStorage(), Substitute.For<IBlobContainerManager>())
            .ValidateKeyRing();
        services.Configure<KeyManagementOptions>(options => options.AutoGenerateKeys = false);

        await using var provider = services.BuildServiceProvider();

        // when
        var act = async () => await _GetWarmup(provider).WarmUpAsync(AbortToken);

        // then: the message distinguishes "reachable but empty" from backend errors.
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*'DataProtection'*")
            .WithMessage("*found no keys*")
            .WithMessage("*designated key writer*");
    }

    [Fact]
    public async Task should_log_critical_when_key_ring_empty_and_mode_is_log_only()
    {
        // given
        var services = _CreateServices();
        var logger = _RegisterCapturingLogger(services);
        services
            .AddDataProtection()
            .PersistKeysToBlobStorage(_CreateWorkingStorage(), Substitute.For<IBlobContainerManager>())
            .ValidateKeyRing(options => options.Check = KeyRingCheck.LogOnly);
        services.Configure<KeyManagementOptions>(options => options.AutoGenerateKeys = false);

        await using var provider = services.BuildServiceProvider();

        // when
        var act = async () => await _GetWarmup(provider).WarmUpAsync(AbortToken);

        // then
        await act.Should().NotThrowAsync();
        _CriticalCount(logger).Should().Be(1);
    }

    [Fact]
    public async Task should_fail_when_write_access_lost_and_valid_key_already_exists()
    {
        // given: a valid key already exists, so the protect/unprotect round-trip performs NO write — without the
        // write probe, lost write access would stay hidden until the ~90-day rotation.
        var stored = await _SeedKeyRingAsync();
        var services = _CreateServices();
        var storage = _CreateReadOnlyStorage(stored);
        services
            .AddDataProtection()
            .PersistKeysToBlobStorage(storage, Substitute.For<IBlobContainerManager>())
            .ValidateKeyRing();

        await using var provider = services.BuildServiceProvider();

        // when
        var act = async () => await _GetWarmup(provider).WarmUpAsync(AbortToken);

        // then: the sentinel write is the only write attempted, and it fails with the container context.
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*key-ring validation failed*")
            .WithMessage("*'DataProtection'*");
        await storage
            .Received(1)
            .UploadAsync(
                Arg.Is<BlobLocation>(l => l.Path == BlobStorageDataProtectionXmlRepository.WriteProbeBlobName),
                Arg.Any<Stream>(),
                Arg.Any<string?>(),
                Arg.Any<IReadOnlyDictionary<string, string>?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_skip_write_probe_when_probe_write_path_disabled()
    {
        // given
        var services = _CreateServices();
        var storage = _CreateWorkingStorage();
        services
            .AddDataProtection()
            .PersistKeysToBlobStorage(storage, Substitute.For<IBlobContainerManager>())
            .ValidateKeyRing(options => options.ProbeWritePath = false);

        await using var provider = services.BuildServiceProvider();

        // when
        await _GetWarmup(provider).WarmUpAsync(AbortToken);

        // then: the round-trip key write still happened, but no sentinel was uploaded or deleted.
        await storage
            .DidNotReceive()
            .UploadAsync(
                Arg.Is<BlobLocation>(l => l.Path == BlobStorageDataProtectionXmlRepository.WriteProbeBlobName),
                Arg.Any<Stream>(),
                Arg.Any<string?>(),
                Arg.Any<IReadOnlyDictionary<string, string>?>(),
                Arg.Any<CancellationToken>()
            );
        await storage.DidNotReceiveWithAnyArgs().DeleteAsync(default, CancellationToken.None);
    }

    [Fact]
    public async Task should_propagate_cancellation_when_the_host_stops_during_validation()
    {
        // given: LogOnly would swallow a validation failure, but a stop request is not one.
        var services = _CreateServices();
        var logger = _RegisterCapturingLogger(services);
        services
            .AddDataProtection()
            .PersistKeysToBlobStorage(_CreateWorkingStorage(), Substitute.For<IBlobContainerManager>())
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
        var builder = services
            .AddDataProtection()
            .PersistKeysToBlobStorage(_CreateWorkingStorage(), Substitute.For<IBlobContainerManager>());

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
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var storage = _CreateWorkingStorage(readGate: release.Task);
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
        await host.StopAsync(AbortToken);
    }

    [Fact]
    public async Task should_keep_readiness_unhealthy_with_the_error_until_the_store_works_when_mode_is_gate_readiness()
    {
        // given: the store rejects every write until it is fixed.
        var broken = new StrongBox<bool>(true);
        var storage = _CreateWorkingStorage(writesFail: broken);
        using var host = _BuildHost(builder =>
            builder.Services.AddDataProtection().PersistKeysToBlobStorage(storage, _Manager()).ValidateKeyRing()
        );

        // when
        await host.StartAsync(AbortToken).WaitAsync(_WaitLimit, AbortToken);
        var failing = await _WaitForReadinessAsync(
            host.Services,
            e => e.Exception?.Message.Contains("last error", StringComparison.Ordinal) == true
        );

        // then: unready, naming the validation and its actionable error, while the store is broken.
        failing.Status.Should().Be(HealthStatus.Unhealthy);
        failing.Exception!.Message.Should().Contain(DataProtectionKeyRingValidationWarmup.WarmupName);
        failing.Exception.Message.Should().Contain("key-ring validation failed");
        failing.Exception.Message.Should().Contain("Simulated write failure");

        // when: the store is fixed, the retried validation succeeds.
        broken.Value = false;
        await _WaitUntilWarmAsync(host.Services);

        // then
        (await _CheckReadinessAsync(host.Services))
            .Status.Should()
            .Be(HealthStatus.Healthy);
        await host.StopAsync(AbortToken);
    }

    [Fact]
    public async Task should_stay_ready_and_log_the_failure_once_without_retry_when_mode_is_log_only()
    {
        // given: a store that never accepts a write.
        var broken = new StrongBox<bool>(true);
        var storage = _CreateWorkingStorage(writesFail: broken);
        using var logs = new RecordingLoggerProvider();
        using var host = _BuildHost(builder =>
        {
            builder.Logging.AddProvider(logs);
            builder
                .Services.AddDataProtection()
                .PersistKeysToBlobStorage(storage, _Manager())
                .ValidateKeyRing(options => options.Check = KeyRingCheck.LogOnly);
        });

        // when
        await host.StartAsync(AbortToken).WaitAsync(_WaitLimit, AbortToken);

        // then: ready at once, because the validation does not gate readiness.
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

    private static IBlobContainerManager _Manager() => Substitute.For<IBlobContainerManager>();

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

    private static async Task<HealthReportEntry> _WaitForReadinessAsync(
        IServiceProvider services,
        Func<HealthReportEntry, bool> condition
    )
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        limit.CancelAfter(_WaitLimit);

        while (true)
        {
            var entry = await _CheckReadinessAsync(services);

            if (condition(entry))
            {
                return entry;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20), limit.Token);
        }
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
    /// Runs a full validation against a fresh working backend so a REAL key (generated and persisted by the actual
    /// key manager) exists in the returned store — the designated-key-writer / existing-key scenarios build on it.
    /// </summary>
    private async Task<ConcurrentDictionary<string, byte[]>> _SeedKeyRingAsync()
    {
        var stored = new ConcurrentDictionary<string, byte[]>(StringComparer.Ordinal);
        var services = _CreateServices();
        services
            .AddDataProtection()
            .PersistKeysToBlobStorage(_CreateWorkingStorage(stored), Substitute.For<IBlobContainerManager>())
            .ValidateKeyRing();

        await using var provider = services.BuildServiceProvider();
        await _GetWarmup(provider).WarmUpAsync(AbortToken);

        return stored;
    }

    /// <summary>
    /// A stateful storage substitute: uploads are captured in memory and served back by the listing/read calls, and
    /// deletes remove them, so the real DataProtection key manager can complete a genuine store-then-load cycle
    /// against it. <paramref name="readGate"/> holds every listing until it completes; <paramref name="writesFail"/>
    /// rejects every upload while its value is <see langword="true"/>.
    /// </summary>
    private static IBlobStorage _CreateWorkingStorage(
        ConcurrentDictionary<string, byte[]>? seed = null,
        Task? readGate = null,
        StrongBox<bool>? writesFail = null
    )
    {
        var stored = seed ?? new ConcurrentDictionary<string, byte[]>(StringComparer.Ordinal);
        var storage = Substitute.For<IBlobStorage>();
        _ConfigureReads(storage, stored, readGate);

        storage
            .UploadAsync(
                Arg.Any<BlobLocation>(),
                Arg.Any<Stream>(),
                Arg.Any<string?>(),
                Arg.Any<IReadOnlyDictionary<string, string>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                // Honors the token like a real backend, so a stop request surfaces as cancellation.
                call.Arg<CancellationToken>().ThrowIfCancellationRequested();

                return writesFail?.Value == true
                    ? throw new NotSupportedException("Simulated write failure")
                    : new ValueTask(captureAsync(call.Arg<BlobLocation>(), call.Arg<Stream>()));
            });

        storage
            .DeleteAsync(Arg.Any<BlobLocation>(), Arg.Any<CancellationToken>())
            .Returns(call => ValueTask.FromResult(stored.TryRemove(call.Arg<BlobLocation>().Path, out _)));

        return storage;

        async Task captureAsync(BlobLocation location, Stream content)
        {
            await using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer);
            stored[location.Path] = buffer.ToArray();
        }
    }

    /// <summary>
    /// A storage substitute that serves the given blobs for listing/reads but rejects every write — the
    /// "lost write permission after deployment" shape.
    /// </summary>
    private static IBlobStorage _CreateReadOnlyStorage(ConcurrentDictionary<string, byte[]> stored)
    {
        var storage = Substitute.For<IBlobStorage>();
        _ConfigureReads(storage, stored, readGate: null);

        storage
            .UploadAsync(
                Arg.Any<BlobLocation>(),
                Arg.Any<Stream>(),
                Arg.Any<string?>(),
                Arg.Any<IReadOnlyDictionary<string, string>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ => throw new NotSupportedException("Simulated lost write access"));

        return storage;
    }

    private static void _ConfigureReads(
        IBlobStorage storage,
        ConcurrentDictionary<string, byte[]> stored,
        Task? readGate
    )
    {
        storage
            .ListAsync(Arg.Any<BlobQuery>(), Arg.Any<CancellationToken>())
            .Returns(_ => new ValueTask<BlobPage>(listAsync()));

        storage
            .OpenReadStreamAsync(Arg.Any<BlobLocation>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var path = call.Arg<BlobLocation>().Path;

                var result = stored.TryGetValue(path, out var bytes)
                    ? new BlobDownloadResult(new MemoryStream(bytes), path)
                    : null;

                return ValueTask.FromResult(result);
            });

        async Task<BlobPage> listAsync()
        {
            if (readGate is not null)
            {
                await readGate;
            }

            var blobs = stored
                .Select(pair => new BlobInfo
                {
                    BlobKey = pair.Key,
                    Created = DateTimeOffset.UtcNow,
                    Modified = DateTimeOffset.UtcNow,
                    Size = pair.Value.Length,
                })
                .ToList();

            return new BlobPage(blobs, null);
        }
    }

    /// <summary>A storage substitute whose listing is empty and whose upload always fails non-transiently.</summary>
    private static IBlobStorage _CreateFailingStorage()
    {
        var storage = Substitute.For<IBlobStorage>();

        storage
            .ListAsync(Arg.Any<BlobQuery>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(BlobPage.Empty));

        storage
            .UploadAsync(
                Arg.Any<BlobLocation>(),
                Arg.Any<Stream>(),
                Arg.Any<string?>(),
                Arg.Any<IReadOnlyDictionary<string, string>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ => throw new NotSupportedException("Simulated fresh-deployment write failure"));

        return storage;
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
