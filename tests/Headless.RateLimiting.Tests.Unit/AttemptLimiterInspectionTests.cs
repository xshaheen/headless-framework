// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.RateLimiting;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class AttemptLimiterInspectionTests : TestBase
{
    private const string _Purpose = "otp-verify";
    private const string _Subject = "someone@example.com";

    private static readonly string _SubjectKey = Convert.ToBase64String(new byte[32]);
    private static readonly AttemptQuota _ThreePerMinute = new(3, TimeSpan.FromMinutes(1));

    // 10s into a minute-aligned window, so the window runs 10:00:00 to 10:01:00 and closes in 50s.
    private static readonly DateTimeOffset _Start = new(2026, 9, 25, 10, 0, 10, TimeSpan.Zero);

    private readonly FakeTimeProvider _timeProvider = new(_Start);
    private readonly InMemoryCache _cache;

    public AttemptLimiterInspectionTests()
    {
        _cache = new InMemoryCache(_timeProvider, new InMemoryCacheOptions());
    }

    [Fact]
    public async Task should_read_an_empty_window_as_zero_without_creating_a_counter()
    {
        // given
        var limiter = _CreateServices().GetRequiredService<IAttemptLimiter>();

        // when
        var snapshot = await limiter.PeekAsync(_Purpose, _Subject, _ThreePerMinute, AbortToken);

        // then
        snapshot.Count.Should().Be(0);
        snapshot.Remaining.Should().Be(3);
        snapshot.IsLockedOut.Should().BeFalse();
        (await _cache.GetCountAsync(cancellationToken: AbortToken)).Should().Be(0);
    }

    [Fact]
    public async Task should_report_the_count_and_aligned_window_of_a_partly_spent_budget()
    {
        // given
        var limiter = _CreateServices().GetRequiredService<IAttemptLimiter>();
        await limiter.AcquireAsync(_Purpose, _Subject, _ThreePerMinute, AbortToken);
        await limiter.AcquireAsync(_Purpose, _Subject, _ThreePerMinute, AbortToken);

        // when
        var snapshot = await limiter.PeekAsync(_Purpose, _Subject, _ThreePerMinute, AbortToken);

        // then
        snapshot.Purpose.Should().Be(_Purpose);
        snapshot.Count.Should().Be(2);
        snapshot.Limit.Should().Be(3);
        snapshot.Remaining.Should().Be(1);
        snapshot.IsLockedOut.Should().BeFalse();
        snapshot.WindowStartsAt.Should().Be(new DateTimeOffset(2026, 9, 25, 10, 0, 0, TimeSpan.Zero));
        snapshot.WindowEndsAt.Should().Be(new DateTimeOffset(2026, 9, 25, 10, 1, 0, TimeSpan.Zero));
        snapshot.RetryAfter.Should().Be(TimeSpan.FromSeconds(50));
    }

    [Fact]
    public async Task should_report_a_locked_out_budget_once_the_limit_is_spent_including_refused_attempts()
    {
        // given
        var limiter = _CreateServices().GetRequiredService<IAttemptLimiter>();

        for (var i = 0; i < 4; i++)
        {
            await limiter.AcquireAsync(_Purpose, _Subject, _ThreePerMinute, AbortToken);
        }

        // when
        var snapshot = await limiter.PeekAsync(_Purpose, _Subject, _ThreePerMinute, AbortToken);

        // then
        snapshot.IsLockedOut.Should().BeTrue();
        snapshot.Count.Should().Be(4);
        snapshot.Remaining.Should().Be(0);
    }

    [Fact]
    public async Task should_not_charge_an_attempt_when_peeking()
    {
        // given
        var limiter = _CreateServices().GetRequiredService<IAttemptLimiter>();
        await limiter.AcquireAsync(_Purpose, _Subject, _ThreePerMinute, AbortToken);

        // when
        await limiter.PeekAsync(_Purpose, _Subject, _ThreePerMinute, AbortToken);
        await limiter.PeekAsync(_Purpose, _Subject, _ThreePerMinute, AbortToken);
        var next = await limiter.AcquireAsync(_Purpose, _Subject, _ThreePerMinute, AbortToken);

        // then
        next.Count.Should().Be(2);
    }

    [Fact]
    public async Task should_never_increment_or_write_the_store_when_peeking()
    {
        // given
        var store = Substitute.For<ICache>();
        store.GetAsync<long>(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(CacheValue<long>.NoValue);
        var limiter = _CreateServices(store).GetRequiredService<IAttemptLimiter>();

        // when
        await limiter.PeekAsync(_Purpose, _Subject, _ThreePerMinute, AbortToken);

        // then
        await store.Received(1).GetAsync<long>(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await store
            .DidNotReceive()
            .IncrementAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
        await store.DidNotReceive().RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_read_a_fresh_window_after_rollover()
    {
        // given
        var limiter = _CreateServices().GetRequiredService<IAttemptLimiter>();

        for (var i = 0; i < 3; i++)
        {
            await limiter.AcquireAsync(_Purpose, _Subject, _ThreePerMinute, AbortToken);
        }

        // when
        _timeProvider.Advance(TimeSpan.FromSeconds(50));
        var snapshot = await limiter.PeekAsync(_Purpose, _Subject, _ThreePerMinute, AbortToken);

        // then
        snapshot.Count.Should().Be(0);
        snapshot.IsLockedOut.Should().BeFalse();
        snapshot.WindowStartsAt.Should().Be(new DateTimeOffset(2026, 9, 25, 10, 1, 0, TimeSpan.Zero));
        snapshot.RetryAfter.Should().Be(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task should_lift_a_lockout_when_an_operator_resets_the_current_window()
    {
        // given
        var limiter = _CreateServices().GetRequiredService<IAttemptLimiter>();

        for (var i = 0; i < 4; i++)
        {
            await limiter.AcquireAsync(_Purpose, _Subject, _ThreePerMinute, AbortToken);
        }

        // when
        var removed = await limiter.ResetAsync(_Purpose, _Subject, _ThreePerMinute, AbortToken);
        var next = await limiter.AcquireAsync(_Purpose, _Subject, _ThreePerMinute, AbortToken);

        // then
        removed.Should().BeTrue();
        next.IsAllowed.Should().BeTrue();
        next.Count.Should().Be(1);
    }

    [Fact]
    public async Task should_report_nothing_removed_when_resetting_a_window_with_no_attempts()
    {
        // given
        var limiter = _CreateServices().GetRequiredService<IAttemptLimiter>();

        // when
        var removed = await limiter.ResetAsync(_Purpose, _Subject, _ThreePerMinute, AbortToken);

        // then
        removed.Should().BeFalse();
    }

    [Fact]
    public async Task should_reset_only_the_named_purpose_and_subject()
    {
        // given
        var limiter = _CreateServices().GetRequiredService<IAttemptLimiter>();
        await limiter.AcquireAsync(_Purpose, _Subject, _ThreePerMinute, AbortToken);
        await limiter.AcquireAsync("password-reset", _Subject, _ThreePerMinute, AbortToken);
        await limiter.AcquireAsync(_Purpose, "other@example.com", _ThreePerMinute, AbortToken);

        // when
        await limiter.ResetAsync(_Purpose, _Subject, _ThreePerMinute, AbortToken);

        // then
        (await limiter.PeekAsync(_Purpose, _Subject, _ThreePerMinute, AbortToken))
            .Count.Should()
            .Be(0);
        (await limiter.PeekAsync("password-reset", _Subject, _ThreePerMinute, AbortToken)).Count.Should().Be(1);
        (await limiter.PeekAsync(_Purpose, "other@example.com", _ThreePerMinute, AbortToken)).Count.Should().Be(1);
    }

    [Fact]
    public async Task should_derive_the_same_fingerprint_the_limiter_keys_its_counter_on()
    {
        // given
        var services = _CreateServices();
        var limiter = services.GetRequiredService<IAttemptLimiter>();
        var fingerprinter = services.GetRequiredService<AttemptSubjectFingerprinter>();
        var attempt = await limiter.AcquireAsync(_Purpose, _Subject, _ThreePerMinute, AbortToken);

        // when
        var fingerprint = fingerprinter.Compute(_Purpose, _Subject);
        var snapshot = await limiter.PeekAsync(_Purpose, _Subject, _ThreePerMinute, AbortToken);

        // then
        attempt.ResetToken.Should().EndWith($":{fingerprint}");
        snapshot.Fingerprint.Should().Be(fingerprint);
    }

    [Fact]
    public void should_compute_a_deterministic_pseudonymous_fingerprint()
    {
        // given
        var fingerprinter = _CreateServices().GetRequiredService<AttemptSubjectFingerprinter>();
        var rotated = _CreateServices(subjectKey: Convert.ToBase64String(Enumerable.Repeat((byte)7, 32).ToArray()))
            .GetRequiredService<AttemptSubjectFingerprinter>();

        // when
        var first = fingerprinter.Compute(_Purpose, _Subject);
        var second = fingerprinter.Compute(_Purpose, _Subject);

        // then
        first.Should().Be(second).And.HaveLength(43);
        first.Should().NotContain("someone").And.NotContain("example");
        fingerprinter.Compute("password-reset", _Subject).Should().NotBe(first);
        fingerprinter.Compute(_Purpose, "Someone@example.com").Should().NotBe(first);
        rotated.Compute(_Purpose, _Subject).Should().NotBe(first);
    }

    [Fact]
    public void should_fingerprint_a_subject_larger_than_the_stack_buffer()
    {
        // given
        var fingerprinter = _CreateServices().GetRequiredService<AttemptSubjectFingerprinter>();
        var subject = new string('a', 100_000);

        // when
        var fingerprint = fingerprinter.Compute(_Purpose, subject);

        // then
        fingerprint.Should().Be(fingerprinter.Compute(_Purpose, subject));
        fingerprint.Should().NotBe(fingerprinter.Compute(_Purpose, subject + "b"));
    }

    [Theory]
    [InlineData("", _Subject)]
    [InlineData(_Purpose, "")]
    public async Task should_reject_an_empty_purpose_or_subject_when_peeking_or_resetting(
        string purpose,
        string subject
    )
    {
        // given
        var limiter = _CreateServices().GetRequiredService<IAttemptLimiter>();

        // when
        var peek = async () => await limiter.PeekAsync(purpose, subject, _ThreePerMinute, AbortToken);
        var reset = async () => await limiter.ResetAsync(purpose, subject, _ThreePerMinute, AbortToken);

        // then
        await peek.Should().ThrowAsync<ArgumentException>();
        await reset.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task should_report_the_limiter_unavailable_without_the_store_text_when_a_peek_fails()
    {
        // given
        var store = Substitute.For<ICache>();
        var failure = new InvalidOperationException("It was not possible to connect to redis-prod-01:6379");
        store
            .GetAsync<long>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask<CacheValue<long>>>(_ => throw failure);
        var limiter = _CreateServices(store).GetRequiredService<IAttemptLimiter>();

        // when
        var act = async () => await limiter.PeekAsync(_Purpose, _Subject, _ThreePerMinute, AbortToken);

        // then
        var thrown = (await act.Should().ThrowAsync<AttemptLimiterUnavailableException>()).Which;
        thrown.Purpose.Should().Be(_Purpose);
        thrown.InnerException.Should().BeSameAs(failure);
        thrown.Message.Should().NotContain("redis-prod-01").And.NotContain(_Subject);
    }

    [Fact]
    public async Task should_report_the_limiter_unavailable_when_an_operator_reset_cannot_reach_the_store()
    {
        // given
        var store = Substitute.For<ICache>();
        store
            .RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask<bool>>(_ => throw new TimeoutException("redis timed out"));
        var limiter = _CreateServices(store).GetRequiredService<IAttemptLimiter>();

        // when
        var act = async () => await limiter.ResetAsync(_Purpose, _Subject, _ThreePerMinute, AbortToken);

        // then
        (await act.Should().ThrowAsync<AttemptLimiterUnavailableException>())
            .Which.Purpose.Should()
            .Be(_Purpose);
    }

    [Fact]
    public async Task should_surface_the_callers_cancellation_when_a_peek_is_cancelled()
    {
        // given
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var store = Substitute.For<ICache>();
        store
            .GetAsync<long>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask<CacheValue<long>>>(call =>
                throw new OperationCanceledException(call.Arg<CancellationToken>())
            );
        var limiter = _CreateServices(store).GetRequiredService<IAttemptLimiter>();

        // when
        var act = async () => await limiter.PeekAsync(_Purpose, _Subject, _ThreePerMinute, cancellation.Token);

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData(-1, 3)]
    [InlineData(0, 0)]
    public void should_refuse_a_snapshot_that_contradicts_the_limiter(long count, int limit)
    {
        var act = () =>
            new AttemptSnapshot(_Purpose, "fp", count, limit, _Start, _Start.AddMinutes(1), TimeSpan.FromSeconds(1));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void should_refuse_a_snapshot_whose_window_does_not_end_after_it_starts()
    {
        var act = () => new AttemptSnapshot(_Purpose, "fp", 0, 3, _Start, _Start, TimeSpan.FromSeconds(1));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private ServiceProvider _CreateServices(ICache? cache = null, string? subjectKey = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(cache ?? _cache);
        services.AddSingleton<TimeProvider>(_timeProvider);
        services.AddAttemptLimiter(options => options.SubjectKey = subjectKey ?? _SubjectKey);

        return services.BuildServiceProvider();
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        _cache.Dispose();
        await base.DisposeAsyncCore();
    }
}
