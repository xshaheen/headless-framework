// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Testing.Tests;

namespace Tests;

public sealed class SafeCancellationTokenSourceTests : TestBase
{
    [Fact]
    public void should_cancel_token_when_cancel()
    {
        // given
        using var source = new SafeCancellationTokenSource();

        // when
        source.Cancel();

        // then
        source.IsCancellationRequested.Should().BeTrue();
        source.Token.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public void should_be_no_op_after_dispose_when_cancel()
    {
        // given
        var source = new SafeCancellationTokenSource();
        source.Dispose();

        // when
        var act = () => source.Cancel();

        // then - the reason this type exists: cancelling a disposed source must not throw
        act.Should().NotThrow();
        source.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public void should_be_idempotent_when_dispose()
    {
        // given
        var source = new SafeCancellationTokenSource();
        source.Dispose();

        // when
        var act = () => source.Dispose();

        // then
        act.Should().NotThrow();
        source.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public void should_remain_observable_after_cancel_then_dispose_when_is_cancellation_requested()
    {
        // given
        var source = new SafeCancellationTokenSource();
        source.Cancel();

        // when
        source.Dispose();

        // then - consumers polling the flag after disposal still see the cancelled state
        source.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public void should_cancel_when_create_linked_any_linked_token_cancels()
    {
        // given
        using var external = new CancellationTokenSource();
        using var source = SafeCancellationTokenSource.CreateLinked(external.Token, CancellationToken.None);

        // when
        external.Cancel();

        // then
        source.IsCancellationRequested.Should().BeTrue();
        source.Token.IsCancellationRequested.Should().BeTrue();
    }
}
