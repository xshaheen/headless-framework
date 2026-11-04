// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Caching;
using Headless.Primitives;
using Headless.RateLimiting;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class CacheAttemptLimiterTests : TestBase
{
    private const string _Purpose = "otp-delivery";
    private const string _Subject = "+201000000000";

    private static readonly string _SubjectKey = Convert.ToBase64String(new byte[32]);
    private static readonly AttemptQuota _FivePerMinute = new(5, TimeSpan.FromMinutes(1));

    // 10s into a minute-aligned window, so the window closes in 50s.
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 9, 25, 10, 0, 10, TimeSpan.Zero));

    private readonly InMemoryCache _cache;

    public CacheAttemptLimiterTests()
    {
        _cache = new InMemoryCache(_timeProvider, new InMemoryCacheOptions());
    }

    [Fact]
    public async Task should_admit_attempts_up_to_the_limit_and_refuse_the_next()
    {
        // given
        var limiter = _CreateLimiter();
        var results = new List<AttemptResult>();

        // when
        for (var i = 0; i < 6; i++)
        {
            results.Add(await limiter.AcquireAsync(_Purpose, _Subject, _FivePerMinute, AbortToken));
        }

        // then
        results.Take(5).Should().AllSatisfy(r => r.IsAllowed.Should().BeTrue());
        results[4].Remaining.Should().Be(0);
        results[5].IsAllowed.Should().BeFalse();
        results.Select(r => r.Count).Should().Equal(1, 2, 3, 4, 5, 6);
    }

    [Fact]
    public async Task should_keep_charging_refused_attempts()
    {
        // given
        var limiter = _CreateLimiter();
        var quota = new AttemptQuota(1, TimeSpan.FromMinutes(1));
        await limiter.AcquireAsync(_Purpose, _Subject, quota, AbortToken);

        // when
        await limiter.AcquireAsync(_Purpose, _Subject, quota, AbortToken);
        var third = await limiter.AcquireAsync(_Purpose, _Subject, quota, AbortToken);

        // then
        third.IsAllowed.Should().BeFalse();
        third.Count.Should().Be(3);
    }

    [Fact]
    public async Task should_report_retry_after_as_time_left_in_the_aligned_window()
    {
        // given
        var limiter = _CreateLimiter();

        // when
        var result = await limiter.AcquireAsync(_Purpose, _Subject, _FivePerMinute, AbortToken);

        // then
        result.RetryAfter.Should().Be(TimeSpan.FromSeconds(50));
    }

    [Fact]
    public async Task should_open_a_fresh_budget_when_the_window_rolls_over()
    {
        // given
        var limiter = _CreateLimiter();
        var quota = new AttemptQuota(1, TimeSpan.FromMinutes(1));
        await limiter.AcquireAsync(_Purpose, _Subject, quota, AbortToken);
        (await limiter.AcquireAsync(_Purpose, _Subject, quota, AbortToken)).IsAllowed.Should().BeFalse();

        // when
        _timeProvider.Advance(TimeSpan.FromSeconds(50));
        var result = await limiter.AcquireAsync(_Purpose, _Subject, quota, AbortToken);

        // then
        result.IsAllowed.Should().BeTrue();
        result.Count.Should().Be(1);
        result.RetryAfter.Should().Be(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task should_apply_a_lowered_limit_to_the_running_window()
    {
        // given
        var limiter = _CreateLimiter();

        for (var i = 0; i < 3; i++)
        {
            await limiter.AcquireAsync(_Purpose, _Subject, _FivePerMinute, AbortToken);
        }

        // when
        var result = await limiter.AcquireAsync(
            _Purpose,
            _Subject,
            new AttemptQuota(3, TimeSpan.FromMinutes(1)),
            AbortToken
        );

        // then
        result.IsAllowed.Should().BeFalse();
        result.Limit.Should().Be(3);
    }

    [Fact]
    public async Task should_start_a_fresh_counter_when_the_window_length_changes()
    {
        // given
        var limiter = _CreateLimiter();
        await limiter.AcquireAsync(_Purpose, _Subject, _FivePerMinute, AbortToken);

        // when
        var result = await limiter.AcquireAsync(
            _Purpose,
            _Subject,
            new AttemptQuota(5, TimeSpan.FromMinutes(2)),
            AbortToken
        );

        // then
        result.Count.Should().Be(1);
    }

    [Fact]
    public async Task should_count_purposes_and_subjects_separately()
    {
        // given
        var limiter = _CreateLimiter();
        await limiter.AcquireAsync(_Purpose, _Subject, _FivePerMinute, AbortToken);

        // when
        var otherPurpose = await limiter.AcquireAsync("password-reset", _Subject, _FivePerMinute, AbortToken);
        var otherSubject = await limiter.AcquireAsync(_Purpose, "+201111111111", _FivePerMinute, AbortToken);

        // then
        otherPurpose.Count.Should().Be(1);
        otherSubject.Count.Should().Be(1);
    }

    [Fact]
    public async Task should_not_share_counters_across_subject_keys()
    {
        // given
        var limiter = _CreateLimiter();
        var rotated = _CreateLimiter(Convert.ToBase64String(Enumerable.Repeat((byte)7, 32).ToArray()));
        await limiter.AcquireAsync(_Purpose, _Subject, _FivePerMinute, AbortToken);

        // when
        var result = await rotated.AcquireAsync(_Purpose, _Subject, _FivePerMinute, AbortToken);

        // then
        result.Count.Should().Be(1);
    }

    [Fact]
    public async Task should_count_a_multi_megabyte_subject_when_it_exceeds_the_stack()
    {
        // given: a subject far larger than a thread stack, as a request body field can carry
        var limiter = _CreateLimiter();
        var subject = new string('a', 4_000_000);
        await limiter.AcquireAsync(_Purpose, subject, _FivePerMinute, AbortToken);

        // when
        var result = await limiter.AcquireAsync(_Purpose, subject, _FivePerMinute, AbortToken);
        var other = await limiter.AcquireAsync(_Purpose, subject + "b", _FivePerMinute, AbortToken);

        // then
        result.Count.Should().Be(2);
        other.Count.Should().Be(1);
    }

    [Fact]
    public async Task should_clear_the_budget_on_reset()
    {
        // given
        var limiter = _CreateLimiter();
        var quota = new AttemptQuota(2, TimeSpan.FromMinutes(1));
        await limiter.AcquireAsync(_Purpose, _Subject, quota, AbortToken);
        var second = await limiter.AcquireAsync(_Purpose, _Subject, quota, AbortToken);

        // when
        await limiter.ResetAsync(second, AbortToken);
        var result = await limiter.AcquireAsync(_Purpose, _Subject, quota, AbortToken);

        // then
        result.Count.Should().Be(1);
    }

    [Fact]
    public async Task should_throw_too_many_requests_with_retry_after_when_rejected()
    {
        // given
        var limiter = _CreateLimiter();
        var quota = new AttemptQuota(1, TimeSpan.FromMinutes(1));
        var error = new ErrorDescriptor("otp_attempts_exceeded", "Too many codes.");
        await limiter.AcquireAsync(_Purpose, _Subject, quota, AbortToken);
        var rejected = await limiter.AcquireAsync(_Purpose, _Subject, quota, AbortToken);

        // when
        var act = () => rejected.ThrowIfRejected(error);

        // then
        var exception = act.Should().ThrowExactly<TooManyRequestsException>().Which;
        exception.RetryAfter.Should().Be(TimeSpan.FromSeconds(50));
        exception.Error.Should().Be(error);
    }

    [Fact]
    public async Task should_not_throw_when_the_attempt_is_allowed()
    {
        // given
        var limiter = _CreateLimiter();
        var allowed = await limiter.AcquireAsync(_Purpose, _Subject, _FivePerMinute, AbortToken);

        // when
        var act = () => allowed.ThrowIfRejected();

        // then
        act.Should().NotThrow();
    }

    [Fact]
    public async Task should_refuse_to_reset_a_result_without_a_reset_token()
    {
        // given
        var limiter = _CreateLimiter();
        var stub = new AttemptResult(_Purpose, count: 1, limit: 5, retryAfter: TimeSpan.FromSeconds(30));

        // when
        var act = async () => await limiter.ResetAsync(stub, AbortToken);

        // then
        (await act.Should().ThrowAsync<ArgumentException>())
            .Which.ParamName.Should()
            .Be("attempt");
    }

    [Theory]
    [InlineData("", _Subject)]
    [InlineData(_Purpose, "")]
    public async Task should_reject_empty_purpose_or_subject(string purpose, string subject)
    {
        // given
        var limiter = _CreateLimiter();

        // when
        var act = async () => await limiter.AcquireAsync(purpose, subject, _FivePerMinute, AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task should_report_the_limiter_unavailable_without_the_store_text_when_the_counter_store_fails()
    {
        // given - a store whose own message names the server, as a Redis connection failure does
        var store = Substitute.For<ICache>();
        var failure = new InvalidOperationException("It was not possible to connect to redis-prod-01:6379");
        store
            .IncrementAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask<long>>(_ => throw failure);
        var limiter = _CreateLimiter(cache: store);

        // when
        var act = async () => await limiter.AcquireAsync(_Purpose, _Subject, _FivePerMinute, AbortToken);

        // then
        var thrown = (await act.Should().ThrowAsync<AttemptLimiterUnavailableException>()).Which;
        thrown.Purpose.Should().Be(_Purpose);
        thrown.InnerException.Should().BeSameAs(failure);
        thrown.Message.Should().NotContain("redis-prod-01");
        thrown.Should().BeAssignableTo<ServiceUnavailableException>();
    }

    [Fact]
    public async Task should_report_the_limiter_unavailable_when_a_reset_cannot_reach_the_counter_store()
    {
        // given
        var limiter = _CreateLimiter();
        var attempt = await limiter.AcquireAsync(_Purpose, _Subject, _FivePerMinute, AbortToken);
        var store = Substitute.For<ICache>();
        store
            .RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask<bool>>(_ => throw new TimeoutException("redis timed out"));
        var failingLimiter = _CreateLimiter(cache: store);

        // when
        var act = async () => await failingLimiter.ResetAsync(attempt, AbortToken);

        // then
        (await act.Should().ThrowAsync<AttemptLimiterUnavailableException>())
            .Which.Purpose.Should()
            .Be(_Purpose);
    }

    [Fact]
    public async Task should_surface_the_callers_cancellation_as_cancellation()
    {
        // given
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var store = Substitute.For<ICache>();
        store
            .IncrementAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask<long>>(call => throw new OperationCanceledException(call.Arg<CancellationToken>()));
        var limiter = _CreateLimiter(cache: store);

        // when
        var act = async () => await limiter.AcquireAsync(_Purpose, _Subject, _FivePerMinute, cancellation.Token);

        // then - an aborted request is reported as aborted, not as a store outage
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private IAttemptLimiter _CreateLimiter(string? subjectKey = null, ICache? cache = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(cache ?? _cache);
        services.AddSingleton<TimeProvider>(_timeProvider);
        services.AddAttemptLimiter(options => options.SubjectKey = subjectKey ?? _SubjectKey);

        return services.BuildServiceProvider().GetRequiredService<IAttemptLimiter>();
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        _cache.Dispose();
        await base.DisposeAsyncCore();
    }
}
