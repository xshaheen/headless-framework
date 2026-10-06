// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Settings;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Tests.Fakes;

namespace Tests.Values;

public sealed class SettingsSnapshotTests : TestBase
{
    private const string _PublicLimit = "RateLimit.Public";
    private const string _UserLimit = "RateLimit.User";

    private readonly Dictionary<string, string?> _stored = new(StringComparer.Ordinal);
    private readonly ISettingManager _settingManager = Substitute.For<ISettingManager>();
    private readonly ISettingDefinitionManager _definitionManager = Substitute.For<ISettingDefinitionManager>();
    private readonly TimerCountingTimeProvider _timeProvider = new();
    private readonly CapturingLogger<SettingsSnapshot<Policy>> _logger = new();
    private int _reads;
    private int _readsInFlight;
    private int _maxReadsInFlight;
    private int _binds;
    private Exception? _readFailure;

    // When set, every store read waits for one release, so a test controls when each read completes.
    private SemaphoreSlim? _readGate;

    public SettingsSnapshotTests()
    {
        _stored[_PublicLimit] = "10";
        _stored[_UserLimit] = "100";

        _settingManager
            .GetAllAsync(
                Arg.Any<HashSet<string>>(),
                SettingValueProviderNames.Global,
                null,
                true,
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
                _readFailure is not null
                    ? Task.FromException<IReadOnlyList<SettingValue>>(_readFailure)
                    : _ReadStoredAsync(call.Arg<HashSet<string>>())
            );

        _definitionManager
            .FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => new SettingDefinition(call.Arg<string>()));
    }

    #region Load and revision

    [Fact]
    public async Task should_bind_the_resolved_values_and_set_revision_one_on_first_load()
    {
        // given
        using var sut = _CreateSut();

        // when
        await sut.EnsureLoadedAsync(AbortToken);

        // then
        _CurrentOf(sut).Should().Be(new Policy(10, 100));
        sut.Revision.Should().Be(1);
    }

    [Fact]
    public void should_report_no_current_value_before_the_first_load()
    {
        // given
        using var sut = _CreateSut();

        // when
        var loaded = sut.TryGetCurrent(out var value);

        // then
        loaded.Should().BeFalse();
        value.Should().BeNull();
        sut.Revision.Should().Be(0);
    }

    [Fact]
    public async Task should_load_on_demand_when_get_is_awaited_before_any_load()
    {
        // given
        using var sut = _CreateSut();

        // when
        var value = await sut.GetAsync(AbortToken);

        // then
        value.Should().Be(new Policy(10, 100));
        sut.Revision.Should().Be(1);
        sut.IsLoaded.Should().BeTrue();
    }

    [Fact]
    public async Task should_complete_get_synchronously_once_loaded()
    {
        // given
        using var sut = _CreateSut();
        await sut.EnsureLoadedAsync(AbortToken);

        // when
        var pending = sut.GetAsync(AbortToken);

        // then
        pending.IsCompletedSuccessfully.Should().BeTrue();
        (await pending).Should().Be(new Policy(10, 100));
        _reads.Should().Be(1);
    }

    [Fact]
    public async Task should_surface_a_failed_first_load_and_retry_on_the_next_get()
    {
        // given
        using var sut = _CreateSut();
        _readFailure = new InvalidOperationException("store down");

        // when
        var failed = () => sut.GetAsync(AbortToken).AsTask();
        await failed.Should().ThrowAsync<InvalidOperationException>().WithMessage("store down");
        _readFailure = null;
        var value = await sut.GetAsync(AbortToken);

        // then
        value.Should().Be(new Policy(10, 100));
        sut.Revision.Should().Be(1);
    }

    [Fact]
    public async Task should_read_and_validate_once_when_first_loads_run_concurrently()
    {
        // given - the first read is held, so the other callers queue behind it
        using var sut = _CreateSut();
        using var gate = new SemaphoreSlim(0);
        _readGate = gate;
        var loads = Enumerable.Range(0, 8).Select(_ => sut.GetAsync(AbortToken).AsTask()).ToArray();
        await TimerCountingTimeProvider.WaitUntilAsync(() => Volatile.Read(ref _reads) == 1, AbortToken);

        // when
        gate.Release();
        await Task.WhenAll(loads);

        // then
        _reads.Should().Be(1);
        sut.Revision.Should().Be(1);
        await _definitionManager.Received(1).FindAsync(_PublicLimit, Arg.Any<CancellationToken>());
        await _definitionManager.Received(1).FindAsync(_UserLimit, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_keep_revision_and_skip_bind_when_values_are_unchanged()
    {
        // given
        using var sut = _CreateSut();
        await sut.EnsureLoadedAsync(AbortToken);
        var current = _CurrentOf(sut);

        // when
        await sut.ReloadAsync(SettingsSnapshotReloadReason.Backstop, null, AbortToken);

        // then
        sut.Revision.Should().Be(1);
        _CurrentOf(sut).Should().BeSameAs(current);
        _binds.Should().Be(1);
    }

    [Fact]
    public async Task should_keep_revision_when_bound_record_holds_a_collection()
    {
        // given - a record holding a list compares by reference, so equality of T cannot decide the revision
        using var sut = new SettingsSnapshot<ListPolicy>(
            [_PublicLimit, _UserLimit],
            values => new ListPolicy([.. values.Values]),
            TimeSpan.FromMinutes(1),
            _settingManager,
            _definitionManager,
            _timeProvider,
            new CapturingLogger<SettingsSnapshot<ListPolicy>>()
        );
        await sut.EnsureLoadedAsync(AbortToken);

        // when
        await sut.ReloadAsync(SettingsSnapshotReloadReason.Backstop, null, AbortToken);
        await sut.ReloadAsync(SettingsSnapshotReloadReason.Backstop, null, AbortToken);

        // then
        sut.Revision.Should().Be(1);
    }

    [Fact]
    public async Task should_bind_increment_revision_and_notify_when_a_value_changes()
    {
        // given
        using var sut = _CreateSut();
        await sut.EnsureLoadedAsync(AbortToken);
        var notifications = new List<(Policy Value, long Revision)>();
        using var subscription = sut.OnChange((value, revision) => notifications.Add((value, revision)));
        _stored[_PublicLimit] = "20";

        // when
        await sut.ReloadAsync(SettingsSnapshotReloadReason.Backstop, null, AbortToken);

        // then
        _CurrentOf(sut).Should().Be(new Policy(20, 100));
        sut.Revision.Should().Be(2);
        _binds.Should().Be(2);
        notifications.Should().Equal((new Policy(20, 100), 2L));
    }

    [Fact]
    public async Task should_pass_every_tracked_name_to_bind_with_null_for_missing_values()
    {
        // given
        _stored.Remove(_UserLimit);
        IReadOnlyDictionary<string, string?>? seen = null;
        using var sut = _CreateSut(values =>
        {
            seen = values;
            return new Policy(0, 0);
        });

        // when
        await sut.EnsureLoadedAsync(AbortToken);

        // then
        seen.Should().NotBeNull();
        seen!.Keys.Should().BeEquivalentTo(_PublicLimit, _UserLimit);
        seen[_UserLimit].Should().BeNull();
        seen[_PublicLimit].Should().Be("10");
    }

    [Fact]
    public async Task should_fail_the_load_when_a_tracked_name_is_undefined()
    {
        // given
        _definitionManager.FindAsync(_UserLimit, Arg.Any<CancellationToken>()).Returns((SettingDefinition?)null);
        using var sut = _CreateSut();

        // when
        var act = () => sut.EnsureLoadedAsync(AbortToken);

        // then
        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage($"*{_UserLimit}*");
    }

    [Fact]
    public async Task should_fail_the_load_when_bind_throws_at_startup()
    {
        // given
        using var sut = _CreateSut(_ => throw new FormatException("bad value"));

        // when
        var act = () => sut.EnsureLoadedAsync(AbortToken);

        // then
        await act.Should().ThrowAsync<FormatException>();
    }

    [Fact]
    public async Task should_keep_the_last_good_value_and_log_names_only_when_bind_fails_later()
    {
        // given
        using var sut = _CreateSut();
        await sut.EnsureLoadedAsync(AbortToken);
        _stored[_PublicLimit] = "not-a-number";

        // when
        await sut.ReloadAsync(SettingsSnapshotReloadReason.Backstop, null, AbortToken);

        // then
        _CurrentOf(sut).Should().Be(new Policy(10, 100));
        sut.Revision.Should().Be(1);
        var (_, message) = _logger.Entries.Should().ContainSingle(x => x.Level == LogLevel.Error).Subject;
        message.Should().Contain(_PublicLimit).And.NotContain("not-a-number");
    }

    [Theory]
    [InlineData((int)SettingsSnapshotReloadReason.Message)]
    [InlineData((int)SettingsSnapshotReloadReason.Establishment)]
    [InlineData((int)SettingsSnapshotReloadReason.Backstop)]
    [InlineData((int)SettingsSnapshotReloadReason.Settle)]
    public async Task should_not_load_on_a_reload_before_the_first_load(int reason)
    {
        // given - the subscription hook runs during host startup, possibly before the first load
        using var sut = _CreateSut();

        // when
        await sut.ReloadAsync((SettingsSnapshotReloadReason)reason, [_PublicLimit], AbortToken);

        // then - nothing touched the store, so a down store cannot hold startup back
        sut.IsLoaded.Should().BeFalse();
        sut.Revision.Should().Be(0);
        _reads.Should().Be(0);
        await _definitionManager.DidNotReceive().FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        _logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task should_still_reject_an_undefined_name_after_an_establishment_reload()
    {
        // given
        _definitionManager.FindAsync(_UserLimit, Arg.Any<CancellationToken>()).Returns((SettingDefinition?)null);
        using var sut = _CreateSut();
        await sut.ReloadAsync(SettingsSnapshotReloadReason.Establishment, null, AbortToken);

        // when
        var act = () => sut.GetAsync(AbortToken).AsTask();

        // then
        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage($"*{_UserLimit}*");
        sut.IsLoaded.Should().BeFalse();
    }

    #endregion

    #region Listeners

    [Fact]
    public async Task should_run_later_listeners_when_one_listener_throws()
    {
        // given
        using var sut = _CreateSut();
        await sut.EnsureLoadedAsync(AbortToken);
        long? seenRevision = null;
        using var failing = sut.OnChange((_, _) => throw new InvalidOperationException("listener"));
        using var working = sut.OnChange((_, revision) => seenRevision = revision);
        _stored[_PublicLimit] = "20";

        // when
        await sut.ReloadAsync(SettingsSnapshotReloadReason.Backstop, null, AbortToken);

        // then
        seenRevision.Should().Be(2);
        _CurrentOf(sut).Should().Be(new Policy(20, 100));
        _logger.Entries.Should().ContainSingle(x => x.Level == LogLevel.Error);
    }

    [Fact]
    public async Task should_not_call_a_disposed_listener()
    {
        // given
        using var sut = _CreateSut();
        await sut.EnsureLoadedAsync(AbortToken);
        var calls = 0;
        var subscription = sut.OnChange((_, _) => calls++);
        subscription.Dispose();
        _stored[_PublicLimit] = "20";

        // when
        await sut.ReloadAsync(SettingsSnapshotReloadReason.Backstop, null, AbortToken);

        // then
        calls.Should().Be(0);
    }

    #endregion

    #region Settle re-reads

    [Fact]
    public async Task should_pick_up_an_announced_value_on_a_settle_re_read()
    {
        // given - the announcement arrives before this replica's cache drops the old value
        using var sut = _CreateSut();
        await sut.EnsureLoadedAsync(AbortToken);
        await sut.ReloadAsync(SettingsSnapshotReloadReason.Message, [_PublicLimit], AbortToken);
        sut.Revision.Should().Be(1);
        _stored[_PublicLimit] = "20";

        // when
        await _AdvanceSettleAsync(0);
        await TimerCountingTimeProvider.WaitUntilAsync(() => sut.Revision == 2, AbortToken);

        // then
        _CurrentOf(sut).Should().Be(new Policy(20, 100));
        _reads.Should().Be(3);
    }

    [Fact]
    public async Task should_re_read_exactly_the_bounded_number_of_times_when_nothing_changes()
    {
        // given
        using var sut = _CreateSut();
        await sut.EnsureLoadedAsync(AbortToken);

        // when
        await sut.ReloadAsync(SettingsSnapshotReloadReason.Message, [_PublicLimit], AbortToken);

        for (var i = 0; i < SettingsSnapshot<Policy>.SettleDelays.Length; i++)
        {
            await _AdvanceSettleAsync(i);
        }

        // then - one startup read, one message read, one per settle delay, and no further delay scheduled
        var expected = 2 + SettingsSnapshot<Policy>.SettleDelays.Length;
        await TimerCountingTimeProvider.WaitUntilAsync(() => Volatile.Read(ref _reads) == expected, AbortToken);
        _timeProvider.TimersCreated.Should().Be(SettingsSnapshot<Policy>.SettleDelays.Length);
        sut.Revision.Should().Be(1);
    }

    [Fact]
    public async Task should_not_settle_after_a_backstop_reload_that_changed_nothing()
    {
        // given
        using var sut = _CreateSut();
        await sut.EnsureLoadedAsync(AbortToken);

        // when
        await sut.ReloadAsync(SettingsSnapshotReloadReason.Backstop, null, AbortToken);

        // then - a settle chain registers its first delay before the reload returns
        _timeProvider.TimersCreated.Should().Be(0);
        _reads.Should().Be(2);
    }

    [Fact]
    public async Task should_not_settle_when_the_announced_value_was_read_but_bind_rejected_it()
    {
        // given
        using var sut = _CreateSut();
        await sut.EnsureLoadedAsync(AbortToken);
        _stored[_PublicLimit] = "not-a-number";

        // when
        await sut.ReloadAsync(SettingsSnapshotReloadReason.Message, [_PublicLimit], AbortToken);

        // then - the value was seen, so re-reading would only fail and log again
        _timeProvider.TimersCreated.Should().Be(0);
        _logger.Entries.Should().ContainSingle(x => x.Level == LogLevel.Error);
        sut.Revision.Should().Be(1);
    }

    [Fact]
    public async Task should_settle_after_an_establishment_reload_that_changed_nothing()
    {
        // given - the hybrid cache flushes its L1 in its own establishment hook, in no order against this one
        using var sut = _CreateSut();
        await sut.EnsureLoadedAsync(AbortToken);
        await sut.ReloadAsync(SettingsSnapshotReloadReason.Establishment, null, AbortToken);
        _stored[_UserLimit] = "200";

        // when
        await _AdvanceSettleAsync(0);
        await TimerCountingTimeProvider.WaitUntilAsync(() => sut.Revision == 2, AbortToken);

        // then
        _CurrentOf(sut).Should().Be(new Policy(10, 200));
    }

    [Fact]
    public async Task should_keep_settling_for_an_announced_name_hidden_behind_another_change()
    {
        // given - two announcements; the second reload sees only the first name's new value
        using var sut = _CreateSut();
        await sut.EnsureLoadedAsync(AbortToken);
        _stored[_PublicLimit] = "20";
        await sut.ReloadAsync(SettingsSnapshotReloadReason.Message, [_UserLimit], AbortToken);
        sut.Revision.Should().Be(2);
        _stored[_UserLimit] = "200";

        // when
        await _AdvanceSettleAsync(0);
        await TimerCountingTimeProvider.WaitUntilAsync(() => sut.Revision == 3, AbortToken);

        // then
        _CurrentOf(sut).Should().Be(new Policy(20, 200));
    }

    [Fact]
    public async Task should_re_read_at_most_once_per_settle_delay_for_a_burst_of_missed_announcements()
    {
        // given
        using var sut = _CreateSut();
        await sut.EnsureLoadedAsync(AbortToken);
        const int burst = 5;

        // when - every announcement misses its value, and the later ones arrive while the first chain waits
        for (var i = 0; i < burst; i++)
        {
            await sut.ReloadAsync(
                SettingsSnapshotReloadReason.Message,
                [i % 2 == 0 ? _PublicLimit : _UserLimit],
                AbortToken
            );
        }

        for (var i = 0; i < SettingsSnapshot<Policy>.SettleDelays.Length; i++)
        {
            await _AdvanceSettleAsync(i);
        }

        // then - one startup read, one read per announcement, and one shared read per settle delay
        var expected = 1 + burst + SettingsSnapshot<Policy>.SettleDelays.Length;
        await TimerCountingTimeProvider.WaitUntilAsync(() => Volatile.Read(ref _reads) == expected, AbortToken);
        _timeProvider.TimersCreated.Should().Be(SettingsSnapshot<Policy>.SettleDelays.Length);
        sut.Revision.Should().Be(1);
    }

    [Fact]
    public async Task should_pick_up_the_value_of_an_announcement_that_joined_a_running_settle_chain()
    {
        // given
        using var sut = _CreateSut();
        await sut.EnsureLoadedAsync(AbortToken);
        await sut.ReloadAsync(SettingsSnapshotReloadReason.Message, [_PublicLimit], AbortToken);
        await sut.ReloadAsync(SettingsSnapshotReloadReason.Message, [_UserLimit], AbortToken);
        _timeProvider.TimersCreated.Should().Be(1);
        _stored[_UserLimit] = "200";

        // when
        await _AdvanceSettleAsync(0);
        await TimerCountingTimeProvider.WaitUntilAsync(() => sut.Revision == 2, AbortToken);

        // then
        _CurrentOf(sut).Should().Be(new Policy(10, 200));
    }

    [Fact]
    public async Task should_keep_settling_for_a_joined_announcement_after_another_announcement_was_seen()
    {
        // given - two announcements share one chain; the first one's value shows up first
        using var sut = _CreateSut();
        await sut.EnsureLoadedAsync(AbortToken);
        await sut.ReloadAsync(SettingsSnapshotReloadReason.Message, [_PublicLimit], AbortToken);
        await sut.ReloadAsync(SettingsSnapshotReloadReason.Message, [_UserLimit], AbortToken);
        _stored[_PublicLimit] = "20";
        await _AdvanceSettleAsync(0);
        await TimerCountingTimeProvider.WaitUntilAsync(() => sut.Revision == 2, AbortToken);
        _stored[_UserLimit] = "200";

        // when
        await _AdvanceSettleAsync(1);
        await TimerCountingTimeProvider.WaitUntilAsync(() => sut.Revision == 3, AbortToken);

        // then
        _CurrentOf(sut).Should().Be(new Policy(20, 200));
    }

    [Fact]
    public async Task should_stop_the_shared_settle_chain_once_every_announcement_was_seen()
    {
        // given
        using var sut = _CreateSut();
        await sut.EnsureLoadedAsync(AbortToken);
        await sut.ReloadAsync(SettingsSnapshotReloadReason.Message, [_PublicLimit], AbortToken);
        await sut.ReloadAsync(SettingsSnapshotReloadReason.Message, [_UserLimit], AbortToken);
        _stored[_PublicLimit] = "20";
        _stored[_UserLimit] = "200";

        // when
        await _AdvanceSettleAsync(0);
        await TimerCountingTimeProvider.WaitUntilAsync(() => Volatile.Read(ref _reads) == 4, AbortToken);
        _timeProvider.Advance(TimeSpan.FromMinutes(1));

        // then - no further delay was scheduled and nothing re-read
        _timeProvider.TimersCreated.Should().Be(1);
        _reads.Should().Be(4);
        _CurrentOf(sut).Should().Be(new Policy(20, 200));
    }

    [Fact]
    public async Task should_start_a_new_settle_chain_after_the_previous_one_finished()
    {
        // given - a chain that ran all its re-reads
        using var sut = _CreateSut();
        await sut.EnsureLoadedAsync(AbortToken);
        await sut.ReloadAsync(SettingsSnapshotReloadReason.Message, [_PublicLimit], AbortToken);

        for (var i = 0; i < SettingsSnapshot<Policy>.SettleDelays.Length; i++)
        {
            await _AdvanceSettleAsync(i);
        }

        var settled = 2 + SettingsSnapshot<Policy>.SettleDelays.Length;
        await TimerCountingTimeProvider.WaitUntilAsync(() => Volatile.Read(ref _reads) == settled, AbortToken);

        // when
        await sut.ReloadAsync(SettingsSnapshotReloadReason.Message, [_PublicLimit], AbortToken);
        _stored[_PublicLimit] = "20";
        await _timeProvider.WaitForTimersAsync(SettingsSnapshot<Policy>.SettleDelays.Length + 1, AbortToken);
        _timeProvider.Advance(SettingsSnapshot<Policy>.SettleDelays[0]);

        // then
        await TimerCountingTimeProvider.WaitUntilAsync(() => sut.Revision == 2, AbortToken);
        _CurrentOf(sut).Should().Be(new Policy(20, 100));
    }

    #endregion

    #region Failures and disposal

    [Fact]
    public async Task should_keep_the_last_good_value_when_the_store_read_fails()
    {
        // given
        using var sut = _CreateSut();
        await sut.EnsureLoadedAsync(AbortToken);
        _readFailure = new InvalidOperationException("store down");

        // when
        var act = () => sut.ReloadAsync(SettingsSnapshotReloadReason.Backstop, null, AbortToken);

        // then
        await act.Should().NotThrowAsync();
        _CurrentOf(sut).Should().Be(new Policy(10, 100));
        sut.Revision.Should().Be(1);
        _logger.Entries.Should().ContainSingle(x => x.Level == LogLevel.Error);
    }

    [Fact]
    public async Task should_rethrow_cancellation_of_the_caller_token()
    {
        // given
        using var sut = _CreateSut();
        await sut.EnsureLoadedAsync(AbortToken);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        // when
        var act = () => sut.ReloadAsync(SettingsSnapshotReloadReason.Backstop, null, cancelled.Token);

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
        _logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task should_stop_pending_settle_re_reads_when_disposed()
    {
        // given
        var sut = _CreateSut();
        await sut.EnsureLoadedAsync(AbortToken);
        await sut.ReloadAsync(SettingsSnapshotReloadReason.Message, [_PublicLimit], AbortToken);
        _timeProvider.TimersCreated.Should().Be(1);

        // when
        sut.Dispose();
        sut.Dispose();
        _timeProvider.Advance(TimeSpan.FromMinutes(1));

        // then
        _reads.Should().Be(2);
        var act = () => sut.ReloadAsync(SettingsSnapshotReloadReason.Backstop, null, AbortToken);
        await act.Should().NotThrowAsync();
        _logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task should_finish_the_running_load_and_fail_the_queued_one_when_disposed_mid_load()
    {
        // given - one first load holds the reload lock on a read, a second waits for the lock
        var sut = _CreateSut();

        try
        {
            using var gate = new SemaphoreSlim(0);
            _readGate = gate;
            var running = sut.EnsureLoadedAsync(AbortToken);
            await TimerCountingTimeProvider.WaitUntilAsync(() => Volatile.Read(ref _reads) == 1, AbortToken);
            var queued = sut.GetAsync(AbortToken).AsTask();

            // when
            sut.Dispose();
            gate.Release();

            // then - neither throws from the lock nor hangs on it
            await running;
            sut.Revision.Should().Be(1);
            var act = () => queued;
            await act.Should().ThrowAsync<ObjectDisposedException>();
            _reads.Should().Be(1);
        }
        finally
        {
            // The test disposes mid-load on purpose; this covers a failure before that point.
            sut.Dispose();
        }
    }

    #endregion

    #region Concurrency

    [Fact]
    public async Task should_serialize_overlapping_reloads_and_never_move_revision_backward()
    {
        // given
        const int reloadCount = 20;
        using var sut = _CreateSut();
        await sut.EnsureLoadedAsync(AbortToken);
        var observed = new List<long>();
        using var subscription = sut.OnChange(
            (_, revision) =>
            {
                lock (observed)
                {
                    observed.Add(revision);
                }
            }
        );
        using var gate = new SemaphoreSlim(0);
        _readGate = gate;

        // when - every reload starts at once; each read waits until the test publishes its value
        var reloads = Enumerable
            .Range(0, reloadCount)
            .Select(i =>
                sut.ReloadAsync(
                    i % 2 == 0 ? SettingsSnapshotReloadReason.Backstop : SettingsSnapshotReloadReason.Message,
                    [_PublicLimit],
                    AbortToken
                )
            )
            .ToArray();

        for (var i = 1; i <= reloadCount; i++)
        {
            // Read i starts only after reload i - 1 released the lock, so it is the one read in flight.
            var expectedReads = 1 + i;
            await TimerCountingTimeProvider.WaitUntilAsync(
                () => Volatile.Read(ref _reads) == expectedReads,
                AbortToken
            );
            _stored[_PublicLimit] = (100 + i).ToString(CultureInfo.InvariantCulture);
            gate.Release();
        }

        await Task.WhenAll(reloads);

        // then
        _maxReadsInFlight.Should().Be(1);
        observed.Should().Equal(Enumerable.Range(2, reloadCount).Select(revision => (long)revision));
        sut.Revision.Should().Be(1 + reloadCount);
        _CurrentOf(sut).PublicPerMinute.Should().Be(100 + reloadCount);
    }

    #endregion

    private async Task<IReadOnlyList<SettingValue>> _ReadStoredAsync(HashSet<string> names)
    {
        Interlocked.Increment(ref _reads);
        var inFlight = Interlocked.Increment(ref _readsInFlight);
        int max;

        while (inFlight > (max = Volatile.Read(ref _maxReadsInFlight)))
        {
            _ = Interlocked.CompareExchange(ref _maxReadsInFlight, inFlight, max);
        }

        try
        {
            if (_readGate is { } gate)
            {
                await gate.WaitAsync(AbortToken);
            }

            return
            [
                .. _stored
                    .Where(pair => names.Contains(pair.Key) && pair.Value is not null)
                    .Select(pair => new SettingValue(pair.Key, pair.Value)),
            ];
        }
        finally
        {
            Interlocked.Decrement(ref _readsInFlight);
        }
    }

    private static TValue _CurrentOf<TValue>(ISettingsSnapshot<TValue> snapshot)
    {
        snapshot.TryGetCurrent(out var value).Should().BeTrue("the snapshot should have loaded");

        return value!;
    }

    private SettingsSnapshot<Policy> _CreateSut(Func<IReadOnlyDictionary<string, string?>, Policy>? bind = null)
    {
        return new SettingsSnapshot<Policy>(
            [_PublicLimit, _UserLimit],
            values =>
            {
                Interlocked.Increment(ref _binds);
                return (bind ?? Policy.From)(values);
            },
            TimeSpan.FromMinutes(1),
            _settingManager,
            _definitionManager,
            _timeProvider,
            _logger
        );
    }

    // A settle chain registers delay i before it waits on it; advancing time first would fire nothing.
    private async Task _AdvanceSettleAsync(int index)
    {
        await _timeProvider.WaitForTimersAsync(index + 1, AbortToken);
        _timeProvider.Advance(SettingsSnapshot<Policy>.SettleDelays[index]);
    }

    private sealed record Policy(int PublicPerMinute, int UserPerMinute)
    {
        public static Policy From(IReadOnlyDictionary<string, string?> values)
        {
            return new Policy(
                int.Parse(values[_PublicLimit] ?? "0", CultureInfo.InvariantCulture),
                int.Parse(values[_UserLimit] ?? "0", CultureInfo.InvariantCulture)
            );
        }
    }

    private sealed record ListPolicy(List<string?> Values);

    private sealed class CapturingLogger<TCategory> : ILogger<TCategory>
    {
        private readonly Lock _lock = new();
        private readonly List<(LogLevel Level, string Message)> _entries = [];

        public IReadOnlyList<(LogLevel Level, string Message)> Entries
        {
            get
            {
                lock (_lock)
                {
                    return [.. _entries];
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            lock (_lock)
            {
                _entries.Add((logLevel, formatter(state, exception)));
            }
        }
    }
}
