// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Testing.Tests;

namespace Tests.Abstractions;

public sealed class TenantStorageScopeBypassTests : TestBase
{
    private static readonly TenantStorageScopeBypass _Sut = TenantStorageScopeBypass.Instance;

    [Fact]
    public void should_activate_then_deactivate_on_dispose_when_begin_bypass()
    {
        // given
        _Sut.IsActive.Should().BeFalse();

        // when
        var scope = _Sut.BeginBypass();

        // then
        _Sut.IsActive.Should().BeTrue();

        // when
        scope.Dispose();

        // then
        _Sut.IsActive.Should().BeFalse();
    }

    [Fact]
    public void should_stay_active_until_the_outer_scope_is_disposed_when_nested_bypass()
    {
        // given
        var outer = _Sut.BeginBypass();
        var inner = _Sut.BeginBypass();

        // when
        inner.Dispose();

        // then
        _Sut.IsActive.Should().BeTrue();

        // when
        outer.Dispose();

        // then
        _Sut.IsActive.Should().BeFalse();
    }

    [Fact]
    public void should_not_reactivate_when_scope_is_disposed_twice()
    {
        // given
        var outer = _Sut.BeginBypass();
        var inner = _Sut.BeginBypass();
        inner.Dispose();
        outer.Dispose();

        // when
        inner.Dispose();

        // then
        _Sut.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task should_flow_into_awaited_work_when_bypass_is_active()
    {
        // given
        using var scope = _Sut.BeginBypass();

        // when
        var observed = await Task.Run(() => _Sut.IsActive, AbortToken);

        // then
        observed.Should().BeTrue();
    }

    [Fact]
    public async Task should_not_leak_to_the_caller_when_bypass_begins_inside_a_child_flow()
    {
        // given
        await Task.Run(
            () =>
            {
                _ = _Sut.BeginBypass();
            },
            AbortToken
        );

        // then
        _Sut.IsActive.Should().BeFalse();
    }
}
