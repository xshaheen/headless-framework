// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Testing.Tests;
using Headless.Tus;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.DistributedLock;

public sealed class DistributedLockTusCleanupGateTests : TestBase
{
    private readonly IDistributedLock _distributedLock = Substitute.For<IDistributedLock>();

    [Fact]
    public async Task should_claim_the_pass_with_a_zero_wait_lease_that_outlives_the_pass()
    {
        // given
        var lease = Substitute.For<IDistributedLease>();
        DistributedLockAcquireOptions? captured = null;
        _distributedLock
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions?>(), Arg.Any<CancellationToken>())
            .Returns(lease)
            .AndDoes(call => captured = call.ArgAt<DistributedLockAcquireOptions?>(1));
        var sut = new DistributedLockTusCleanupGate(_distributedLock, "uploads");

        // when
        var claimed = await sut.TryClaimPassAsync(TimeSpan.FromMinutes(4), AbortToken);

        // then
        claimed.Should().BeTrue();
        await _distributedLock
            .Received(1)
            .TryAcquireAsync(
                "uploads:expired-uploads-cleanup",
                Arg.Any<DistributedLockAcquireOptions?>(),
                Arg.Any<CancellationToken>()
            );
        captured.Should().NotBeNull();
        captured!.AcquireTimeout.Should().Be(TimeSpan.Zero);
        captured.TimeUntilExpires.Should().Be(TimeSpan.FromMinutes(4));
        captured.ReleaseOnDispose.Should().BeFalse();
        await lease.DidNotReceive().ReleaseAsync();
    }

    [Fact]
    public async Task should_not_claim_the_pass_when_another_node_holds_the_lease()
    {
        // given
        _distributedLock
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions?>(), Arg.Any<CancellationToken>())
            .Returns((IDistributedLease?)null);
        var sut = new DistributedLockTusCleanupGate(_distributedLock, "uploads");

        // when
        var claimed = await sut.TryClaimPassAsync(TimeSpan.FromMinutes(4), AbortToken);

        // then
        claimed.Should().BeFalse();
    }

    [Fact]
    public void should_register_the_cleanup_gate_with_the_lock_provider()
    {
        // given
        var services = new ServiceCollection();
        services.AddSingleton(_distributedLock);

        // when
        services.AddDistributedLockTusLockProvider("uploads");
        using var provider = services.BuildServiceProvider();

        // then
        provider
            .GetRequiredService<ITusExpiredUploadsCleanupGate>()
            .Should()
            .BeOfType<DistributedLockTusCleanupGate>();
    }
}
