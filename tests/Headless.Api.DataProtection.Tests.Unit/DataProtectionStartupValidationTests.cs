// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Api;
using Headless.Blobs;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class DataProtectionStartupValidationTests : TestBase
{
    [Fact]
    public async Task should_persist_key_through_real_path_and_round_trip_when_auto_generate_keys_enabled()
    {
        // given: a working blob backend and a fresh deployment (no keys yet).
        var services = _CreateServices();
        var storage = _CreateWorkingStorage();
        var manager = Substitute.For<IBlobContainerManager>();
        services.AddDataProtection().PersistKeysToBlobStorage(storage, manager).ValidateKeyRingAtStartup();

        await using var provider = services.BuildServiceProvider();
        var sut = _GetValidationService(provider);

        // when: the startup gate runs.
        await sut.StartingAsync(AbortToken);

        // then: the round-trip forced key generation through the REAL persistence path — the container was
        // ensured and StoreElement reached the storage — and the write probe uploaded AND deleted its sentinel.
        await manager.Received().EnsureContainerAsync("DataProtection", Arg.Any<CancellationToken>());
        await storage
            .Received(1)
            .UploadAsync(
                Arg.Is<BlobLocation>(l => l.Path != BlobStorageDataProtectionXmlRepository.WriteProbeBlobName),
                Arg.Any<Stream>(),
                Arg.Any<IReadOnlyDictionary<string, string>?>(),
                Arg.Any<CancellationToken>()
            );
        await storage
            .Received(1)
            .UploadAsync(
                Arg.Is<BlobLocation>(l => l.Path == BlobStorageDataProtectionXmlRepository.WriteProbeBlobName),
                Arg.Any<Stream>(),
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
    public async Task should_throw_actionable_error_at_startup_when_store_fails_and_mode_is_throw()
    {
        // given: a fresh-deployment-shaped failure — the key write to the backend always fails.
        var services = _CreateServices();
        var storage = _CreateFailingStorage();
        services
            .AddDataProtection()
            .PersistKeysToBlobStorage(storage, Substitute.For<IBlobContainerManager>())
            .ValidateKeyRingAtStartup();

        await using var provider = services.BuildServiceProvider();
        var sut = _GetValidationService(provider);

        // when
        var act = async () => await sut.StartingAsync(AbortToken);

        // then: host start fails with the actionable message naming the container and the remediation
        // (default mode is Throw), instead of the failure hiding until the first lazy key write.
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*startup validation failed*")
            .WithMessage("*'DataProtection'*")
            .WithMessage("*IBlobContainerManager*");
    }

    [Fact]
    public async Task should_log_critical_and_continue_when_store_fails_and_mode_is_log_only()
    {
        // given: the same failure, but the consumer opted into LogOnly.
        var services = _CreateServices();
        var storage = _CreateFailingStorage();
        var logger = _RegisterCapturingLogger(services);
        services
            .AddDataProtection()
            .PersistKeysToBlobStorage(storage, Substitute.For<IBlobContainerManager>())
            .ValidateKeyRingAtStartup(options => options.Mode = StartupValidationMode.LogOnly);

        await using var provider = services.BuildServiceProvider();
        var sut = _GetValidationService(provider);

        // when
        var act = async () => await sut.StartingAsync(AbortToken);

        // then: startup continues and the failure is surfaced at Critical level.
        await act.Should().NotThrowAsync();
        _CriticalLogged(logger).Should().BeTrue("a LogOnly validation failure must be logged at Critical level");
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
            .ValidateKeyRingAtStartup(options => options.ProbeWritePath = false);
        services.Configure<KeyManagementOptions>(options => options.AutoGenerateKeys = false);

        await using var provider = services.BuildServiceProvider();
        var sut = _GetValidationService(provider);

        // when
        await sut.StartingAsync(AbortToken);

        // then: the read-only GetAllKeys probe exercised the repository read path and passed (keys present),
        // and the protect path was never taken (no key generated, nothing uploaded).
        await storage.Received(1).ListAsync(Arg.Any<BlobQuery>(), Arg.Any<CancellationToken>());
        await storage.DidNotReceiveWithAnyArgs().UploadAsync(default, null!, null, CancellationToken.None);
    }

    [Fact]
    public async Task should_fail_when_key_ring_empty_and_auto_generate_keys_disabled()
    {
        // given: the backend is reachable but holds NO keys — this node cannot create one (AutoGenerateKeys is
        // false), so its first protected operation would fail later. That must fail validation, not pass it.
        var services = _CreateServices();
        var storage = _CreateWorkingStorage();
        services
            .AddDataProtection()
            .PersistKeysToBlobStorage(storage, Substitute.For<IBlobContainerManager>())
            .ValidateKeyRingAtStartup();
        services.Configure<KeyManagementOptions>(options => options.AutoGenerateKeys = false);

        await using var provider = services.BuildServiceProvider();
        var sut = _GetValidationService(provider);

        // when
        var act = async () => await sut.StartingAsync(AbortToken);

        // then: the message distinguishes "reachable but empty" from backend errors.
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*'DataProtection'*")
            .WithMessage("*found no keys*")
            .WithMessage("*designated key writer*");
    }

    [Fact]
    public async Task should_fail_at_startup_when_write_access_lost_and_valid_key_already_exists()
    {
        // given: a valid key already exists, so the protect/unprotect round-trip performs NO write — without the
        // write probe, lost write access would stay hidden until the ~90-day rotation.
        var stored = await _SeedKeyRingAsync();
        var services = _CreateServices();
        var storage = _CreateReadOnlyStorage(stored);
        services
            .AddDataProtection()
            .PersistKeysToBlobStorage(storage, Substitute.For<IBlobContainerManager>())
            .ValidateKeyRingAtStartup();

        await using var provider = services.BuildServiceProvider();
        var sut = _GetValidationService(provider);

        // when
        var act = async () => await sut.StartingAsync(AbortToken);

        // then: the sentinel write is the only write attempted, and it fails the deploy with the container context.
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*startup validation failed*")
            .WithMessage("*'DataProtection'*");
        await storage
            .Received(1)
            .UploadAsync(
                Arg.Is<BlobLocation>(l => l.Path == BlobStorageDataProtectionXmlRepository.WriteProbeBlobName),
                Arg.Any<Stream>(),
                Arg.Any<IReadOnlyDictionary<string, string>?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_log_critical_when_write_probe_fails_and_mode_is_log_only()
    {
        // given: same lost-write-access scenario, but the consumer opted into LogOnly.
        var stored = await _SeedKeyRingAsync();
        var services = _CreateServices();
        var storage = _CreateReadOnlyStorage(stored);
        var logger = _RegisterCapturingLogger(services);
        services
            .AddDataProtection()
            .PersistKeysToBlobStorage(storage, Substitute.For<IBlobContainerManager>())
            .ValidateKeyRingAtStartup(options => options.Mode = StartupValidationMode.LogOnly);

        await using var provider = services.BuildServiceProvider();
        var sut = _GetValidationService(provider);

        // when
        var act = async () => await sut.StartingAsync(AbortToken);

        // then
        await act.Should().NotThrowAsync();
        _CriticalLogged(logger).Should().BeTrue("a LogOnly write-probe failure must be logged at Critical level");
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
            .ValidateKeyRingAtStartup(options => options.ProbeWritePath = false);

        await using var provider = services.BuildServiceProvider();
        var sut = _GetValidationService(provider);

        // when
        await sut.StartingAsync(AbortToken);

        // then: the round-trip key write still happened, but no sentinel was uploaded or deleted.
        await storage
            .DidNotReceive()
            .UploadAsync(
                Arg.Is<BlobLocation>(l => l.Path == BlobStorageDataProtectionXmlRepository.WriteProbeBlobName),
                Arg.Any<Stream>(),
                Arg.Any<IReadOnlyDictionary<string, string>?>(),
                Arg.Any<CancellationToken>()
            );
        await storage.DidNotReceiveWithAnyArgs().DeleteAsync(default, CancellationToken.None);
    }

    [Fact]
    public async Task should_gate_in_starting_async_while_start_async_is_noop()
    {
        // given: the probe must run in IHostedLifecycleService.StartingAsync, which the host executes BEFORE any
        // registered IHostedService.StartAsync — otherwise app services could start against a broken key store.
        var services = _CreateServices();
        var storage = _CreateFailingStorage();
        services
            .AddDataProtection()
            .PersistKeysToBlobStorage(storage, Substitute.For<IBlobContainerManager>())
            .ValidateKeyRingAtStartup();

        await using var provider = services.BuildServiceProvider();
        var sut = _GetValidationService(provider);

        sut.Should().BeAssignableTo<IHostedLifecycleService>();

        // when/then: StartAsync is a no-op — the storage is never touched there.
        await sut.StartAsync(AbortToken);
        storage.ReceivedCalls().Should().BeEmpty();

        // when/then: the probe gates in StartingAsync.
        var act = async () => await sut.StartingAsync(AbortToken);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void should_register_single_hosted_service_when_called_twice()
    {
        // given: registration must be idempotent — wiring the validation twice (e.g. from two composition
        // helpers) must not run the probe twice.
        var services = _CreateServices();
        var builder = services
            .AddDataProtection()
            .PersistKeysToBlobStorage(_CreateWorkingStorage(), Substitute.For<IBlobContainerManager>());

        // when
        builder.ValidateKeyRingAtStartup();
        builder.ValidateKeyRingAtStartup(options => options.Mode = StartupValidationMode.LogOnly);

        // then: one hosted service, and the second call's configure still applied.
        using var provider = services.BuildServiceProvider();
        provider
            .GetServices<IHostedService>()
            .OfType<DataProtectionStartupValidationService>()
            .Should()
            .ContainSingle();
        provider
            .GetRequiredService<IOptions<DataProtectionStartupValidationOptions>>()
            .Value.Mode.Should()
            .Be(StartupValidationMode.LogOnly);
    }

    #region Helper Methods

    private ServiceCollection _CreateServices()
    {
        var services = new ServiceCollection();
        // Route framework logs to the xUnit output; individual tests may override ILoggerFactory afterwards.
        services.AddLogging(logging => logging.AddProvider(LoggerProvider));

        return services;
    }

    private static DataProtectionStartupValidationService _GetValidationService(IServiceProvider provider)
    {
        return provider.GetServices<IHostedService>().OfType<DataProtectionStartupValidationService>().Single();
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

    private static bool _CriticalLogged(ILogger logger)
    {
        return logger
            .ReceivedCalls()
            .Any(call =>
                string.Equals(call.GetMethodInfo().Name, nameof(ILogger.Log), StringComparison.Ordinal)
                && Equals(call.GetArguments()[0], LogLevel.Critical)
            );
    }

    /// <summary>
    /// Runs a full startup validation against a fresh working backend so a REAL key (generated and persisted by the
    /// actual key manager) exists in the returned store — the designated-key-writer / existing-key scenarios build
    /// on it.
    /// </summary>
    private async Task<ConcurrentDictionary<string, byte[]>> _SeedKeyRingAsync()
    {
        var stored = new ConcurrentDictionary<string, byte[]>(StringComparer.Ordinal);
        var services = _CreateServices();
        var storage = _CreateWorkingStorage(stored);
        services
            .AddDataProtection()
            .PersistKeysToBlobStorage(storage, Substitute.For<IBlobContainerManager>())
            .ValidateKeyRingAtStartup();

        await using var provider = services.BuildServiceProvider();
        await _GetValidationService(provider).StartingAsync(AbortToken);

        return stored;
    }

    /// <summary>
    /// A stateful storage substitute: uploads are captured in memory and served back by the listing/read calls, and
    /// deletes remove them, so the real DataProtection key manager can complete a genuine store-then-load cycle
    /// against it.
    /// </summary>
    private static IBlobStorage _CreateWorkingStorage(ConcurrentDictionary<string, byte[]>? seed = null)
    {
        var stored = seed ?? new ConcurrentDictionary<string, byte[]>(StringComparer.Ordinal);
        var storage = Substitute.For<IBlobStorage>();
        _ConfigureReads(storage, stored);

        storage
            .UploadAsync(
                Arg.Any<BlobLocation>(),
                Arg.Any<Stream>(),
                Arg.Any<IReadOnlyDictionary<string, string>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call => new ValueTask(captureAsync(call.Arg<BlobLocation>(), call.Arg<Stream>())));

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
        _ConfigureReads(storage, stored);

        storage
            .UploadAsync(
                Arg.Any<BlobLocation>(),
                Arg.Any<Stream>(),
                Arg.Any<IReadOnlyDictionary<string, string>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ => throw new NotSupportedException("Simulated lost write access"));

        return storage;
    }

    private static void _ConfigureReads(IBlobStorage storage, ConcurrentDictionary<string, byte[]> stored)
    {
        storage
            .ListAsync(Arg.Any<BlobQuery>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var blobs = stored
                    .Select(pair => new BlobInfo
                    {
                        BlobKey = pair.Key,
                        Created = DateTimeOffset.UtcNow,
                        Modified = DateTimeOffset.UtcNow,
                        Size = pair.Value.Length,
                    })
                    .ToList();

                return ValueTask.FromResult(new BlobPage(blobs, null));
            });

        storage
            .OpenReadStreamAsync(Arg.Any<BlobLocation>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var path = call.Arg<BlobLocation>().Path;

#pragma warning disable CA2000 // Dispose objects before losing scope
                var result = stored.TryGetValue(path, out var bytes)
                    ? new BlobDownloadResult(new MemoryStream(bytes), path)
                    : null;
#pragma warning restore CA2000

                return ValueTask.FromResult(result);
            });
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
                Arg.Any<IReadOnlyDictionary<string, string>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ => throw new NotSupportedException("Simulated fresh-deployment write failure"));

        return storage;
    }

    #endregion
}
