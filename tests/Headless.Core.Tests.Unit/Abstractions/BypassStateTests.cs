// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;

namespace Tests.Abstractions;

// Directly exercises the CAS ref-count state machine behind ITenantWriteGuardBypass — its in-window
// race is not reachable through the public BeginBypass API, so it is tested at the primitive level.
public sealed class BypassStateTests
{
    [Fact]
    public void fresh_state_should_be_inactive_until_the_first_ref()
    {
        var state = new TenantWriteGuardBypass.BypassState();

        state.IsActive.Should().BeFalse();
        state.TryAddRef().Should().BeTrue();
        state.IsActive.Should().BeTrue();
    }

    [Fact]
    public void nested_refs_should_stay_active_until_the_last_release()
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
    public void try_add_ref_should_fail_on_a_terminal_state()
    {
        var state = new TenantWriteGuardBypass.BypassState();
        state.TryAddRef();
        state.Release(); // latches terminal

        state.TryAddRef().Should().BeFalse();
        state.IsActive.Should().BeFalse();
    }

    [Fact]
    public void over_release_should_latch_terminal_without_underflow_revival()
    {
        var state = new TenantWriteGuardBypass.BypassState();
        state.TryAddRef();

        // Many concurrent releases: exactly one latches terminal, the rest are no-ops (current <= 0).
        Parallel.For(0, 200, _ => state.Release());

        state.IsActive.Should().BeFalse();
        state.TryAddRef().Should().BeFalse("a terminal state can never be revived");
    }

    [Fact]
    public void concurrent_add_release_pairs_should_keep_a_held_ref_active()
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
