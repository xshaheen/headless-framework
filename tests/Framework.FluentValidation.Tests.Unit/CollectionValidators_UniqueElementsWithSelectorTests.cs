// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using FluentValidation;
using FluentValidation.TestHelper;

namespace Framework.FluentValidation.Tests.Unit;

public sealed class CollectionValidators_UniqueElementsWithSelectorTests
{
    private sealed class TestElement(string value)
    {
        public string Value { get; } = value;
    }

    private sealed class TestModel(IEnumerable<TestElement>? elements)
    {
        public IEnumerable<TestElement>? Elements { get; } = elements;
    }

    private sealed class TestModelValidator : AbstractValidator<TestModel>
    {
        public TestModelValidator(Func<TestElement, string> selector, IEqualityComparer<string>? comparer = null)
        {
            RuleFor(x => x.Elements).UniqueElements(selector, comparer);
        }
    }

    [Fact]
    public void should_not_have_error_when_elements_is_null()
    {
        var sut = new TestModelValidator(x => x.Value);
        var model = new TestModel(elements: null);
        var result = sut.TestValidate(model);
        result.ShouldNotHaveValidationErrorFor(x => x.Elements);
    }

    [Fact]
    public void should_not_have_error_when_elements_are_unique()
    {
        var sut = new TestModelValidator(x => x.Value);
        var model = new TestModel([new TestElement("a"), new TestElement("b"), new TestElement("c")]);
        var result = sut.TestValidate(model);
        result.ShouldNotHaveValidationErrorFor(x => x.Elements);
    }

    [Fact]
    public void should_have_error_when_elements_are_not_unique()
    {
        var sut = new TestModelValidator(x => x.Value);
        var model = new TestModel([new TestElement("a"), new TestElement("b"), new TestElement("a")]);
        var result = sut.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.Elements);
    }

    [Fact]
    public void should_not_have_error_when_elements_are_unique_with_custom_comparer()
    {
        var sut = new TestModelValidator(x => x.Value, StringComparer.OrdinalIgnoreCase);
        var model = new TestModel([new TestElement("a"), new TestElement("b"), new TestElement("c")]);
        var result = sut.TestValidate(model);
        result.ShouldNotHaveValidationErrorFor(x => x.Elements);
    }

    [Fact]
    public void should_have_error_when_elements_are_not_unique_with_custom_comparer()
    {
        var sut = new TestModelValidator(x => x.Value, StringComparer.OrdinalIgnoreCase);
        var model = new TestModel([new TestElement("a"), new TestElement("b"), new TestElement("A")]);
        var result = sut.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.Elements);
    }
}
