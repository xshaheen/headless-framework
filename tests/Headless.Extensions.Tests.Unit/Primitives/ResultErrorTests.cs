// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Tests.Primitives;

public sealed class ResultErrorTests
{
    [Fact]
    public void should_create_not_found_error_with_correct_code()
    {
        // when
        var error = new NotFoundError { Entity = "User", Key = "123" };

        // then
        error.Code.Should().Be("notfound:user");
        error.Message.Should().Be("User with key '123' was not found.");
        error.Metadata.Should().ContainKey("entity").WhoseValue.Should().Be("User");
        error.Metadata.Should().ContainKey("key").WhoseValue.Should().Be("123");
    }

    [Fact]
    public void should_create_conflict_error()
    {
        // when
        var error = new ConflictError("duplicate_email", "Email already exists");

        // then
        error.Code.Should().Be("duplicate_email");
        error.Message.Should().Be("Email already exists");
    }

    [Fact]
    public void should_create_validation_error_from_fields()
    {
        // when
        var error = ValidationError.FromFields(
            ("email", "Email is required"),
            ("email", "Email is invalid"),
            ("name", "Name is required")
        );

        // then
        error.Code.Should().Be("validation:failed");
        error.Message.Should().Be("One or more validation errors occurred.");
        error.FieldErrors.Should().HaveCount(2);
        error.FieldErrors["email"].Should().HaveCount(2);
        error.FieldErrors["name"].Should().HaveCount(1);
    }

    [Fact]
    public void should_create_forbidden_error()
    {
        // when
        var error = new ForbiddenError { Reason = "You cannot delete this" };

        // then
        error.Code.Should().Be("forbidden:access_denied");
        error.Message.Should().Be("You cannot delete this");
    }

    [Fact]
    public void should_reuse_unauthorized_error_instance()
    {
        // when
        var error1 = UnauthorizedError.Instance;
        var error2 = UnauthorizedError.Instance;

        // then
        error1.Should().BeSameAs(error2);
        error1.Code.Should().Be("unauthorized");
        error1.Message.Should().Be("Authentication required.");
    }

    [Fact]
    public void should_create_aggregate_error()
    {
        // given
        var errors = new ResultError[]
        {
            new NotFoundError { Entity = "User", Key = "1" },
            new NotFoundError { Entity = "Order", Key = "2" },
        };

        // when
        var error = new AggregateError { Errors = errors };

        // then
        error.Code.Should().Be("aggregate:multiple_errors");
        error.Message.Should().Be("2 errors occurred.");
        error.Errors.Should().HaveCount(2);
    }

    [Fact]
    public void should_create_custom_error()
    {
        // when
        var error = ResultError.Custom("custom:error", "Custom error message");

        // then
        error.Code.Should().Be("custom:error");
        error.Message.Should().Be("Custom error message");
        error.Metadata.Should().BeNull();
    }
}
