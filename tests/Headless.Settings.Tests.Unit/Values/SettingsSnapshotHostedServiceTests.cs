// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings.Values;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Values;

public sealed class SettingsSnapshotHostedServiceTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();

    [Fact]
    public async Task should_load_every_snapshot_on_start()
    {
        // given
        var first = new FakeEntry(TimeSpan.FromMinutes(1));
        var second = new FakeEntry(TimeSpan.FromMinutes(1));
        using var sut = new SettingsSnapshotHostedService(
            [first, second],
            _timeProvider,
            NullLogger<SettingsSnapshotHostedService>.Instance
        );

        // when
        await sut.StartAsync(AbortToken);
        await sut.StopAsync(AbortToken);

        // then
        first.Loads.Should().Be(1);
        second.Loads.Should().Be(1);
    }

    [Fact]
    public async Task should_fail_start_when_a_load_fails()
    {
        // given
        var entry = new FakeEntry(TimeSpan.FromMinutes(1)) { LoadFailure = new InvalidOperationException("undefined") };
        using var sut = new SettingsSnapshotHostedService(
            [entry],
            _timeProvider,
            NullLogger<SettingsSnapshotHostedService>.Instance
        );

        // when
        var act = () => sut.StartAsync(AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("undefined");
    }

    [Fact]
    public async Task should_re_read_after_the_backstop_interval()
    {
        // given
        var entry = new FakeEntry(TimeSpan.FromMinutes(1));
        using var sut = new SettingsSnapshotHostedService(
            [entry],
            _timeProvider,
            NullLogger<SettingsSnapshotHostedService>.Instance
        );
        await sut.StartAsync(AbortToken);
        await _QuietAsync(); // ExecuteAsync runs on a background thread; let it reach its first wait

        // when - past the interval plus its maximum jitter
        _timeProvider.Advance(TimeSpan.FromSeconds(50));
        await _QuietAsync();
        var beforeInterval = entry.Backstops;
        _timeProvider.Advance(TimeSpan.FromSeconds(20));
        await entry.WaitForBackstopsAsync(1);

        // then
        beforeInterval.Should().Be(0);
        entry.Backstops.Should().Be(1);
        await sut.StopAsync(AbortToken);
    }

    [Fact]
    public async Task should_keep_each_snapshot_on_its_own_interval()
    {
        // given
        var fast = new FakeEntry(TimeSpan.FromMinutes(1));
        var slow = new FakeEntry(TimeSpan.FromMinutes(10));
        using var sut = new SettingsSnapshotHostedService(
            [fast, slow],
            _timeProvider,
            NullLogger<SettingsSnapshotHostedService>.Instance
        );
        await sut.StartAsync(AbortToken);
        await _QuietAsync(); // ExecuteAsync runs on a background thread; let it reach its first wait

        // when - five minutes in one-minute steps
        for (var i = 0; i < 5; i++)
        {
            _timeProvider.Advance(TimeSpan.FromSeconds(70));
            await _QuietAsync();
        }

        // then
        fast.Backstops.Should().BeGreaterThanOrEqualTo(4);
        slow.Backstops.Should().Be(0);
        await sut.StopAsync(AbortToken);
    }

    [Fact]
    public async Task should_keep_looping_after_a_failed_re_read()
    {
        // given
        var entry = new FakeEntry(TimeSpan.FromMinutes(1)) { FailFirstBackstop = true };
        using var sut = new SettingsSnapshotHostedService(
            [entry],
            _timeProvider,
            NullLogger<SettingsSnapshotHostedService>.Instance
        );
        await sut.StartAsync(AbortToken);
        await _QuietAsync(); // ExecuteAsync runs on a background thread; let it reach its first wait

        // when
        _timeProvider.Advance(TimeSpan.FromSeconds(70));
        await entry.WaitForBackstopsAsync(1);
        _timeProvider.Advance(TimeSpan.FromSeconds(70));
        await entry.WaitForBackstopsAsync(2);

        // then
        entry.Backstops.Should().Be(2);
        await sut.StopAsync(AbortToken);
    }

    [Fact]
    public async Task should_stop_the_loop_without_throwing()
    {
        // given
        var entry = new FakeEntry(TimeSpan.FromMinutes(1));
        using var sut = new SettingsSnapshotHostedService(
            [entry],
            _timeProvider,
            NullLogger<SettingsSnapshotHostedService>.Instance
        );
        await sut.StartAsync(AbortToken);
        await _QuietAsync(); // ExecuteAsync runs on a background thread; let it reach its first wait

        // when
        var act = () => sut.StopAsync(AbortToken);

        // then
        await act.Should().NotThrowAsync();
    }

    private static Task _QuietAsync() => Task.Delay(100, AbortToken);

    private sealed class FakeEntry(TimeSpan backstop) : ISettingsSnapshotEntry
    {
        private int _backstops;

        public int Loads { get; private set; }

        public int Backstops => Volatile.Read(ref _backstops);

        public Exception? LoadFailure { get; init; }

        public bool FailFirstBackstop { get; init; }

        public IReadOnlySet<string> Names { get; } = new HashSet<string>(StringComparer.Ordinal) { "App.Theme" };

        public TimeSpan Backstop { get; } = backstop;

        public Task LoadAsync(CancellationToken cancellationToken)
        {
            Loads++;

            return LoadFailure is null ? Task.CompletedTask : Task.FromException(LoadFailure);
        }

        public Task ReloadAsync(
            SettingsSnapshotReloadReason reason,
            IReadOnlyCollection<string>? announcedNames,
            CancellationToken cancellationToken
        )
        {
            reason.Should().Be(SettingsSnapshotReloadReason.Backstop);
            var count = Interlocked.Increment(ref _backstops);

            // The real snapshot logs and swallows its failures; a fault here proves the loop does not depend on that.
            return FailFirstBackstop && count == 1
                ? Task.FromException(new InvalidOperationException("store down"))
                : Task.CompletedTask;
        }

        public async Task WaitForBackstopsAsync(int count)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);

            while (Backstops < count && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10, AbortToken);
            }
        }
    }
}
