// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Tests.Primitives;

public sealed class ErrorDescriptorTests
{
    [Fact]
    public void should_default_to_error_severity_for_every_constructor()
    {
        var descriptor = new ErrorDescriptor("code", "description");
        var descriptorWithParams = new ErrorDescriptor(
            "code",
            "description",
            new Dictionary<string, object?>(StringComparer.Ordinal)
        );

        descriptor.Severity.Should().Be(ValidationSeverity.Error);
        descriptorWithParams.Severity.Should().Be(ValidationSeverity.Error);
    }

    [Fact]
    public void should_accept_a_parameter_tuple_without_chaining()
    {
        var descriptor = new ErrorDescriptor("user:duplicate_email", "Email already exists", ("email", "a@b.com"));

        descriptor
            .Params.Should()
            .ContainSingle()
            .Which.Should()
            .Be(new KeyValuePair<string, object?>("email", "a@b.com"));
        descriptor.Severity.Should().Be(ValidationSeverity.Error);
    }

    [Fact]
    public void should_accept_multiple_parameter_tuples()
    {
        var descriptor = new ErrorDescriptor(
            "user:conflict",
            "User conflicts with existing data",
            ("email", "a@b.com"),
            ("tenantId", 42)
        );

        descriptor
            .Params.Should()
            .BeEquivalentTo(
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["email"] = "a@b.com", ["tenantId"] = 42 }
            );
    }

    [Fact]
    public void should_snapshot_span_parameters_and_overwrite_duplicate_keys_case_insensitively()
    {
        (string Key, object? Value)[] parameters = [("email", "first@b.com"), ("EMAIL", "last@b.com")];

        var descriptor = new ErrorDescriptor("user:duplicate_email", "Email already exists", parameters.AsSpan());
        parameters[1] = ("changed", "changed@b.com");

        descriptor
            .Params.Should()
            .ContainSingle()
            .Which.Should()
            .Be(new KeyValuePair<string, object?>("email", "last@b.com"));
    }

    [Fact]
    public void should_accept_severity_with_tuple_parameters()
    {
        var descriptor = new ErrorDescriptor(
            "user:duplicate_email",
            "Email already exists",
            ValidationSeverity.Warning,
            ("email", "a@b.com")
        );

        descriptor.Severity.Should().Be(ValidationSeverity.Warning);
        descriptor.Params.Should().ContainKey("email").WhoseValue.Should().Be("a@b.com");
    }
}
