// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Primitives;

namespace Tests.Exceptions;

public sealed class ConflictExceptionTests
{
    [Fact]
    public void should_set_properties_correctly_when_constructed_with_single_error_descriptor()
    {
        // given
        var error = new ErrorDescriptor("code1", "description1");

        // when
        var exception = new ConflictException(error);

        // then
        exception.Errors.Should().ContainSingle();
        exception.Errors[0].Description.Should().Be("description1");
        exception.Message.Should().Be("Conflict: code1: description1");
    }

    [Fact]
    public void should_set_properties_correctly_when_constructed_with_multiple_error_descriptors()
    {
        // given
        var errors = new List<ErrorDescriptor> { new("code1", "description1"), new("code2", "description2") };

        // when
        var exception = new ConflictException(errors);

        // then
        exception.Errors.Should().HaveCount(2);
        exception.Errors[0].Description.Should().Be("description1");
        exception.Errors[1].Description.Should().Be("description2");
        exception.Message.Should().Contain("code1: description1");
        exception.Message.Should().Contain("code2: description2");
    }

    [Fact]
    public void should_set_properties_correctly_when_constructed_with_string_error()
    {
        // given
        const string error = "An error occurred";

        // when
        var exception = new ConflictException(error);

        // then
        exception.Errors.Should().ContainSingle();
        exception.Errors[0].Description.Should().Be("An error occurred");
        exception.Message.Should().Be("Conflict: An error occurred");
    }

    [Fact]
    public void should_set_properties_correctly_when_constructed_with_string_error_and_inner_exception()
    {
        // given
        const string error = "An error occurred";
        var innerException = new InvalidOperationException("Inner exception");

        // when
        var exception = new ConflictException(error, innerException);

        // then
        exception.Errors.Should().ContainSingle();
        exception.Errors[0].Description.Should().Be("An error occurred");
        exception.Message.Should().Be("Conflict: An error occurred");
        exception.InnerException.Should().Be(innerException);
    }
}
