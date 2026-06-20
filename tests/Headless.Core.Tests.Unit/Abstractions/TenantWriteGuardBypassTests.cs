// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;

namespace Tests.Abstractions;

public sealed class TenantWriteGuardBypassTests
{
    [Fact]
    public void begin_bypass_should_activate_then_deactivate_on_dispose()
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
    public void nested_bypass_should_stay_active_until_the_outer_scope_is_disposed()
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
    public void double_dispose_should_be_safe_and_not_corrupt_state()
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
    public async Task concurrent_nested_begin_and_dispose_should_stay_consistent()
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
    public void begin_after_a_full_release_should_return_a_fresh_active_bypass()
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
