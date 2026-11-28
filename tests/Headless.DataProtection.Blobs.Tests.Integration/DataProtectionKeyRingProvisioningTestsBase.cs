// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Blobs;
using Headless.DataProtection;
using Headless.Hosting;
using Headless.IO;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Tests;

/// <summary>
/// Proves the key-ring deploy contract against a real backend: <c>resources provision</c> creates the container and
/// the first key, verify fails before it and passes after, and a host started without it writes nothing.
/// </summary>
public abstract class DataProtectionKeyRingProvisioningTestsBase : TestBase
{
    // Unique per test so the shared container fixture never carries one test's key ring into another.
    private readonly string _containerName = "dpkeys" + Guid.NewGuid().ToString("N")[..12];

    /// <summary>Registers the provider under test as the default blob store, with its container manager.</summary>
    protected abstract void UseBlobs(HeadlessBlobsSetupBuilder blobs);

    [Fact]
    public async Task should_create_the_container_and_one_key_when_provisioned_and_verify_only_after()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddProvider(LoggerProvider));
        _AddKeyRing(services);
        await using var provider = services.BuildServiceProvider();
        var provisioner = await _GetProvisionerAsync(provider);

        // when
        var before = async () => await provisioner.VerifyAsync(AbortToken);

        // then
        await before.Should().ThrowAsync<ResourceProvisioningException>().WithMessage("*resources provision*");
        (await _ContainerExistsAsync(provider)).Should().BeFalse();

        // when: a release runs the deploy step twice.
        await provider.ProvisionResourcesAsync(AbortToken);
        await provider.ProvisionResourcesAsync(AbortToken);

        // then: the container exists and holds exactly one key, and verify passes.
        (await _ContainerExistsAsync(provider))
            .Should()
            .BeTrue();
        (await _ListBlobNamesAsync(provider))
            .Should()
            .ContainSingle(name => name.EndsWith(".xml", StringComparison.Ordinal));
        await provisioner.Invoking(p => p.VerifyAsync(AbortToken)).Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_start_a_default_host_without_writing_and_hold_readiness_until_provisioned()
    {
        // given: a fresh environment whose deploy step has not run yet.
        using var warmupFailed = new WarmupFailedSignal();
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.AddProvider(LoggerProvider);
        builder.Logging.AddProvider(warmupFailed);
        _AddKeyRing(builder.Services).ValidateKeyRing();
        using var host = builder.Build();

        // when
        await host.StartAsync(AbortToken).WaitAsync(TimeSpan.FromSeconds(30), AbortToken);
        await warmupFailed.Failed.Bounded();
        var failing = await _CheckReadinessAsync(host.Services);

        // then: unready, naming the deploy step, with no container and no blob created by the start.
        failing.Status.Should().Be(HealthStatus.Unhealthy);
        failing.Exception!.Message.Should().Contain("resources provision");
        (await _ContainerExistsAsync(host.Services)).Should().BeFalse();

        // when: the deploy step runs against the same environment.
        await host.Services.ProvisionResourcesAsync(AbortToken);
        await host.Services.WaitForWarmupAsync(AbortToken).Bounded();

        // then
        (await _CheckReadinessAsync(host.Services))
            .Status.Should()
            .Be(HealthStatus.Healthy);
        (await _ListBlobNamesAsync(host.Services)).Should().ContainSingle();
        await host.StopAsync(AbortToken);
    }

    private IDataProtectionBuilder _AddKeyRing(IServiceCollection services)
    {
        services.TryAddSingleton<IMimeTypeProvider, MimeTypeProvider>();
        services.AddHeadlessBlobs(UseBlobs);

        return services
            .AddDataProtection()
            .PersistKeysToBlobStorage(configure: options => options.ContainerName = _containerName);
    }

    private static async Task<IResourceProvisioner> _GetProvisionerAsync(IServiceProvider provider)
    {
        var provisioners = new List<IResourceProvisioner>();

        foreach (var source in provider.GetServices<IResourceProvisionerSource>())
        {
            provisioners.AddRange(await source.GetProvisionersAsync(AbortToken));
        }

        return provisioners
            .Should()
            .ContainSingle(p => p.Kind == BlobStorageDataProtectionOptions.KeysResourceKind)
            .Which;
    }

    private async Task<bool> _ContainerExistsAsync(IServiceProvider provider)
    {
        return await provider
            .GetRequiredService<IBlobContainerManager>()
            .ContainerExistsAsync(_containerName, AbortToken);
    }

    private async Task<List<string>> _ListBlobNamesAsync(IServiceProvider provider)
    {
        var names = new List<string>();

        await foreach (
            var blob in provider
                .GetRequiredService<IBlobStorage>()
                .GetBlobsAsync(new BlobQuery(_containerName), AbortToken)
        )
        {
            names.Add(blob.BlobKey);
        }

        return names;
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

    private sealed class WarmupFailedSignal : ILoggerProvider, ILogger
    {
        private readonly TaskCompletionSource _failed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Failed => _failed.Task;

        public ILogger CreateLogger(string categoryName) => this;

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
            if (string.Equals(eventId.Name, "WarmupFailed", StringComparison.Ordinal))
            {
                _failed.TrySetResult();
            }
        }

        public void Dispose() { }
    }
}
