// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;

namespace Tests.Abstractions;

// Directly exercises the CAS ref-count state machine behind ITenantWriteGuardBypass — its in-window
// race is not reachable through the public BeginBypass API, so it is tested at the primitive level.
public sealed class BypassStateTests
{
    [Fact]
    public void should_be_inactive_until_the_first_ref_when_fresh_state()
    {
        var state = new TenantWriteGuardBypass.BypassState();

        state.IsActive.Should().BeFalse();
        state.TryAddRef().Should().BeTrue();
        state.IsActive.Should().BeTrue();
    }

    [Fact]
    public void should_stay_active_until_the_last_release_when_nested_refs()
    {
        var state = new TenantWriteGuardBypass.BypassState();
        state.TryAddRef();
        state.TryAddRef();

        state.Release();
        state.IsActive.Should().BeTrue();

        state.Release();
        state.IsActive.Should().BeFalse();
    }

    [Fact]
    public void should_fail_on_a_terminal_state_when_try_add_ref()
    {
        var state = new TenantWriteGuardBypass.BypassState();
        state.TryAddRef();
        state.Release(); // latches terminal

        state.TryAddRef().Should().BeFalse();
        state.IsActive.Should().BeFalse();
    }

    [Fact]
    public void should_latch_terminal_without_underflow_revival_when_over_release()
    {
        var state = new TenantWriteGuardBypass.BypassState();
        state.TryAddRef();

        // Many concurrent releases: exactly one latches terminal, the rest are no-ops (current <= 0).
        Parallel.For(0, 200, _ => state.Release());

        state.IsActive.Should().BeFalse();
        state.TryAddRef().Should().BeFalse("a terminal state can never be revived");
    }

    [Fact]
    public void should_keep_a_held_ref_active_when_concurrent_add_release_pairs()
    {
        var state = new TenantWriteGuardBypass.BypassState();
        state.TryAddRef(); // held throughout

        Parallel.For(
            0,
            2000,
            _ =>
            {
                if (state.TryAddRef())
                {
                    state.Release();
                }
            }
        );

        state.IsActive.Should().BeTrue();

        state.Release();
        state.IsActive.Should().BeFalse();
    }
}
