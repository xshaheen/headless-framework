// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings;
using Headless.Settings.Testing;
using Headless.Testing.Tests;

namespace Tests.Values;

public sealed class TestSettingsSnapshotTests : TestBase
{
    [Fact]
    public void should_report_not_loaded_when_created_without_a_value()
    {
        // given
        ISettingsSnapshot<Policy> snapshot = new TestSettingsSnapshot<Policy>();

        // when
        var loaded = snapshot.TryGetCurrent(out var value);

        // then
        loaded.Should().BeFalse();
        value.Should().BeNull();
        snapshot.Revision.Should().Be(0);
    }

    [Fact]
    public async Task should_throw_from_get_async_when_no_value_was_set()
    {
        // given
        var snapshot = new TestSettingsSnapshot<Policy>();

        // when
        var act = async () => await snapshot.GetAsync(AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Policy*Set(...)*");
    }

    [Fact]
    public async Task should_start_loaded_at_revision_one_when_created_with_a_value()
    {
        // given
        var policy = new Policy(10);

        // when
        var snapshot = new TestSettingsSnapshot<Policy>(policy);

        // then
        snapshot.TryGetCurrent(out var current).Should().BeTrue();
        current.Should().BeSameAs(policy);
        (await snapshot.GetAsync(AbortToken)).Should().BeSameAs(policy);
        snapshot.Revision.Should().Be(1);
    }

    [Fact]
    public void should_publish_the_value_before_notifying_listeners_when_set()
    {
        // given
        var snapshot = new TestSettingsSnapshot<Policy>(new Policy(10));
        var observed = new List<(Policy Value, long Revision, long SnapshotRevision, bool Published)>();
        using var registration = snapshot.OnChange(
            (value, revision) =>
                observed.Add(
                    (value, revision, snapshot.Revision, snapshot.TryGetCurrent(out var current) && current == value)
                )
        );
        var changed = new Policy(20);

        // when
        var revision = snapshot.Set(changed);

        // then
        revision.Should().Be(2);
        observed.Should().ContainSingle().Which.Should().Be((changed, 2L, 2L, true));
    }

    [Fact]
    public void should_stop_notifying_a_listener_after_its_registration_is_disposed()
    {
        // given
        var snapshot = new TestSettingsSnapshot<Policy>();
        var calls = 0;
        var registration = snapshot.OnChange((_, _) => calls++);
        snapshot.Set(new Policy(1));

        // when
        registration.Dispose();
        snapshot.Set(new Policy(2));

        // then
        calls.Should().Be(1);
        snapshot.Revision.Should().Be(2);
    }

    [Fact]
    public void should_run_every_listener_and_rethrow_their_failures_when_set()
    {
        // given
        var snapshot = new TestSettingsSnapshot<Policy>();
        var laterListenerRan = false;
        using var failing = snapshot.OnChange((_, _) => throw new InvalidOperationException("listener failed"));
        using var later = snapshot.OnChange((_, _) => laterListenerRan = true);

        // when
        var act = () => snapshot.Set(new Policy(5));

        // then
        act.Should()
            .Throw<AggregateException>()
            .Which.InnerExceptions.Should()
            .ContainSingle()
            .Which.Message.Should()
            .Be("listener failed");
        laterListenerRan.Should().BeTrue();
        snapshot.Revision.Should().Be(1);
    }

    private sealed record Policy(int PermitLimit);
}
