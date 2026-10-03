// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Core;

public sealed class TimeProviderExtensionsTests : TestBase
{
    [Fact]
    public async Task delay_completes_only_after_the_provider_clock_advances()
    {
        // given
        var clock = new FakeTimeProvider();

        // when
        var delay = clock.Delay(TimeSpan.FromSeconds(5), AbortToken);
        var completedBeforeAdvance = delay.IsCompleted;
        clock.Advance(TimeSpan.FromSeconds(5));
        await delay;

        // then
        completedBeforeAdvance.Should().BeFalse();
        delay.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task delay_is_canceled_by_the_token()
    {
        // given
        var clock = new FakeTimeProvider();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);

        // when
        var delay = clock.Delay(TimeSpan.FromMinutes(1), cts.Token);
        await cts.CancelAsync();
        var act = async () => await delay;

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void create_cancellation_token_source_cancels_when_the_provider_clock_reaches_the_delay()
    {
        // given
        var clock = new FakeTimeProvider();

        // when
        using var cts = clock.CreateCancellationTokenSource(TimeSpan.FromSeconds(10));
        clock.Advance(TimeSpan.FromSeconds(9));
        var canceledEarly = cts.IsCancellationRequested;
        clock.Advance(TimeSpan.FromSeconds(1));

        // then
        canceledEarly.Should().BeFalse();
        cts.IsCancellationRequested.Should().BeTrue();
    }
}
