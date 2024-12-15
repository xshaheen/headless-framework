// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using FluentValidation.TestHelper;

namespace Framework.FluentValidation.Tests.Unit;

public sealed class CollectionValidators_MaximumElementsTests
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
            RuleFor(x => x.Elements).MaximumElements(value);
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
    public void should_not_have_error_when_elements_count_is_less_than_or_equal_to_max()
    {
        var max = _faker.Random.Int(1, 1000);
        var sut = new TestModelValidator(max);
        var model = new TestModel { Elements = Enumerable.Range(1, max) };
        var result = sut.TestValidate(model);
        result.ShouldNotHaveValidationErrorFor(x => x.Elements);
    }

    [Fact]
    public void should_have_error_when_elements_count_is_greater_than_max()
    {
        var max = _faker.Random.Int(1, 1000);
        var extra = _faker.Random.Int(1, 1000);
        var sut = new TestModelValidator(max);
        var model = new TestModel { Elements = Enumerable.Range(1, max + extra) };
        var result = sut.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.Elements);
    }
}
