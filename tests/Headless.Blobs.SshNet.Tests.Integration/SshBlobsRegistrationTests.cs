// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Blobs;
using Headless.Blobs.SshNet;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// Verifies <c>AddHeadlessBlobs</c> default + named registration and per-instance isolation for the SSH provider.
/// Registration-shape only — no SSH container required, no live connection is made (<see cref="SftpClientPool"/>
/// defers connection until the first <c>AcquireAsync</c> call; constructing the pool in the singleton factory does
/// not connect).
/// </summary>
public sealed class SshBlobsRegistrationTests
{
    /// <summary>Dummy SSH connection string accepted by the options validator. No live host required.</summary>
    private const string _DummyConnectionString = "sftp://user:password@localhost:2222";

    [Fact]
    public async Task default_store_is_injectable_and_named_stores_resolve_via_provider()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessBlobs(blobs =>
        {
            blobs.UseSsh(options => options.ConnectionString = _DummyConnectionString);
            blobs.AddNamed(
                "primary",
                instance => instance.UseSsh(options => options.ConnectionString = _DummyConnectionString)
            );
            blobs.AddNamed(
                "backup",
                instance => instance.UseSsh(options => options.ConnectionString = _DummyConnectionString)
            );
        });

        // when
        await using var serviceProvider = services.BuildServiceProvider();

        // Singletons are lazy — resolving them constructs the instance; SftpClientPool ctor only allocates
        // channels and semaphores, it does not open any SSH connection.
        var defaultStorage = serviceProvider.GetService<IBlobStorage>();
        var provider = serviceProvider.GetRequiredService<IBlobStorageProvider>();
        var primary = provider.GetStorage("primary");
        var backup = provider.GetStorage("backup");

        // then
        defaultStorage.Should().NotBeNull();
        primary.Should().NotBeSameAs(backup);
        serviceProvider.GetRequiredKeyedService<IBlobStorage>("primary").Should().BeSameAs(primary);
        serviceProvider.GetRequiredKeyedService<IBlobStorage>("backup").Should().BeSameAs(backup);
    }

    [Fact]
    public async Task named_only_configuration_leaves_default_store_unregistered()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessBlobs(blobs =>
        {
            blobs.AddNamed(
                "store-a",
                instance => instance.UseSsh(options => options.ConnectionString = _DummyConnectionString)
            );
            blobs.AddNamed(
                "store-b",
                instance => instance.UseSsh(options => options.ConnectionString = _DummyConnectionString)
            );
        });

        // when
        await using var serviceProvider = services.BuildServiceProvider();
        var defaultStorage = serviceProvider.GetService<IBlobStorage>();
        var provider = serviceProvider.GetRequiredService<IBlobStorageProvider>();
        var storeA = provider.GetStorage("store-a");
        var storeB = provider.GetStorage("store-b");

        // then — no default unkeyed IBlobStorage; named stores are distinct instances
        defaultStorage.Should().BeNull();
        storeA.Should().NotBeSameAs(storeB);
    }

    [Fact]
    public async Task container_manager_resolves_for_default_and_named_stores()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessBlobs(blobs =>
        {
            blobs.UseSsh(options => options.ConnectionString = _DummyConnectionString);
            blobs.AddNamed(
                "primary",
                instance => instance.UseSsh(options => options.ConnectionString = _DummyConnectionString)
            );
        });

        // when
        await using var serviceProvider = services.BuildServiceProvider();

        // then — the container-management capability is a separately-resolved service (default + keyed), never a cast
        // from IBlobStorage. Resolving it constructs the manager + its pool, but no SSH connection is opened.
        serviceProvider.GetService<IBlobContainerManager>().Should().NotBeNull();
        serviceProvider.GetRequiredKeyedService<IBlobContainerManager>("primary").Should().NotBeNull();
    }

    [Fact]
    public async Task named_stores_have_separate_pools_that_are_disposed_with_container()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessBlobs(blobs =>
        {
            blobs.AddNamed(
                "alpha",
                instance => instance.UseSsh(options => options.ConnectionString = _DummyConnectionString)
            );
            blobs.AddNamed(
                "beta",
                instance => instance.UseSsh(options => options.ConnectionString = _DummyConnectionString)
            );
        });

        // when
        SftpClientPool? alphaPool;
        SftpClientPool? betaPool;

        await using (var serviceProvider = services.BuildServiceProvider())
        {
            alphaPool = serviceProvider.GetRequiredKeyedService<SftpClientPool>("alpha");
            betaPool = serviceProvider.GetRequiredKeyedService<SftpClientPool>("beta");

            // each named store must use its own pool — pools must be distinct
            alphaPool.Should().NotBeSameAs(betaPool);
        }

        // after container dispose, pools must have been disposed (Disposed field is checked via ObjectDisposedException)
        var acquireAlpha = async () => await alphaPool.AcquireAsync(CancellationToken.None);
        var acquireBeta = async () => await betaPool.AcquireAsync(CancellationToken.None);

        await acquireAlpha.Should().ThrowAsync<ObjectDisposedException>();
        await acquireBeta.Should().ThrowAsync<ObjectDisposedException>();
    }
}
