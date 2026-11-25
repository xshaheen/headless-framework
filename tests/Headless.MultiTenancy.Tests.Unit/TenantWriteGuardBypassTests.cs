// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.MultiTenancy;
using Headless.Testing.Tests;

namespace Tests;

public sealed class TenantWriteGuardBypassTests : TestBase
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

        // when
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

        // then
        sut.IsActive.Should().BeFalse();

        // and a subsequent bypass still works
        using (sut.BeginBypass())
        {
            sut.IsActive.Should().BeTrue();
        }

        sut.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task should_end_parent_bypass_while_child_bypass_is_still_active()
    {
        var sut = new TenantWriteGuardBypass();
        var parentEnded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool> child;

        using (sut.BeginBypass())
        {
            child = observeChildAsync();
        }

        var parentWasActive = sut.IsActive;
        parentEnded.SetResult();

        (await child).Should().BeTrue();
        parentWasActive.Should().BeFalse();

        async Task<bool> observeChildAsync()
        {
            using var nested = sut.BeginBypass();
            await parentEnded.Task.WaitAsync(AbortToken);

            return sut.IsActive;
        }
    }

    [Fact]
    public void should_return_a_fresh_active_bypass_when_begin_after_a_full_release()
    {
        // given
        var sut = new TenantWriteGuardBypass();
        sut.BeginBypass().Dispose();
        sut.IsActive.Should().BeFalse();

        // when
        using (sut.BeginBypass())
        {
            // then
            sut.IsActive.Should().BeTrue();
        }

        sut.IsActive.Should().BeFalse();
    }
}
