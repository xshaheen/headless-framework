// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Tests.Fakes;

namespace Tests.ThrottlingLocks;

public sealed class ThrottlingDistributedLockProviderCancellationTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly ILogger<ThrottlingDistributedLockProvider> _logger = Substitute.For<
        ILogger<ThrottlingDistributedLockProvider>
    >();

    [Fact]
    public async Task should_respect_cancellation_token_during_acquisition()
    {
        // given
        var storage = Substitute.For<IThrottlingDistributedLockStorage>();
        var options = new ThrottlingDistributedLockOptions { MaxHitsPerPeriod = 1 };
        var sut = new ThrottlingDistributedLockProvider(storage, options, _timeProvider, _logger);
        var resource = "test-resource";

        using var cts = new CancellationTokenSource();

        // Simulate already at max hits
        storage.GetHitCountsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(1L);

        // when - start acquisition
        var acquireTask = sut.TryAcquireAsync(resource, cancellationToken: cts.Token);

        // Cancel the token
        await cts.CancelAsync();

        // then
        var result = await acquireTask;
        result.Should().BeNull();
        await storage
            .Received()
            .GetHitCountsAsync(Arg.Any<string>(), Arg.Is<CancellationToken>(t => t == cts.Token || t.CanBeCanceled));
    }

    [Fact]
    public async Task should_cancel_during_retry_delay()
    {
        // given
        var storage = Substitute.For<IThrottlingDistributedLockStorage>();
        var options = new ThrottlingDistributedLockOptions { MaxHitsPerPeriod = 1 };
        var sut = new ThrottlingDistributedLockProvider(storage, options, _timeProvider, _logger);
        var resource = "test-resource";

        using var cts = new CancellationTokenSource();

        // Simulate already at max hits
        storage.GetHitCountsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(1L);

        // when - start acquisition
        var acquireTask = sut.TryAcquireAsync(
            resource,
            acquireTimeout: TimeSpan.FromSeconds(30),
            cancellationToken: cts.Token
        );

        // Cancel and then advance time to trigger the delay continuation
        await cts.CancelAsync();
        _timeProvider.Advance(TimeSpan.FromSeconds(1));

        // then
        var result = await acquireTask;
        result.Should().BeNull();
    }
}
