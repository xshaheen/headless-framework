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
    public void should_grow_exponentially_within_jitter_band_when_backoff(int attempt, double expectedBaseMs)
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
    public void should_cap_at_three_seconds_for_high_attempts_when_backoff(int attempt)
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

    public static TheoryData<ExceptionKind> TransientExceptions =>
        [ExceptionKind.Timeout, ExceptionKind.IOException, ExceptionKind.InvalidData];

    public static TheoryData<ExceptionKind> NonTransientExceptions =>
        [
            ExceptionKind.OperationCanceled,
            ExceptionKind.TaskCanceled,
            ExceptionKind.ObjectDisposed,
            ExceptionKind.InvalidOperation,
            ExceptionKind.Argument,
            ExceptionKind.ArgumentNull,
            ExceptionKind.ArgumentOutOfRange,
        ];

    [Theory]
    [MemberData(nameof(TransientExceptions))]
    public void should_be_classified_retryable_when_transient_storage_exceptions(ExceptionKind exceptionKind)
    {
        var exception = _CreateException(exceptionKind);

        DistributedLockCoreHelpers.IsTransientStorageException(exception).Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(NonTransientExceptions))]
    public void should_not_be_classified_retryable_when_programmer_errors_and_cancellation(ExceptionKind exceptionKind)
    {
        var exception = _CreateException(exceptionKind);

        DistributedLockCoreHelpers.IsTransientStorageException(exception).Should().BeFalse();
    }

    private static Exception _CreateException(ExceptionKind kind)
    {
        return kind switch
        {
            ExceptionKind.Timeout => new TimeoutException(),
            ExceptionKind.IOException => new IOException("connection reset"),
            ExceptionKind.InvalidData => new InvalidDataException("storage blip"),
            ExceptionKind.OperationCanceled => new OperationCanceledException(),
            ExceptionKind.TaskCanceled => new TaskCanceledException(),
            ExceptionKind.ObjectDisposed => new ObjectDisposedException("storage"),
            ExceptionKind.InvalidOperation => new InvalidOperationException(),
            ExceptionKind.Argument => new ArgumentException("bad resource", nameof(kind)),
            ExceptionKind.ArgumentNull => new ArgumentNullException(nameof(kind)),
            ExceptionKind.ArgumentOutOfRange => new ArgumentOutOfRangeException(nameof(kind)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    #endregion

    #region NormalizeTimeUntilExpires

    [Fact]
    public void should_fall_back_to_default_when_normalize_null()
    {
        // given
        var defaultTtl = TimeSpan.FromMinutes(20);

        // when
        var result = DistributedLockCoreHelpers.NormalizeTimeUntilExpires(null, defaultTtl);

        // then
        result.Should().Be(defaultTtl);
    }

    [Fact]
    public void should_translate_infinite_to_null_when_normalize()
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
    public void should_pass_finite_positive_value_through_when_normalize()
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
    public void should_reject_zero_or_negative_values_when_normalize(int seconds)
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
    public void should_reject_values_above_int_max_milliseconds_when_normalize()
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
    public void should_return_finite_duration_unchanged_when_require_finite(bool monitorLease)
    {
        // given
        var ttl = TimeSpan.FromSeconds(15);

        // when
        var result = DistributedLockCoreHelpers.RequireFiniteLeaseDuration(ttl, monitorLease);

        // then
        result.Should().Be(ttl);
    }

    [Fact]
    public void should_throw_when_require_finite_monitoring_an_infinite_lease()
    {
        // when — a monitored lease must have a finite duration for the renewal loop to schedule against
        var act = () => DistributedLockCoreHelpers.RequireFiniteLeaseDuration(null, monitorLease: true);

        // then
        act.Should().Throw<ArgumentException>().WithParameterName("timeUntilExpires");
    }

    [Fact]
    public void should_allow_infinite_lease_without_monitoring_when_require_finite()
    {
        // when
        var result = DistributedLockCoreHelpers.RequireFiniteLeaseDuration(null, monitorLease: false);

        // then
        result.Should().Be(Timeout.InfiniteTimeSpan);
    }

    #endregion

    #region GetWriterWaitingId

    [Fact]
    public void should_append_the_shared_suffix_when_writer_waiting_id()
    {
        // when
        var markerId = DistributedLockCoreHelpers.GetWriterWaitingId("lease-1");

        // then — the literal is embedded in the provider Lua scripts as well; changing it here
        // without changing the scripts would desynchronize the two surfaces.
        markerId.Should().Be("lease-1:_WRITERWAITING");
    }

    #endregion

    public enum ExceptionKind
    {
        None = 0,
        Timeout = 1,
        IOException = 2,
        InvalidData = 3,
        OperationCanceled = 4,
        TaskCanceled = 5,
        ObjectDisposed = 6,
        InvalidOperation = 7,
        Argument = 8,
        ArgumentNull = 9,
        ArgumentOutOfRange = 10,
    }
}
