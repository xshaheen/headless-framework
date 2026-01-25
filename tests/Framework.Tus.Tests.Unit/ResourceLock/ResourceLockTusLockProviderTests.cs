// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.ResourceLocks;
using Framework.Testing.Tests;
using Framework.Tus;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using tusdotnet.Interfaces;

namespace Tests.ResourceLock;

public sealed class ResourceLockTusLockProviderTests : TestBase
{
    private readonly IResourceLockProvider _resourceLockProvider = Substitute.For<IResourceLockProvider>();

    [Fact]
    public async Task should_create_file_lock()
    {
        // given
        var sut = new ResourceLockTusLockProvider(_resourceLockProvider);

        // when
        var result = await sut.AquireLock("test-file-id");

        // then
        result.Should().NotBeNull();
        result.Should().BeOfType<ResourceLockTusFileLock>();
    }

    [Fact]
    public async Task should_pass_file_id_to_lock()
    {
        // given
        const string fileId = "unique-file-123";
        var sut = new ResourceLockTusLockProvider(_resourceLockProvider);

        // when
        ITusFileLock fileLock = await sut.AquireLock(fileId);
        await fileLock.Lock();

        // then
        await _resourceLockProvider
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
        var resourceLock = Substitute.For<IResourceLock>();
        _resourceLockProvider
            .TryAcquireAsync(
                Arg.Any<string>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(resourceLock);

        var sut = new ResourceLockTusLockProvider(_resourceLockProvider);

        // when
        ITusFileLock fileLock = await sut.AquireLock(fileId);
        var lockResult = await fileLock.Lock();

        // then
        lockResult.Should().BeTrue();
        await _resourceLockProvider
            .Received(1)
            .TryAcquireAsync(
                Arg.Any<string>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            );
    }
}

public sealed class TusResourceLockSetupTests : TestBase
{
    [Fact]
    public void should_register_lock_provider()
    {
        // given
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IResourceLockProvider>());

        // when
        services.AddResourceLockTusLockProvider();

        // then
        var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetService<ITusFileLockProvider>();

        provider.Should().NotBeNull();
        provider.Should().BeOfType<ResourceLockTusLockProvider>();
    }
}
