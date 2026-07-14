// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using FluentValidation.Results;
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
        validator.RuleFor(x => x.Value).NotEmpty().WithErrorDescriptor(descriptor);

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

    private sealed record TestModel(string Value);
}
