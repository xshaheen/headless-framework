// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using FluentValidation.TestHelper;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class DateTimeValidatorsDateTimeOffsetTests
{
    private static readonly DateTimeOffset _Now = new(2026, 6, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset _Past = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset _Future = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static FakeTimeProvider _Clock() => new(_Now);

    private static DateTimeOffset _Resolve(string when) =>
        when switch
        {
            "past" => _Past,
            "now" => _Now,
            "future" => _Future,
            _ => throw new ArgumentOutOfRangeException(nameof(when)),
        };

    private sealed record Model(DateTimeOffset Value);

    private sealed record NullableModel(DateTimeOffset? Value);

    #region InThePast

    [Theory]
    [InlineData("past", false)]
    [InlineData("now", true)]
    [InlineData("future", true)]
    public void in_the_past(string when, bool expectError)
    {
        var validator = new InlineValidator<Model>();
        validator.RuleFor(x => x.Value).InThePast(_Clock());

        var result = validator.TestValidate(new Model(_Resolve(when)));

        if (expectError)
        {
            result.ShouldHaveValidationErrorFor(x => x.Value).WithErrorCode("g:must_be_in_past");
        }
        else
        {
            result.ShouldNotHaveValidationErrorFor(x => x.Value);
        }
    }

    #endregion

    #region InTheFuture

    [Theory]
    [InlineData("past", true)]
    [InlineData("now", true)]
    [InlineData("future", false)]
    public void in_the_future(string when, bool expectError)
    {
        var validator = new InlineValidator<Model>();
        validator.RuleFor(x => x.Value).InTheFuture(_Clock());

        var result = validator.TestValidate(new Model(_Resolve(when)));

        if (expectError)
        {
            result.ShouldHaveValidationErrorFor(x => x.Value).WithErrorCode("g:must_be_in_future");
        }
        else
        {
            result.ShouldNotHaveValidationErrorFor(x => x.Value);
        }
    }

    #endregion

    #region NotInThePast

    [Theory]
    [InlineData("past", true)]
    [InlineData("now", false)]
    [InlineData("future", false)]
    public void not_in_the_past(string when, bool expectError)
    {
        var validator = new InlineValidator<Model>();
        validator.RuleFor(x => x.Value).NotInThePast(_Clock());

        var result = validator.TestValidate(new Model(_Resolve(when)));

        if (expectError)
        {
            result.ShouldHaveValidationErrorFor(x => x.Value).WithErrorCode("g:must_not_be_in_past");
        }
        else
        {
            result.ShouldNotHaveValidationErrorFor(x => x.Value);
        }
    }

    #endregion

    #region NotInTheFuture

    [Theory]
    [InlineData("past", false)]
    [InlineData("now", false)]
    [InlineData("future", true)]
    public void not_in_the_future(string when, bool expectError)
    {
        var validator = new InlineValidator<Model>();
        validator.RuleFor(x => x.Value).NotInTheFuture(_Clock());

        var result = validator.TestValidate(new Model(_Resolve(when)));

        if (expectError)
        {
            result.ShouldHaveValidationErrorFor(x => x.Value).WithErrorCode("g:must_not_be_in_future");
        }
        else
        {
            result.ShouldNotHaveValidationErrorFor(x => x.Value);
        }
    }

    #endregion

    #region Nullable passes through when null

    [Fact]
    public void nullable_rules_pass_when_value_is_null()
    {
        var validator = new InlineValidator<NullableModel>();
        validator.RuleFor(x => x.Value).InThePast(_Clock());
        validator.RuleFor(x => x.Value).InTheFuture(_Clock());
        validator.RuleFor(x => x.Value).NotInThePast(_Clock());
        validator.RuleFor(x => x.Value).NotInTheFuture(_Clock());

        var result = validator.TestValidate(new NullableModel(Value: null));

        result.ShouldNotHaveValidationErrorFor(x => x.Value);
    }

    [Fact]
    public void nullable_in_the_past_validates_present_value()
    {
        var validator = new InlineValidator<NullableModel>();
        validator.RuleFor(x => x.Value).InThePast(_Clock());

        var result = validator.TestValidate(new NullableModel(_Future));

        result.ShouldHaveValidationErrorFor(x => x.Value).WithErrorCode("g:must_be_in_past");
    }

    #endregion
}
