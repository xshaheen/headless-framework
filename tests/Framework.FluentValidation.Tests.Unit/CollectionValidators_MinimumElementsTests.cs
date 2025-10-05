// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using FluentValidation.TestHelper;
using Framework.FluentValidation;

namespace Tests;

public sealed class CollectionValidatorsMinimumElementsTests
{
    private readonly Faker _faker = new();

    private sealed class TestModel
    {
        public IEnumerable<int>? Elements { get; init; }
    }

    private sealed class TestModelValidator : AbstractValidator<TestModel>
    {
        public TestModelValidator(int value)
        {
            RuleFor(x => x.Elements).MinimumElements(value);
        }
    }

    [Fact]
    public void should_not_have_error_when_elements_is_null()
    {
        var sut = new TestModelValidator(_faker.Random.Int(1, 1000));
        var model = new TestModel { Elements = null };
        var result = sut.TestValidate(model);
        result.ShouldNotHaveValidationErrorFor(x => x.Elements);
    }

    [Fact]
    public void should_not_have_error_when_elements_count_is_greater_than_or_equal_to_min()
    {
        var min = _faker.Random.Int(1, 1000);
        var sut = new TestModelValidator(min);
        var model = new TestModel { Elements = Enumerable.Range(1, min) };
        var result = sut.TestValidate(model);
        result.ShouldNotHaveValidationErrorFor(x => x.Elements);
    }

    [Fact]
    public void should_have_error_when_elements_count_is_less_than_min()
    {
        var min = _faker.Random.Int(1, 1000);
        var extra = _faker.Random.Int(1, min - 1); // Ensure extra is less than min
        var sut = new TestModelValidator(min);
        var model = new TestModel { Elements = Enumerable.Range(1, min - extra) };
        var result = sut.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.Elements);
    }
}
