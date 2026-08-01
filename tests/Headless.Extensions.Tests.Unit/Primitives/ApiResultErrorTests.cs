// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Tests.Primitives;

public sealed class ApiResultErrorTests
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
    public void should_preserve_all_error_descriptors_when_creating_conflict_error()
    {
        // given
        var errors = new[]
        {
            new ErrorDescriptor("user:duplicate_email", "Email already exists").WithParam("email", "a@b.com"),
            new ErrorDescriptor("user:duplicate_phone", "Phone already exists"),
        };

        // when
        var error = new ConflictError(errors);

        // then
        error.Errors.Should().Equal(errors);
        error.Errors[0].Params.Should().ContainKey("email").WhoseValue.Should().Be("a@b.com");
    }

    [Fact]
    public void should_compare_descriptor_backed_errors_by_code_and_message()
    {
        // given
        var conflictA = new ConflictError("user:duplicate", "User already exists");
        var conflictB = new ConflictError("user:duplicate", "User already exists");
        var forbiddenA = new ForbiddenError(new ErrorDescriptor("permission:missing", "Permission is required"));
        var forbiddenB = new ForbiddenError(new ErrorDescriptor("permission:missing", "Permission is required"));
        var unauthorizedA = new UnauthorizedError(new ErrorDescriptor("auth:expired", "Session expired"));
        var unauthorizedB = new UnauthorizedError(new ErrorDescriptor("auth:expired", "Session expired"));

        // then
        conflictA.Should().Be(conflictB);
        conflictA.GetHashCode().Should().Be(conflictB.GetHashCode());
        forbiddenA.Should().Be(forbiddenB);
        forbiddenA.GetHashCode().Should().Be(forbiddenB.GetHashCode());
        unauthorizedA.Should().Be(unauthorizedB);
        unauthorizedA.GetHashCode().Should().Be(unauthorizedB.GetHashCode());
    }

    [Fact]
    public void should_distinguish_generic_unauthorized_error_from_structured_descriptor()
    {
        // given
        var generic = UnauthorizedError.Instance;
        var structured = new UnauthorizedError(new ErrorDescriptor("unauthorized", "Authentication required."));

        // then
        generic.Should().NotBe(structured);
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
        error.FieldErrors["name"].Should().ContainSingle();
    }

    [Fact]
    public void should_create_validation_error_with_stable_general_codes_from_message_pairs()
    {
        // when
        var error = ValidationError.FromFields(("Contact.Email", "Email is required"));

        // then
        error.Errors["Contact.Email"].Should().ContainSingle();
        error.Errors["Contact.Email"][0].Code.Should().Be(ApiResultErrorCodes.ValidationFailed);
    }

    [Fact]
    public void should_preserve_structured_validation_descriptors()
    {
        // given
        var descriptor = new ErrorDescriptor("g:must_be_not_empty", "Email is required").WithParam(
            "propertyPath",
            "email"
        );

        // when
        var error = ValidationError.FromErrorDescriptors(
            new Dictionary<string, IReadOnlyList<ErrorDescriptor>>(StringComparer.Ordinal) { ["email"] = [descriptor] }
        );

        // then
        error.Errors["email"].Should().ContainSingle().Which.Should().BeSameAs(descriptor);
        error.Errors["email"][0].Params.Should().ContainKey("propertyPath");
    }

    [Fact]
    public void should_create_forbidden_error()
    {
        // when
        var error = new ForbiddenError(new ErrorDescriptor(ApiResultErrorCodes.Forbidden, "You cannot delete this"));

        // then
        error.Code.Should().Be(ApiResultErrorCodes.Forbidden);
        error.Message.Should().Be("You cannot delete this");
    }

    [Fact]
    public void should_preserve_structured_forbidden_and_unauthorized_descriptors()
    {
        // given
        var forbidden = new ErrorDescriptor("permission:missing", "Permission is required");
        var unauthorized = new ErrorDescriptor("auth:expired", "Session expired");

        // when
        var forbiddenError = new ForbiddenError(forbidden);
        var unauthorizedError = new UnauthorizedError(unauthorized);

        // then
        forbiddenError.Error.Should().BeSameAs(forbidden);
        forbiddenError.Code.Should().Be("permission:missing");
        unauthorizedError.Error.Should().BeSameAs(unauthorized);
        unauthorizedError.Code.Should().Be("auth:expired");
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
        var errors = new ApiResultError[]
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
    public void should_compare_independent_aggregates_by_ordered_errors()
    {
        // given
        var a = new AggregateError
        {
            Errors = [new ConflictError("first", "First"), new ConflictError("second", "Second")],
        };
        var b = new AggregateError
        {
            Errors = [new ConflictError("first", "First"), new ConflictError("second", "Second")],
        };

        // then
        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void should_snapshot_conflict_aggregate_and_validation_collections()
    {
        // given
        var first = new ErrorDescriptor("first", "First");
        var conflictInput = new[] { first };
        var aggregateInput = new ApiResultError[] { new ConflictError(first) };
        var validationInput = new[] { first };
        var validationMap = new Dictionary<string, IReadOnlyList<ErrorDescriptor>>(StringComparer.Ordinal)
        {
            ["email"] = validationInput,
        };
        var conflict = new ConflictError(conflictInput);
        var aggregate = new AggregateError { Errors = aggregateInput };
        var validation = ValidationError.FromErrorDescriptors(validationMap);

        // when
        conflictInput[0] = new ErrorDescriptor("changed", "Changed");
        aggregateInput[0] = new ConflictError("changed", "Changed");
        validationInput[0] = new ErrorDescriptor("changed", "Changed");
        validationMap["email"] = [new ErrorDescriptor("replaced", "Replaced")];

        // then
        conflict.Errors.Should().ContainSingle().Which.Should().BeSameAs(first);
        aggregate.Errors.Should().ContainSingle().Which.Code.Should().Be("first");
        validation.Errors["email"].Should().ContainSingle().Which.Should().BeSameAs(first);
    }

    [Fact]
    public void should_create_custom_error()
    {
        // when
        var error = ApiResultError.Custom("custom:error", "Custom error message");

        // then
        error.Code.Should().Be("custom:error");
        error.Message.Should().Be("Custom error message");
        error.Metadata.Should().BeNull();
    }

    [Fact]
    public void should_stay_equal_after_reading_code_and_metadata_when_not_found_errors()
    {
        // given - two logically-equal errors
        var a = new NotFoundError { Entity = "User", Key = "123" };
        var b = new NotFoundError { Entity = "User", Key = "123" };

        // when - reading the computed members on one (a `field`-backed cache here would poison record equality)
        _ = a.Code;
        _ = a.Metadata;

        // then - record equality and hashing remain stable
        a.Should().Be(b);
        (a == b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void should_stay_equal_after_reading_metadata_when_validation_errors()
    {
        // given - two errors sharing the same FieldErrors instance, so they are logically equal
        var fieldErrors = new Dictionary<string, IReadOnlyList<ErrorDescriptor>>(StringComparer.Ordinal)
        {
            ["email"] = [new ErrorDescriptor(ApiResultErrorCodes.ValidationFailed, "Email is required")],
        };
        var a = new ValidationError { Errors = fieldErrors };
        var b = a with { };

        // when - reading Metadata on one (a `field`-backed cache here would poison record equality)
        _ = a.Metadata;

        // then - record equality and hashing remain stable
        a.Should().Be(b);
        (a == b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void should_compare_separately_created_validation_errors_by_field_code_and_message()
    {
        // given
        var a = ValidationError.FromErrorDescriptors(
            new Dictionary<string, IReadOnlyList<ErrorDescriptor>>(StringComparer.Ordinal)
            {
                ["email"] = [new ErrorDescriptor("g:required", "Email is required")],
            }
        );
        var b = ValidationError.FromErrorDescriptors(
            new Dictionary<string, IReadOnlyList<ErrorDescriptor>>(StringComparer.Ordinal)
            {
                ["email"] = [new ErrorDescriptor("g:required", "Email is required")],
            }
        );

        // then
        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }
}
