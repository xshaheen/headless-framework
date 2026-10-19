// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using FluentValidation.Results;
using Headless;
using Headless.Primitives;
using FluentSeverity = FluentValidation.Severity;
using HeadlessValidationSeverity = Headless.Primitives.ValidationSeverity;

namespace Tests;

public sealed class FluentValidationExtensionsTests
{
    [Theory]
    [InlineData(HeadlessValidationSeverity.Information, FluentSeverity.Info)]
    [InlineData(HeadlessValidationSeverity.Warning, FluentSeverity.Warning)]
    [InlineData(HeadlessValidationSeverity.Error, FluentSeverity.Error)]
    public void should_apply_error_descriptor_severity_to_validation_failure(
        HeadlessValidationSeverity descriptorSeverity,
        FluentSeverity expectedSeverity
    )
    {
        var validator = new InlineValidator<TestModel>();
        var descriptor = new ErrorDescriptor("custom:failure", "Value is required.", descriptorSeverity);
        validator.RuleFor(x => x.Value).NotEmpty().WithErrorDescriptor(() => descriptor);

        var failure = validator.Validate(new TestModel(Value: "")).Errors.Single();

        failure.Severity.Should().Be(expectedSeverity);
    }

    [Theory]
    [InlineData(FluentSeverity.Info, HeadlessValidationSeverity.Information)]
    [InlineData(FluentSeverity.Warning, HeadlessValidationSeverity.Warning)]
    [InlineData(FluentSeverity.Error, HeadlessValidationSeverity.Error)]
    public void should_preserve_failure_severity_when_converting_to_error_descriptors(
        FluentSeverity failureSeverity,
        HeadlessValidationSeverity expectedSeverity
    )
    {
        var failure = new ValidationFailure(nameof(TestModel.Value), "Value is invalid.")
        {
            ErrorCode = "custom:failure",
            Severity = failureSeverity,
        };

        var descriptor = new[] { failure }.ToErrorDescriptors().Single().Value.Single();

        descriptor.Severity.Should().Be(expectedSeverity);
    }

    [Fact]
    public void should_describe_failure_in_current_culture_when_validator_was_built_in_another()
    {
        var validator = new InlineValidator<TestModel>();

        using (CultureHelper.Use("en", "en"))
        {
            validator.RuleFor(x => x.Value).OnlyIntegers();
        }

        var invalid = new TestModel(Value: "12a");

        using (CultureHelper.Use("ar", "ar"))
        {
            validator.Validate(invalid).Errors.Single().ErrorMessage.Should().Be("يجب أن يحتوي الحقل على أرقام فقط.");
        }

        using (CultureHelper.Use("en", "en"))
        {
            validator
                .Validate(invalid)
                .Errors.Single()
                .ErrorMessage.Should()
                .Be("The field must contain only numbers.");
        }
    }

    private sealed record TestModel(string Value);
}
