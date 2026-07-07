// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;

namespace Tests;

public sealed class DistributedLockCoreHelpersTests
{
    #region GetBackoffDelay

    [Theory]
    [InlineData(0, 50)]
    [InlineData(1, 100)]
    [InlineData(2, 200)]
    [InlineData(3, 400)]
    [InlineData(4, 800)]
    [InlineData(5, 1600)]
    public void backoff_should_grow_exponentially_within_jitter_band(int attempt, double expectedBaseMs)
    {
        // when
        var delay = DistributedLockCoreHelpers.GetBackoffDelay(attempt);

        // then — base * 2^attempt with ±25% jitter
        delay.TotalMilliseconds.Should().BeGreaterThanOrEqualTo(expectedBaseMs * 0.75);
        delay.TotalMilliseconds.Should().BeLessThanOrEqualTo(expectedBaseMs * 1.25);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(100)]
    [InlineData(int.MaxValue)]
    public void backoff_should_cap_at_three_seconds_for_high_attempts(int attempt)
    {
        // when — the shift is clamped (no overflow for huge attempt counts) and the delay is
        // capped at 3s before jitter is applied.
        var delay = DistributedLockCoreHelpers.GetBackoffDelay(attempt);

        // then — 3000ms cap with ±25% jitter
        delay.TotalMilliseconds.Should().BeGreaterThanOrEqualTo(3000 * 0.75);
        delay.TotalMilliseconds.Should().BeLessThanOrEqualTo(3000 * 1.25);
    }

    #endregion

    #region IsTransientStorageException

    public static TheoryData<Exception> TransientExceptions =>
        [new TimeoutException(), new IOException("connection reset"), new InvalidDataException("storage blip")];

    public static TheoryData<Exception> NonTransientExceptions =>
        [
            new OperationCanceledException(),
            new TaskCanceledException(),
            new ObjectDisposedException("storage"),
            new InvalidOperationException(),
            _ArgumentException(),
            _ArgumentNullException(),
            _ArgumentOutOfRangeException(),
        ];

    // MA0015 requires the paramName argument to resolve to a real parameter, so these factories
    // exist purely so nameof binds to one instead of a bare string literal the analyzer rejects.
    private static ArgumentException _ArgumentException(string resource = "resource") =>
        new("bad resource", nameof(resource));

    private static ArgumentNullException _ArgumentNullException(string resource = "resource") => new(nameof(resource));

    private static ArgumentOutOfRangeException _ArgumentOutOfRangeException(string resource = "resource") =>
        new(nameof(resource));

    [Theory]
    [MemberData(nameof(TransientExceptions), DisableDiscoveryEnumeration = true)]
    public void transient_storage_exceptions_should_be_classified_retryable(Exception exception)
    {
        DistributedLockCoreHelpers.IsTransientStorageException(exception).Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(NonTransientExceptions), DisableDiscoveryEnumeration = true)]
    public void programmer_errors_and_cancellation_should_not_be_classified_retryable(Exception exception)
    {
        DistributedLockCoreHelpers.IsTransientStorageException(exception).Should().BeFalse();
    }

    #endregion

    #region NormalizeTimeUntilExpires

    [Fact]
    public void normalize_should_fall_back_to_default_when_null()
    {
        // given
        var defaultTtl = TimeSpan.FromMinutes(20);

        // when
        var result = DistributedLockCoreHelpers.NormalizeTimeUntilExpires(null, defaultTtl);

        // then
        result.Should().Be(defaultTtl);
    }

    [Fact]
    public void normalize_should_translate_infinite_to_null()
    {
        // when
        var result = DistributedLockCoreHelpers.NormalizeTimeUntilExpires(
            Timeout.InfiniteTimeSpan,
            TimeSpan.FromMinutes(20)
        );

        // then — InfiniteTimeSpan means "no expiration", represented as null downstream
        result.Should().BeNull();
    }

    [Fact]
    public void normalize_should_pass_finite_positive_value_through()
    {
        // given
        var ttl = TimeSpan.FromSeconds(30);

        // when
        var result = DistributedLockCoreHelpers.NormalizeTimeUntilExpires(ttl, TimeSpan.FromMinutes(20));

        // then
        result.Should().Be(ttl);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void normalize_should_reject_zero_or_negative_values(int seconds)
    {
        // when
        var act = () =>
            DistributedLockCoreHelpers.NormalizeTimeUntilExpires(
                TimeSpan.FromSeconds(seconds),
                TimeSpan.FromMinutes(20)
            );

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void normalize_should_reject_values_above_int_max_milliseconds()
    {
        // given — lease TTLs travel to storage as int milliseconds (Redis PX); a larger value
        // would silently overflow on cast, so it must be rejected at validation time.
        var tooLarge = TimeSpan.FromMilliseconds((double)int.MaxValue + 1000);

        // when
        var act = () => DistributedLockCoreHelpers.NormalizeTimeUntilExpires(tooLarge, TimeSpan.FromMinutes(20));

        // then
        act.Should().Throw<ArgumentException>();
    }

    #endregion

    #region RequireFiniteLeaseDuration

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void require_finite_should_return_finite_duration_unchanged(bool monitorLease)
    {
        // given
        var ttl = TimeSpan.FromSeconds(15);

        // when
        var result = DistributedLockCoreHelpers.RequireFiniteLeaseDuration(ttl, monitorLease);

        // then
        result.Should().Be(ttl);
    }

    [Fact]
    public void require_finite_should_throw_when_monitoring_an_infinite_lease()
    {
        // when — a monitored lease must have a finite duration for the renewal loop to schedule against
        var act = () => DistributedLockCoreHelpers.RequireFiniteLeaseDuration(null, monitorLease: true);

        // then
        act.Should().Throw<ArgumentException>().WithParameterName("timeUntilExpires");
    }

    [Fact]
    public void require_finite_should_allow_infinite_lease_without_monitoring()
    {
        // when
        var result = DistributedLockCoreHelpers.RequireFiniteLeaseDuration(null, monitorLease: false);

        // then
        result.Should().Be(Timeout.InfiniteTimeSpan);
    }

    #endregion

    #region GetWriterWaitingId

    [Fact]
    public void writer_waiting_id_should_append_the_shared_suffix()
    {
        // when
        var markerId = DistributedLockCoreHelpers.GetWriterWaitingId("lease-1");

        // then — the literal is embedded in the provider Lua scripts as well; changing it here
        // without changing the scripts would desynchronize the two surfaces.
        markerId.Should().Be("lease-1:_WRITERWAITING");
    }

    #endregion
}
