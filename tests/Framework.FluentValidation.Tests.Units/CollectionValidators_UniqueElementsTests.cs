// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using FluentValidation.TestHelper;

namespace Framework.FluentValidation.Tests.Unit;

public sealed class CollectionValidators_UniqueElementsTests
{
    private sealed class TestModel(IEnumerable<string>? elements)
    {
        public IEnumerable<string>? Elements { get; init; } = elements;
    }

    private sealed class TestModelValidator : AbstractValidator<TestModel>
    {
        public TestModelValidator(IEqualityComparer<string>? comparer = null)
        {
            RuleFor(x => x.Elements).UniqueElements(comparer);
        }
    }

    [Fact]
    public void should_not_have_error_when_elements_is_null()
    {
        var sut = new TestModelValidator();
        var model = new TestModel(elements: null);
        var result = sut.TestValidate(model);
        result.ShouldNotHaveValidationErrorFor(x => x.Elements);
    }

    [Fact]
    public void should_not_have_error_when_elements_are_unique()
    {
        var sut = new TestModelValidator();
        var model = new TestModel(["a", "b", "c"]);
        var result = sut.TestValidate(model);
        result.ShouldNotHaveValidationErrorFor(x => x.Elements);
    }

    [Fact]
    public void should_have_error_when_elements_are_not_unique()
    {
        var sut = new TestModelValidator();
        var model = new TestModel(["a", "b", "a"]);
        var result = sut.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.Elements);
    }

    [Fact]
    public void should_not_have_error_when_elements_are_unique_with_custom_comparer()
    {
        var sut = new TestModelValidator(StringComparer.OrdinalIgnoreCase);
        var model = new TestModel(["a", "b", "c"]);
        var result = sut.TestValidate(model);
        result.ShouldNotHaveValidationErrorFor(x => x.Elements);
    }

    [Fact]
    public void should_have_error_when_elements_are_not_unique_with_custom_comparer()
    {
        var sut = new TestModelValidator(StringComparer.OrdinalIgnoreCase);
        var model = new TestModel(["a", "b", "A"]);
        var result = sut.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.Elements);
    }
}
