// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Testing.Tests;

namespace Tests;

public sealed class SafeCancellationTokenSourceTests : TestBase
{
    [Fact]
    public void cancel_should_cancel_token()
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
    public void cancel_should_be_no_op_after_dispose()
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
    public void dispose_should_be_idempotent()
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
    public void is_cancellation_requested_should_remain_observable_after_cancel_then_dispose()
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
    public void create_linked_should_cancel_when_any_linked_token_cancels()
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
