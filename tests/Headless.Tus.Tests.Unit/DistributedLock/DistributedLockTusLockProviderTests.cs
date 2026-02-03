// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Testing.Tests;
using Headless.Tus;
using Microsoft.Extensions.DependencyInjection;
using tusdotnet.Interfaces;

namespace Tests.DistributedLock;

public sealed class DistributedLockTusLockProviderTests : TestBase
{
    private readonly IDistributedLockProvider _distributedLockProvider = Substitute.For<IDistributedLockProvider>();

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
        ITusFileLock fileLock = await sut.AquireLock(fileId);
        await fileLock.Lock();

        // then
        await _distributedLockProvider
            .Received(1)
            .TryAcquireAsync(
                $"tus-file-lock-{fileId}",
                Arg.Any<TimeSpan?>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_use_injected_provider()
    {
        // given
        const string fileId = "test-file";
        var distributedLock = Substitute.For<IDistributedLock>();
        _distributedLockProvider
            .TryAcquireAsync(
                Arg.Any<string>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(distributedLock);

        var sut = new DistributedLockTusLockProvider(_distributedLockProvider);

        // when
        ITusFileLock fileLock = await sut.AquireLock(fileId);
        var lockResult = await fileLock.Lock();

        // then
        lockResult.Should().BeTrue();
        await _distributedLockProvider
            .Received(1)
            .TryAcquireAsync(
                Arg.Any<string>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<TimeSpan?>(),
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
        services.AddSingleton(Substitute.For<IDistributedLockProvider>());

        // when
        services.AddDistributedLockTusLockProvider();

        // then
        var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetService<ITusFileLockProvider>();

        provider.Should().NotBeNull();
        provider.Should().BeOfType<DistributedLockTusLockProvider>();
    }
}
