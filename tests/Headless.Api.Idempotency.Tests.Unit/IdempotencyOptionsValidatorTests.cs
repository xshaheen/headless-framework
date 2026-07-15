// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation.TestHelper;
using Headless.Api.Idempotency;

namespace Tests;

public sealed class IdempotencyOptionsValidatorTests
{
    private readonly IdempotencyOptionsValidator _sut = new();

    [Fact]
    public void should_pass_for_default_options()
    {
        var result = _sut.TestValidate(new IdempotencyOptions());

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void should_fail_when_expiration_is_zero()
    {
        var options = _CreateValidOptions();
        options.IdempotencyKeyExpiration = TimeSpan.Zero;

        var result = _sut.TestValidate(options);

        result.ShouldHaveValidationErrorFor(x => x.IdempotencyKeyExpiration);
    }

    [Fact]
    public void should_fail_when_expiration_is_negative()
    {
        var options = _CreateValidOptions();
        options.IdempotencyKeyExpiration = TimeSpan.FromDays(-1);

        var result = _sut.TestValidate(options);

        result.ShouldHaveValidationErrorFor(x => x.IdempotencyKeyExpiration);
    }

    [Fact]
    public void should_fail_when_max_body_size_is_zero()
    {
        var options = _CreateValidOptions();
        options.MaxBodySizeForHashing = 0;

        var result = _sut.TestValidate(options);

        result.ShouldHaveValidationErrorFor(x => x.MaxBodySizeForHashing);
    }

    [Fact]
    public void should_fail_when_max_body_size_is_negative()
    {
        var options = _CreateValidOptions();
        options.MaxBodySizeForHashing = -1;

        var result = _sut.TestValidate(options);

        result.ShouldHaveValidationErrorFor(x => x.MaxBodySizeForHashing);
    }

    [Fact]
    public void should_fail_when_max_body_size_exceeds_64_mib()
    {
        var options = _CreateValidOptions();
        options.MaxBodySizeForHashing = (64 * 1024 * 1024) + 1;

        var result = _sut.TestValidate(options);

        result.ShouldHaveValidationErrorFor(x => x.MaxBodySizeForHashing);
    }

    [Fact]
    public void should_pass_when_max_body_size_is_exactly_64_mib()
    {
        var options = _CreateValidOptions();
        options.MaxBodySizeForHashing = 64 * 1024 * 1024;

        var result = _sut.TestValidate(options);

        result.ShouldNotHaveValidationErrorFor(x => x.MaxBodySizeForHashing);
    }

    [Fact]
    public void should_fail_when_in_flight_lock_timeout_is_zero()
    {
        var options = _CreateValidOptions();
        options.InFlightLockTimeout = TimeSpan.Zero;

        var result = _sut.TestValidate(options);

        result.ShouldHaveValidationErrorFor(x => x.InFlightLockTimeout);
    }

    [Fact]
    public void should_fail_when_header_name_is_empty()
    {
        var options = _CreateValidOptions();
        options.HeaderName = string.Empty;

        var result = _sut.TestValidate(options);

        result.ShouldHaveValidationErrorFor(x => x.HeaderName);
    }

    [Fact]
    public void should_fail_when_header_name_is_null()
    {
        var options = _CreateValidOptions();
        options.HeaderName = null!;

        var result = _sut.TestValidate(options);

        result.ShouldHaveValidationErrorFor(x => x.HeaderName);
    }

    [Fact]
    public void should_fail_when_methods_is_empty()
    {
        var options = _CreateValidOptions();
        options.Methods = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var result = _sut.TestValidate(options);

        result.ShouldHaveValidationErrorFor(x => x.Methods);
    }

    [Fact]
    public void should_fail_when_methods_contains_get()
    {
        var options = _CreateValidOptions();
        options.Methods = new HashSet<string>(["GET"], StringComparer.OrdinalIgnoreCase);

        var result = _sut.TestValidate(options);

        result.ShouldHaveValidationErrorFor("Methods[0]");
    }

    [Fact]
    public void should_fail_when_methods_contains_unrecognized_method()
    {
        var options = _CreateValidOptions();
        options.Methods = new HashSet<string>(["FROBNICATE"], StringComparer.OrdinalIgnoreCase);

        var result = _sut.TestValidate(options);

        result.ShouldHaveValidationErrorFor("Methods[0]");
    }

    [Fact]
    public void should_fail_when_replay_allowlist_is_null()
    {
        var options = _CreateValidOptions();
        options.ReplayHeaderAllowlist = null!;

        var result = _sut.TestValidate(options);

        result.ShouldHaveValidationErrorFor(x => x.ReplayHeaderAllowlist);
    }

    [Fact]
    public void should_pass_when_replay_allowlist_is_empty()
    {
        var options = _CreateValidOptions();
        options.ReplayHeaderAllowlist = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var result = _sut.TestValidate(options);

        result.ShouldNotHaveValidationErrorFor(x => x.ReplayHeaderAllowlist);
    }

    [Fact]
    public void should_fail_when_mismatch_status_code_is_invalid()
    {
        var options = _CreateValidOptions();
        options.MismatchStatusCode = 400;

        var result = _sut.TestValidate(options);

        result.ShouldHaveValidationErrorFor(x => x.MismatchStatusCode);
    }

    [Fact]
    public void should_pass_when_mismatch_status_code_is_409()
    {
        var options = _CreateValidOptions();
        options.MismatchStatusCode = 409;

        var result = _sut.TestValidate(options);

        result.ShouldNotHaveValidationErrorFor(x => x.MismatchStatusCode);
    }

    [Fact]
    public void should_pass_when_mismatch_status_code_is_422()
    {
        var options = _CreateValidOptions();
        options.MismatchStatusCode = 422;

        var result = _sut.TestValidate(options);

        result.ShouldNotHaveValidationErrorFor(x => x.MismatchStatusCode);
    }

    [Fact]
    public void should_fail_when_wait_and_replay_with_lock_timeout_exceeding_5_minutes()
    {
        var options = _CreateValidOptions();
        options.InFlightStrategy = InFlightStrategy.WaitAndReplay;
        options.InFlightLockTimeout = TimeSpan.FromMinutes(10);

        var result = _sut.TestValidate(options);

        result.ShouldHaveValidationErrorFor(x => x.InFlightLockTimeout);
    }

    [Fact]
    public void should_pass_when_wait_and_replay_with_lock_timeout_within_5_minutes()
    {
        var options = _CreateValidOptions();
        options.InFlightStrategy = InFlightStrategy.WaitAndReplay;
        options.InFlightLockTimeout = TimeSpan.FromSeconds(30);

        var result = _sut.TestValidate(options);

        result.ShouldNotHaveValidationErrorFor(x => x.InFlightLockTimeout);
    }

    [Fact]
    public void should_pass_when_lock_timeout_exceeds_5_minutes_with_reject_strategy()
    {
        // The 5-minute cap only applies to WaitAndReplay; Reject has no upper bound.
        var options = _CreateValidOptions();
        options.InFlightStrategy = InFlightStrategy.Reject;
        options.InFlightLockTimeout = TimeSpan.FromMinutes(10);

        var result = _sut.TestValidate(options);

        result.ShouldNotHaveValidationErrorFor(x => x.InFlightLockTimeout);
    }

    [Fact]
    public void should_pass_when_all_nullable_delegates_are_null()
    {
        var options = _CreateValidOptions();
        options.KeyDeriver = null;
        options.RequestFingerprint = null;
        options.ShouldApply = null;
        options.ShouldCacheResponse = null;

        var result = _sut.TestValidate(options);

        result.ShouldNotHaveAnyValidationErrors();
    }

    private static IdempotencyOptions _CreateValidOptions()
    {
        return new();
    }
}
