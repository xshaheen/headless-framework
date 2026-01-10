// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Domain.Results;

namespace Tests.Results;

public sealed class ErrorTests
{
    [Fact]
    public void should_create_error_with_code_and_message()
    {
        var error = new Error("TestCode", "Test message");

        error.Code.Should().Be("TestCode");
        error.Message.Should().Be("Test message");
    }

    [Fact]
    public void should_create_none_error()
    {
        var error = Error.None;

        error.Code.Should().BeEmpty();
        error.Message.Should().BeEmpty();
    }

    [Fact]
    public void should_create_not_found_error()
    {
        var error = Error.NotFound("Item not found");

        error.Code.Should().Be("NotFound");
        error.Message.Should().Be("Item not found");
    }

    [Fact]
    public void should_create_validation_error()
    {
        var error = Error.Validation("Invalid input");

        error.Code.Should().Be("Validation");
        error.Message.Should().Be("Invalid input");
    }

    [Fact]
    public void should_create_conflict_error()
    {
        var error = Error.Conflict("Resource conflict");

        error.Code.Should().Be("Conflict");
        error.Message.Should().Be("Resource conflict");
    }

    [Fact]
    public void should_create_unauthorized_error()
    {
        var error = Error.Unauthorized("Not authenticated");

        error.Code.Should().Be("Unauthorized");
        error.Message.Should().Be("Not authenticated");
    }

    [Fact]
    public void should_create_forbidden_error()
    {
        var error = Error.Forbidden("Access denied");

        error.Code.Should().Be("Forbidden");
        error.Message.Should().Be("Access denied");
    }

    [Fact]
    public void should_be_equal_when_same_code_and_message()
    {
        var error1 = new Error("Code", "Message");
        var error2 = new Error("Code", "Message");

        error1.Should().Be(error2);
        (error1 == error2).Should().BeTrue();
    }

    [Fact]
    public void should_not_be_equal_when_different_code()
    {
        var error1 = new Error("Code1", "Message");
        var error2 = new Error("Code2", "Message");

        error1.Should().NotBe(error2);
        (error1 != error2).Should().BeTrue();
    }

    [Fact]
    public void should_not_be_equal_when_different_message()
    {
        var error1 = new Error("Code", "Message1");
        var error2 = new Error("Code", "Message2");

        error1.Should().NotBe(error2);
    }
}
