// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using FluentValidation.TestHelper;

namespace Tests;

public sealed class IdValidatorsTests
{
    #region Guid

    private sealed class GuidModel
    {
        public Guid Id { get; init; }
    }

    private sealed class GuidModelValidator : AbstractValidator<GuidModel>
    {
        public GuidModelValidator()
        {
            RuleFor(x => x.Id).Id();
        }
    }

    [Fact]
    public void should_not_have_error_when_guid_is_valid()
    {
        var validator = new GuidModelValidator();
        var model = new GuidModel { Id = Guid.NewGuid() };

        var result = validator.TestValidate(model);

        result.ShouldNotHaveValidationErrorFor(x => x.Id);
    }

    [Fact]
    public void should_have_error_when_guid_is_empty()
    {
        var validator = new GuidModelValidator();
        var model = new GuidModel { Id = Guid.Empty };

        var result = validator.TestValidate(model);

        result.ShouldHaveValidationErrorFor(x => x.Id);
    }

    #endregion

    #region Guid?

    private sealed class NullableGuidModel
    {
        public Guid? Id { get; init; }
    }

    private sealed class NullableGuidModelValidator : AbstractValidator<NullableGuidModel>
    {
        public NullableGuidModelValidator()
        {
            RuleFor(x => x.Id).Id();
        }
    }

    [Fact]
    public void should_not_have_error_when_nullable_guid_is_null()
    {
        var validator = new NullableGuidModelValidator();
        var model = new NullableGuidModel { Id = null };

        var result = validator.TestValidate(model);

        result.ShouldNotHaveValidationErrorFor(x => x.Id);
    }

    [Fact]
    public void should_not_have_error_when_nullable_guid_is_valid()
    {
        var validator = new NullableGuidModelValidator();
        var model = new NullableGuidModel { Id = Guid.NewGuid() };

        var result = validator.TestValidate(model);

        result.ShouldNotHaveValidationErrorFor(x => x.Id);
    }

    [Fact]
    public void should_have_error_when_nullable_guid_is_empty()
    {
        var validator = new NullableGuidModelValidator();
        var model = new NullableGuidModel { Id = Guid.Empty };

        var result = validator.TestValidate(model);

        result.ShouldHaveValidationErrorFor(x => x.Id);
    }

    #endregion

    #region string?

    private sealed class StringIdModel
    {
        public string? Id { get; init; }
    }

    private sealed class StringIdModelValidator : AbstractValidator<StringIdModel>
    {
        public StringIdModelValidator()
        {
            RuleFor(x => x.Id).Id();
        }
    }

    [Fact]
    public void should_not_have_error_when_string_id_is_null()
    {
        var validator = new StringIdModelValidator();
        var model = new StringIdModel { Id = null };

        var result = validator.TestValidate(model);

        result.ShouldNotHaveValidationErrorFor(x => x.Id);
    }

    [Fact]
    public void should_not_have_error_when_string_id_is_valid()
    {
        var validator = new StringIdModelValidator();
        var model = new StringIdModel { Id = "abc123" };

        var result = validator.TestValidate(model);

        result.ShouldNotHaveValidationErrorFor(x => x.Id);
    }

    [Fact]
    public void should_have_error_when_string_id_is_empty()
    {
        var validator = new StringIdModelValidator();
        var model = new StringIdModel { Id = string.Empty };

        var result = validator.TestValidate(model);

        result.ShouldHaveValidationErrorFor(x => x.Id);
    }

    #endregion

    #region int

    private sealed class IntIdModel
    {
        public int Id { get; init; }
    }

    private sealed class IntIdModelValidator : AbstractValidator<IntIdModel>
    {
        public IntIdModelValidator()
        {
            RuleFor(x => x.Id).Id();
        }
    }

    [Fact]
    public void should_not_have_error_when_int_id_is_positive()
    {
        var validator = new IntIdModelValidator();
        var model = new IntIdModel { Id = 1 };

        var result = validator.TestValidate(model);

        result.ShouldNotHaveValidationErrorFor(x => x.Id);
    }

    [Fact]
    public void should_have_error_when_int_id_is_zero()
    {
        var validator = new IntIdModelValidator();
        var model = new IntIdModel { Id = 0 };

        var result = validator.TestValidate(model);

        result.ShouldHaveValidationErrorFor(x => x.Id);
    }

    [Fact]
    public void should_have_error_when_int_id_is_negative()
    {
        var validator = new IntIdModelValidator();
        var model = new IntIdModel { Id = -1 };

        var result = validator.TestValidate(model);

        result.ShouldHaveValidationErrorFor(x => x.Id);
    }

    #endregion

    #region int?

    private sealed class NullableIntIdModel
    {
        public int? Id { get; init; }
    }

    private sealed class NullableIntIdModelValidator : AbstractValidator<NullableIntIdModel>
    {
        public NullableIntIdModelValidator()
        {
            RuleFor(x => x.Id).Id();
        }
    }

    [Fact]
    public void should_not_have_error_when_nullable_int_id_is_null()
    {
        var validator = new NullableIntIdModelValidator();
        var model = new NullableIntIdModel { Id = null };

        var result = validator.TestValidate(model);

        result.ShouldNotHaveValidationErrorFor(x => x.Id);
    }

    [Fact]
    public void should_not_have_error_when_nullable_int_id_is_positive()
    {
        var validator = new NullableIntIdModelValidator();
        var model = new NullableIntIdModel { Id = 1 };

        var result = validator.TestValidate(model);

        result.ShouldNotHaveValidationErrorFor(x => x.Id);
    }

    [Fact]
    public void should_have_error_when_nullable_int_id_is_zero()
    {
        var validator = new NullableIntIdModelValidator();
        var model = new NullableIntIdModel { Id = 0 };

        var result = validator.TestValidate(model);

        result.ShouldHaveValidationErrorFor(x => x.Id);
    }

    #endregion

    #region long

    private sealed class LongIdModel
    {
        public long Id { get; init; }
    }

    private sealed class LongIdModelValidator : AbstractValidator<LongIdModel>
    {
        public LongIdModelValidator()
        {
            RuleFor(x => x.Id).Id();
        }
    }

    [Fact]
    public void should_not_have_error_when_long_id_is_positive()
    {
        var validator = new LongIdModelValidator();
        var model = new LongIdModel { Id = 1L };

        var result = validator.TestValidate(model);

        result.ShouldNotHaveValidationErrorFor(x => x.Id);
    }

    [Fact]
    public void should_have_error_when_long_id_is_zero()
    {
        var validator = new LongIdModelValidator();
        var model = new LongIdModel { Id = 0L };

        var result = validator.TestValidate(model);

        result.ShouldHaveValidationErrorFor(x => x.Id);
    }

    [Fact]
    public void should_have_error_when_long_id_is_negative()
    {
        var validator = new LongIdModelValidator();
        var model = new LongIdModel { Id = -1L };

        var result = validator.TestValidate(model);

        result.ShouldHaveValidationErrorFor(x => x.Id);
    }

    #endregion

    #region long?

    private sealed class NullableLongIdModel
    {
        public long? Id { get; init; }
    }

    private sealed class NullableLongIdModelValidator : AbstractValidator<NullableLongIdModel>
    {
        public NullableLongIdModelValidator()
        {
            RuleFor(x => x.Id).Id();
        }
    }

    [Fact]
    public void should_not_have_error_when_nullable_long_id_is_null()
    {
        var validator = new NullableLongIdModelValidator();
        var model = new NullableLongIdModel { Id = null };

        var result = validator.TestValidate(model);

        result.ShouldNotHaveValidationErrorFor(x => x.Id);
    }

    [Fact]
    public void should_not_have_error_when_nullable_long_id_is_positive()
    {
        var validator = new NullableLongIdModelValidator();
        var model = new NullableLongIdModel { Id = 1L };

        var result = validator.TestValidate(model);

        result.ShouldNotHaveValidationErrorFor(x => x.Id);
    }

    [Fact]
    public void should_have_error_when_nullable_long_id_is_zero()
    {
        var validator = new NullableLongIdModelValidator();
        var model = new NullableLongIdModel { Id = 0L };

        var result = validator.TestValidate(model);

        result.ShouldHaveValidationErrorFor(x => x.Id);
    }

    #endregion
}
