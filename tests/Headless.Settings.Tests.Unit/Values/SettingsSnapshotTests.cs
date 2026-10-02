// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Settings.Definitions;
using Headless.Settings.Models;
using Headless.Settings.Values;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Values;

public sealed class SettingsSnapshotTests : TestBase
{
    private const string _PublicLimit = "RateLimit.Public";
    private const string _UserLimit = "RateLimit.User";

    private readonly Dictionary<string, string?> _stored = new(StringComparer.Ordinal);
    private readonly ISettingManager _settingManager = Substitute.For<ISettingManager>();
    private readonly ISettingDefinitionManager _definitionManager = Substitute.For<ISettingDefinitionManager>();
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly CapturingLogger<SettingsSnapshot<Policy>> _logger = new();
    private int _reads;
    private int _binds;

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
            {
                Interlocked.Increment(ref _reads);
                var names = call.Arg<HashSet<string>>();

                IReadOnlyList<SettingValue> values =
                [
                    .. _stored
                        .Where(pair => names.Contains(pair.Key) && pair.Value is not null)
                        .Select(pair => new SettingValue(pair.Key, pair.Value)),
                ];

                return Task.FromResult(values);
            });

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
        await sut.LoadAsync(AbortToken);

        // then
        sut.Current.Should().Be(new Policy(10, 100));
        sut.Revision.Should().Be(1);
    }

    [Fact]
    public void should_throw_when_current_is_read_before_the_first_load()
    {
        // given
        using var sut = _CreateSut();

        // when
        var act = () => sut.Current;

        // then
        act.Should().Throw<InvalidOperationException>();
        sut.Revision.Should().Be(0);
    }

    [Fact]
    public async Task should_keep_revision_and_skip_bind_when_values_are_unchanged()
    {
        // given
        using var sut = _CreateSut();
        await sut.LoadAsync(AbortToken);
        var current = sut.Current;

        // when
        await sut.ReloadAsync(SettingsSnapshotReloadReason.Backstop, null, AbortToken);

        // then
        sut.Revision.Should().Be(1);
        sut.Current.Should().BeSameAs(current);
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
        await sut.LoadAsync(AbortToken);

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
        await sut.LoadAsync(AbortToken);
        var notifications = new List<(Policy Value, long Revision)>();
        using var subscription = sut.OnChange((value, revision) => notifications.Add((value, revision)));
        _stored[_PublicLimit] = "20";

        // when
        await sut.ReloadAsync(SettingsSnapshotReloadReason.Backstop, null, AbortToken);

        // then
        sut.Current.Should().Be(new Policy(20, 100));
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
        await sut.LoadAsync(AbortToken);

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
        var act = () => sut.LoadAsync(AbortToken);

        // then
        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage($"*{_UserLimit}*");
    }

    [Fact]
    public async Task should_fail_the_load_when_bind_throws_at_startup()
    {
        // given
        using var sut = _CreateSut(_ => throw new FormatException("bad value"));

        // when
        var act = () => sut.LoadAsync(AbortToken);

        // then
        await act.Should().ThrowAsync<FormatException>();
    }

    [Fact]
    public async Task should_keep_the_last_good_value_and_log_names_only_when_bind_fails_later()
    {
        // given
        using var sut = _CreateSut();
        await sut.LoadAsync(AbortToken);
        _stored[_PublicLimit] = "not-a-number";

        // when
        await sut.ReloadAsync(SettingsSnapshotReloadReason.Backstop, null, AbortToken);

        // then
        sut.Current.Should().Be(new Policy(10, 100));
        sut.Revision.Should().Be(1);
        var entry = _logger.Entries.Should().ContainSingle(x => x.Level == LogLevel.Error).Subject;
        entry.Message.Should().Contain(_PublicLimit).And.NotContain("not-a-number");
    }

    [Fact]
    public async Task should_bind_on_a_later_reload_when_the_snapshot_has_no_state_yet()
    {
        // given - the subscription hook can run before the startup load
        using var sut = _CreateSut();

        // when
        await sut.ReloadAsync(SettingsSnapshotReloadReason.Establishment, null, AbortToken);

        // then
        sut.Current.Should().Be(new Policy(10, 100));
        sut.Revision.Should().Be(1);
    }

    [Fact]
    public async Task should_stay_unloaded_when_bind_fails_before_the_startup_load()
    {
        // given
        _stored[_PublicLimit] = "not-a-number";
        using var sut = _CreateSut();

        // when
        await sut.ReloadAsync(SettingsSnapshotReloadReason.Establishment, null, AbortToken);

        // then
        sut.Revision.Should().Be(0);
        _logger.Entries.Should().ContainSingle(x => x.Level == LogLevel.Error);
    }

    #endregion

    #region Listeners

    [Fact]
    public async Task should_run_later_listeners_when_one_listener_throws()
    {
        // given
        using var sut = _CreateSut();
        await sut.LoadAsync(AbortToken);
        long? seenRevision = null;
        using var failing = sut.OnChange((_, _) => throw new InvalidOperationException("listener"));
        using var working = sut.OnChange((_, revision) => seenRevision = revision);
        _stored[_PublicLimit] = "20";

        // when
        await sut.ReloadAsync(SettingsSnapshotReloadReason.Backstop, null, AbortToken);

        // then
        seenRevision.Should().Be(2);
        sut.Current.Should().Be(new Policy(20, 100));
        _logger.Entries.Should().ContainSingle(x => x.Level == LogLevel.Error);
    }

    [Fact]
    public async Task should_not_call_a_disposed_listener()
    {
        // given
        using var sut = _CreateSut();
        await sut.LoadAsync(AbortToken);
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
        await sut.LoadAsync(AbortToken);
        await sut.ReloadAsync(SettingsSnapshotReloadReason.Message, [_PublicLimit], AbortToken);
        sut.Revision.Should().Be(1);
        _stored[_PublicLimit] = "20";

        // when
        await _AdvanceAndWaitForReadsAsync(SettingsSnapshot<Policy>.SettleDelays[0], expectedReads: 3);

        // then
        sut.Current.Should().Be(new Policy(20, 100));
        sut.Revision.Should().Be(2);

        // the chain stopped at the change
        _timeProvider.Advance(TimeSpan.FromMinutes(1));
        await _SettleQuietlyAsync();
        _reads.Should().Be(3);
    }

    [Fact]
    public async Task should_re_read_exactly_the_bounded_number_of_times_when_nothing_changes()
    {
        // given
        using var sut = _CreateSut();
        await sut.LoadAsync(AbortToken);

        // when
        await sut.ReloadAsync(SettingsSnapshotReloadReason.Message, [_PublicLimit], AbortToken);

        foreach (var delay in SettingsSnapshot<Policy>.SettleDelays)
        {
            _timeProvider.Advance(delay);
            await _SettleQuietlyAsync();
        }

        _timeProvider.Advance(TimeSpan.FromMinutes(1));
        await _SettleQuietlyAsync();

        // then - one startup read, one message read, one per settle delay
        _reads.Should().Be(2 + SettingsSnapshot<Policy>.SettleDelays.Length);
        sut.Revision.Should().Be(1);
    }

    [Fact]
    public async Task should_not_settle_after_a_backstop_reload_that_changed_nothing()
    {
        // given
        using var sut = _CreateSut();
        await sut.LoadAsync(AbortToken);

        // when
        await sut.ReloadAsync(SettingsSnapshotReloadReason.Backstop, null, AbortToken);
        _timeProvider.Advance(TimeSpan.FromMinutes(1));
        await _SettleQuietlyAsync();

        // then
        _reads.Should().Be(2);
    }

    [Fact]
    public async Task should_settle_after_an_establishment_reload_that_changed_nothing()
    {
        // given - the hybrid cache flushes its L1 in its own establishment hook, in no order against this one
        using var sut = _CreateSut();
        await sut.LoadAsync(AbortToken);
        await sut.ReloadAsync(SettingsSnapshotReloadReason.Establishment, null, AbortToken);
        _stored[_UserLimit] = "200";

        // when
        await _AdvanceAndWaitForReadsAsync(SettingsSnapshot<Policy>.SettleDelays[0], expectedReads: 3);

        // then
        sut.Current.Should().Be(new Policy(10, 200));
        sut.Revision.Should().Be(2);
    }

    [Fact]
    public async Task should_keep_settling_for_an_announced_name_hidden_behind_another_change()
    {
        // given - two announcements; the second reload sees only the first name's new value
        using var sut = _CreateSut();
        await sut.LoadAsync(AbortToken);
        _stored[_PublicLimit] = "20";
        await sut.ReloadAsync(SettingsSnapshotReloadReason.Message, [_UserLimit], AbortToken);
        sut.Revision.Should().Be(2);
        _stored[_UserLimit] = "200";

        // when
        await _AdvanceAndWaitForReadsAsync(SettingsSnapshot<Policy>.SettleDelays[0], expectedReads: 3);

        // then
        sut.Current.Should().Be(new Policy(20, 200));
        sut.Revision.Should().Be(3);
    }

    [Fact]
    public async Task should_never_move_revision_backward_under_concurrent_reloads()
    {
        // given
        using var sut = _CreateSut();
        await sut.LoadAsync(AbortToken);
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

        // when
        var reloads = Enumerable
            .Range(0, 20)
            .Select(i =>
            {
                _stored[_PublicLimit] = i.ToString(CultureInfo.InvariantCulture);
                return sut.ReloadAsync(
                    i % 2 == 0 ? SettingsSnapshotReloadReason.Backstop : SettingsSnapshotReloadReason.Message,
                    [_PublicLimit],
                    AbortToken
                );
            })
            .ToArray();
        await Task.WhenAll(reloads);

        // then
        observed.Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
        sut.Revision.Should().Be(observed.Count == 0 ? 1 : observed[^1]);
    }

    #endregion

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

    private async Task _AdvanceAndWaitForReadsAsync(TimeSpan delay, int expectedReads)
    {
        _timeProvider.Advance(delay);
        var deadline = DateTime.UtcNow.AddSeconds(5);

        while (Volatile.Read(ref _reads) < expectedReads && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10, AbortToken);
        }

        await _SettleQuietlyAsync();
    }

    // Settle re-reads run on timer callbacks; give their continuations a moment to finish.
    private static Task _SettleQuietlyAsync() => Task.Delay(100, AbortToken);

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
