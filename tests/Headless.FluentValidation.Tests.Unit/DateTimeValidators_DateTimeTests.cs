// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using FluentValidation.TestHelper;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class DateTimeValidatorsDateTimeTests
{
    private static readonly DateTimeOffset _Now = new(2026, 6, 27, 12, 0, 0, TimeSpan.Zero);

    private static FakeTimeProvider _Clock() => new(_Now);

    private sealed record Model(DateTime Value);

    private sealed record NullableModel(DateTime? Value);

    private static TestValidationResult<Model> _ValidateInThePast(DateTime value)
    {
        var validator = new InlineValidator<Model>();
        validator.RuleFor(x => x.Value).InThePast(_Clock());

        return validator.TestValidate(new Model(value));
    }

    #region Kind handling

    [Fact]
    public void in_the_past_passes_for_utc_past_value()
    {
        var result = _ValidateInThePast(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        result.ShouldNotHaveValidationErrorFor(x => x.Value);
    }

    [Fact]
    public void in_the_past_passes_for_unspecified_past_value_treated_as_utc()
    {
        var result = _ValidateInThePast(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Unspecified));

        result.ShouldNotHaveValidationErrorFor(x => x.Value);
    }

    [Fact]
    public void in_the_past_passes_for_local_past_value()
    {
        // A 2020 value stays in the past once converted to UTC regardless of the machine's offset (|offset| <= 14h).
        var result = _ValidateInThePast(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Local));

        result.ShouldNotHaveValidationErrorFor(x => x.Value);
    }

    [Fact]
    public void in_the_past_fails_for_utc_future_value()
    {
        var result = _ValidateInThePast(new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        result.ShouldHaveValidationErrorFor(x => x.Value);
    }

    #endregion

    #region Boundary (strict vs inclusive)

    [Fact]
    public void in_the_past_fails_for_value_equal_to_now()
    {
        var result = _ValidateInThePast(_Now.UtcDateTime);

        result.ShouldHaveValidationErrorFor(x => x.Value);
    }

    [Fact]
    public void not_in_the_past_passes_for_value_equal_to_now()
    {
        var validator = new InlineValidator<Model>();
        validator.RuleFor(x => x.Value).NotInThePast(_Clock());

        var result = validator.TestValidate(new Model(_Now.UtcDateTime));

        result.ShouldNotHaveValidationErrorFor(x => x.Value);
    }

    #endregion

    #region Future rules

    [Fact]
    public void in_the_future_passes_for_utc_future_value()
    {
        var validator = new InlineValidator<Model>();
        validator.RuleFor(x => x.Value).InTheFuture(_Clock());

        var result = validator.TestValidate(new Model(new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

        result.ShouldNotHaveValidationErrorFor(x => x.Value);
    }

    [Fact]
    public void not_in_the_future_fails_for_utc_future_value()
    {
        var validator = new InlineValidator<Model>();
        validator.RuleFor(x => x.Value).NotInTheFuture(_Clock());

        var result = validator.TestValidate(new Model(new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

        result.ShouldHaveValidationErrorFor(x => x.Value);
    }

    #endregion

    #region Error code + nullable

    [Fact]
    public void should_have_correct_error_code()
    {
        var result = _ValidateInThePast(new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        result.ShouldHaveValidationErrorFor(x => x.Value).WithErrorCode("datetime:must_be_in_past");
    }

    [Fact]
    public void nullable_rules_pass_when_value_is_null()
    {
        var validator = new InlineValidator<NullableModel>();
        validator.RuleFor(x => x.Value).InThePast(_Clock());
        validator.RuleFor(x => x.Value).NotInTheFuture(_Clock());

        var result = validator.TestValidate(new NullableModel(Value: null));

        result.ShouldNotHaveValidationErrorFor(x => x.Value);
    }

    #endregion
}
