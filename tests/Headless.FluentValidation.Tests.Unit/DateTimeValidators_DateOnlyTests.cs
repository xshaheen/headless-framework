// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using FluentValidation.TestHelper;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class DateTimeValidatorsDateOnlyTests
{
    // Current UTC date resolves to 2026-06-27.
    private static readonly DateTimeOffset _Now = new(2026, 6, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly _Today = new(2026, 6, 27);
    private static readonly DateOnly _Past = new(2020, 1, 1);
    private static readonly DateOnly _Future = new(2030, 1, 1);

    private static FakeTimeProvider _Clock()
    {
        return new(_Now);
    }

    private static DateOnly _Resolve(string when)
    {
        return when switch
        {
            "past" => _Past,
            "today" => _Today,
            "future" => _Future,
            _ => throw new ArgumentOutOfRangeException(nameof(when)),
        };
    }

    private sealed record Model(DateOnly Value);

    private sealed record NullableModel(DateOnly? Value);

    #region Relative rules

    [Theory]
    [InlineData("past", false)]
    [InlineData("today", true)]
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

    [Theory]
    [InlineData("past", true)]
    [InlineData("today", false)]
    [InlineData("future", false)]
    public void not_in_the_past(string when, bool expectError)
    {
        var validator = new InlineValidator<Model>();
        validator.RuleFor(x => x.Value).NotInThePast(_Clock());

        var result = validator.TestValidate(new Model(_Resolve(when)));

        if (expectError)
        {
            result.ShouldHaveValidationErrorFor(x => x.Value);
        }
        else
        {
            result.ShouldNotHaveValidationErrorFor(x => x.Value);
        }
    }

    [Theory]
    [InlineData("past", true)]
    [InlineData("today", true)]
    [InlineData("future", false)]
    public void in_the_future(string when, bool expectError)
    {
        var validator = new InlineValidator<Model>();
        validator.RuleFor(x => x.Value).InTheFuture(_Clock());

        var result = validator.TestValidate(new Model(_Resolve(when)));

        if (expectError)
        {
            result.ShouldHaveValidationErrorFor(x => x.Value);
        }
        else
        {
            result.ShouldNotHaveValidationErrorFor(x => x.Value);
        }
    }

    [Theory]
    [InlineData("past", false)]
    [InlineData("today", false)]
    [InlineData("future", true)]
    public void not_in_the_future(string when, bool expectError)
    {
        var validator = new InlineValidator<Model>();
        validator.RuleFor(x => x.Value).NotInTheFuture(_Clock());

        var result = validator.TestValidate(new Model(_Resolve(when)));

        if (expectError)
        {
            result.ShouldHaveValidationErrorFor(x => x.Value);
        }
        else
        {
            result.ShouldNotHaveValidationErrorFor(x => x.Value);
        }
    }

    #endregion

    #region MinimumAge

    [Fact]
    public void minimum_age_passes_when_older_than_required()
    {
        var validator = new InlineValidator<Model>();
        validator.RuleFor(x => x.Value).MinimumAge(18, _Clock());

        var result = validator.TestValidate(new Model(new DateOnly(2000, 6, 27)));

        result.ShouldNotHaveValidationErrorFor(x => x.Value);
    }

    [Fact]
    public void minimum_age_passes_exactly_on_birthday()
    {
        var validator = new InlineValidator<Model>();
        validator.RuleFor(x => x.Value).MinimumAge(18, _Clock());

        // Born exactly 18 years ago today.
        var result = validator.TestValidate(new Model(new DateOnly(2008, 6, 27)));

        result.ShouldNotHaveValidationErrorFor(x => x.Value);
    }

    [Fact]
    public void minimum_age_fails_one_day_before_birthday()
    {
        var validator = new InlineValidator<Model>();
        validator.RuleFor(x => x.Value).MinimumAge(18, _Clock());

        // Born 2008-06-28: the 18th birthday is tomorrow, so the current age is 17.
        var result = validator.TestValidate(new Model(new DateOnly(2008, 6, 28)));

        result.ShouldHaveValidationErrorFor(x => x.Value).WithErrorCode("g:minimum_age");
    }

    [Fact]
    public void minimum_age_fails_for_future_birth_date()
    {
        var validator = new InlineValidator<Model>();
        validator.RuleFor(x => x.Value).MinimumAge(1, _Clock());

        var result = validator.TestValidate(new Model(_Future));

        result.ShouldHaveValidationErrorFor(x => x.Value);
    }

    [Fact]
    public void minimum_age_throws_for_negative_argument()
    {
        var validator = new InlineValidator<Model>();

        Action act = () => validator.RuleFor(x => x.Value).MinimumAge(-1, _Clock());

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    #endregion

    #region Nullable

    [Fact]
    public void nullable_rules_pass_when_value_is_null()
    {
        var validator = new InlineValidator<NullableModel>();
        validator.RuleFor(x => x.Value).InThePast(_Clock());
        validator.RuleFor(x => x.Value).NotInTheFuture(_Clock());
        validator.RuleFor(x => x.Value).MinimumAge(18, _Clock());

        var result = validator.TestValidate(new NullableModel(Value: null));

        result.ShouldNotHaveValidationErrorFor(x => x.Value);
    }

    [Fact]
    public void nullable_minimum_age_validates_present_value()
    {
        var validator = new InlineValidator<NullableModel>();
        validator.RuleFor(x => x.Value).MinimumAge(18, _Clock());

        var result = validator.TestValidate(new NullableModel(new DateOnly(2020, 1, 1)));

        result.ShouldHaveValidationErrorFor(x => x.Value).WithErrorCode("g:minimum_age");
    }

    #endregion
}
