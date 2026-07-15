// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;

namespace Tests.Abstractions;

public sealed class TenantWriteGuardBypassTests
{
    [Fact]
    public void should_activate_then_deactivate_on_dispose_when_begin_bypass()
    {
        // given
        var sut = new TenantWriteGuardBypass();
        sut.IsActive.Should().BeFalse();

        // when
        var scope = sut.BeginBypass();

        // then
        sut.IsActive.Should().BeTrue();

        // when
        scope.Dispose();

        // then
        sut.IsActive.Should().BeFalse();
    }

    [Fact]
    public void should_stay_active_until_the_outer_scope_is_disposed_when_nested_bypass()
    {
        // given
        var sut = new TenantWriteGuardBypass();
        var outer = sut.BeginBypass();
        var inner = sut.BeginBypass();

        // then
        sut.IsActive.Should().BeTrue();

        // when — inner released, outer still holds a ref
        inner.Dispose();

        // then
        sut.IsActive.Should().BeTrue();

        // when
        outer.Dispose();

        // then
        sut.IsActive.Should().BeFalse();
    }

    [Fact]
    public void should_be_safe_and_not_corrupt_state_when_double_dispose()
    {
        // given
        var sut = new TenantWriteGuardBypass();
        var scope = sut.BeginBypass();

        // when
        scope.Dispose();
        scope.Dispose();

        // then — no throw / no ref-count underflow
        sut.IsActive.Should().BeFalse();

        // and a subsequent bypass still works
        using (sut.BeginBypass())
        {
            sut.IsActive.Should().BeTrue();
        }

        sut.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task should_stay_consistent_when_concurrent_nested_begin_and_dispose()
    {
        // given — a parent ref keeps the shared (AsyncLocal-flowed) state alive while children race
        var sut = new TenantWriteGuardBypass();

        using (sut.BeginBypass())
        {
            sut.IsActive.Should().BeTrue();

            // when — many parallel branches share the same flowed BypassState and churn begin/dispose
            var tasks = Enumerable
                .Range(0, 32)
                .Select(_ =>
                    Task.Run(() =>
                    {
                        for (var i = 0; i < 500; i++)
                        {
                            using var nested = sut.BeginBypass();
                            // While this nested scope (and the parent) are held, the bypass must be active —
                            // never an inactive "zombie" scope from a check/add race.
                            sut.IsActive.Should().BeTrue();
                        }
                    })
                )
                .ToArray();

            await Task.WhenAll(tasks);

            // then — the parent ref survived the churn
            sut.IsActive.Should().BeTrue();
        }

        sut.IsActive.Should().BeFalse();
    }

    [Fact]
    public void should_return_a_fresh_active_bypass_when_begin_after_a_full_release()
    {
        // given — a scope that has been fully released (state latched terminal)
        var sut = new TenantWriteGuardBypass();
        sut.BeginBypass().Dispose();
        sut.IsActive.Should().BeFalse();

        // when — a new bypass must install a fresh active state, never revive the terminal one
        using (sut.BeginBypass())
        {
            // then
            sut.IsActive.Should().BeTrue();
        }

        sut.IsActive.Should().BeFalse();
    }
}
