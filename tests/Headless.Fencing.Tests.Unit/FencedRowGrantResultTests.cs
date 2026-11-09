// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Fencing;
using Headless.Testing.Tests;

namespace Tests;

public sealed class FencedRowGrantResultTests : TestBase
{
    private static readonly DateTimeOffset _ExpiresAt = new(2026, 1, 1, 12, 5, 0, TimeSpan.Zero);

    private static readonly FencedRow<Guid> _Row = new(
        "work_items",
        Guid.Parse("8d7c55a1-6c3e-4d43-9b8d-0b1d1f1c2a11"),
        4
    );

    [Fact]
    public void should_carry_the_acquired_row_when_granted()
    {
        // when
        var result = FencedRowGrantResult<Guid>.Granted(_Row, _ExpiresAt, takeoverCount: 2);

        // then
        result.Status.Should().Be(LeaseGrantStatus.Granted);
        result.IsAcquired.Should().BeTrue();
        result.Row.Should().Be(_Row);
        result.ExpiresAt.Should().Be(_ExpiresAt);
        result.TakeoverCount.Should().Be(2);
        result.HolderGeneration.Should().BeNull();
        result.PreviousGeneration.Should().BeNull();
    }

    [Fact]
    public void should_carry_the_replaced_generation_when_taken_over()
    {
        // when
        var result = FencedRowGrantResult<Guid>.Takeover(_Row, _ExpiresAt, previousGeneration: 3);

        // then
        result.Status.Should().Be(LeaseGrantStatus.Takeover);
        result.IsAcquired.Should().BeTrue();
        result.PreviousGeneration.Should().Be(3);
        result.TakeoverCount.Should().Be(1);
    }

    [Fact]
    public void should_throw_when_a_takeover_does_not_advance_the_generation()
    {
        // when
        var act = () => FencedRowGrantResult<Guid>.Takeover(_Row, _ExpiresAt, previousGeneration: 4);

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_report_the_live_holder_without_a_row_when_held()
    {
        // when
        var result = FencedRowGrantResult<Guid>.Held(holderGeneration: 7, _ExpiresAt, takeoverCount: 1);

        // then
        result.Status.Should().Be(LeaseGrantStatus.Held);
        result.IsAcquired.Should().BeFalse();
        result.Row.Should().BeNull();
        result.HolderGeneration.Should().Be(7);
        result.ExpiresAt.Should().Be(_ExpiresAt);
        result.TakeoverCount.Should().Be(1);
    }

    [Fact]
    public void should_report_the_expired_holder_without_a_row_when_the_grant_waits_for_the_sweep()
    {
        // when
        var result = FencedRowGrantResult<Guid>.Expired(holderGeneration: 7, _ExpiresAt);

        // then
        result.Status.Should().Be(LeaseGrantStatus.Expired);
        result.IsAcquired.Should().BeFalse();
        result.HolderGeneration.Should().Be(7);
    }

    [Fact]
    public void should_throw_when_a_refusal_names_no_issued_generation()
    {
        // when
        var act = () => FencedRowGrantResult<Guid>.Held(holderGeneration: 0, _ExpiresAt);

        // then
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
