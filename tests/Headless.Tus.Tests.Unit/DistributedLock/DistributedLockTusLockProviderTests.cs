// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Testing.Tests;
using Headless.Tus;
using Microsoft.Extensions.DependencyInjection;
using tusdotnet.Interfaces;

namespace Tests.DistributedLock;

public sealed class DistributedLockTusLockProviderTests : TestBase
{
    private readonly IDistributedLock _distributedLockProvider = Substitute.For<IDistributedLock>();

    [Fact]
    public async Task should_create_file_lock()
    {
        // given
        var sut = new DistributedLockTusLockProvider(_distributedLockProvider);

        // when
        var result = await sut.AquireLock("test-file-id");

        // then
        result.Should().NotBeNull();
        result.Should().BeOfType<DistributedLockTusFileLock>();
    }

    [Fact]
    public async Task should_pass_file_id_to_lock()
    {
        // given
        const string fileId = "unique-file-123";
        var sut = new DistributedLockTusLockProvider(_distributedLockProvider);

        // when
        var fileLock = await sut.AquireLock(fileId);
        await fileLock.Lock();

        // then
        await _distributedLockProvider
            .Received(1)
            .TryAcquireAsync(
                $"tus-file-lock-{fileId}",
                Arg.Any<DistributedLockAcquireOptions?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_pass_custom_resource_prefix_to_created_locks()
    {
        // given
        const string fileId = "unique-file-123";
        var sut = new DistributedLockTusLockProvider(_distributedLockProvider, "tus-avatars");

        // when
        var fileLock = await sut.AquireLock(fileId);
        await fileLock.Lock();

        // then
        await _distributedLockProvider
            .Received(1)
            .TryAcquireAsync(
                $"tus-avatars-{fileId}",
                Arg.Any<DistributedLockAcquireOptions?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_use_injected_provider()
    {
        // given
        const string fileId = "test-file";
        var distributedLock = Substitute.For<IDistributedLease>();
        _distributedLockProvider
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions?>(), Arg.Any<CancellationToken>())
            .Returns(distributedLock);

        var sut = new DistributedLockTusLockProvider(_distributedLockProvider);

        // when
        var fileLock = await sut.AquireLock(fileId);
        var lockResult = await fileLock.Lock();

        // then
        lockResult.Should().BeTrue();
        await _distributedLockProvider
            .Received(1)
            .TryAcquireAsync(
                Arg.Any<string>(),
                Arg.Any<DistributedLockAcquireOptions?>(),
                Arg.Any<CancellationToken>()
            );
    }
}

public sealed class TusDistributedLockSetupTests : TestBase
{
    [Fact]
    public void should_register_lock_provider()
    {
        // given
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IDistributedLock>());

        // when
        services.AddDistributedLockTusLockProvider();

        // then
        var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetService<ITusFileLockProvider>();

        provider.Should().NotBeNull();
        provider.Should().BeOfType<DistributedLockTusLockProvider>();
    }

    [Fact]
    public async Task should_register_lock_provider_with_custom_prefix()
    {
        // given
        var distributedLock = Substitute.For<IDistributedLock>();
        var services = new ServiceCollection();
        services.AddSingleton(distributedLock);

        // when
        services.AddDistributedLockTusLockProvider("tus-avatars");

        // then - locks created by the registered provider carry the prefix
        var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<ITusFileLockProvider>();
        var fileLock = await provider.AquireLock("file-1");
        await fileLock.Lock();

        await distributedLock
            .Received(1)
            .TryAcquireAsync(
                "tus-avatars-file-1",
                Arg.Any<DistributedLockAcquireOptions?>(),
                Arg.Any<CancellationToken>()
            );
    }
}
