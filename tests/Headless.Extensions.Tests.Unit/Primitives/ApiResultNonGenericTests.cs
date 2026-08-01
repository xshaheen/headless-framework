// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Tests.Primitives;

public sealed class ApiResultNonGenericTests
{
    [Fact]
    public void should_create_success_result()
    {
        // when
        var result = ApiResult.Ok();

        // then
        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
        result.Invoking(static value => value.Error).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void should_create_failure_result_with_error()
    {
        // given
        var error = new NotFoundError { Entity = "User", Key = "123" };

        // when
        var result = ApiResult.Fail(error);

        // then
        result.IsSuccess.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    [Fact]
    public void should_try_get_error_on_failure()
    {
        // given
        var error = new NotFoundError { Entity = "User", Key = "123" };
        var result = ApiResult.Fail(error);

        // when
        var failed = result.TryGetError(out var returnedError);

        // then
        failed.Should().BeTrue();
        returnedError.Should().Be(error);
    }

    [Fact]
    public void should_not_try_get_error_on_success()
    {
        // given
        var result = ApiResult.Ok();

        // when
        var failed = result.TryGetError(out var error);

        // then
        failed.Should().BeFalse();
        error.Should().BeNull();
    }

    [Fact]
    public void should_not_report_an_error_for_default_result()
    {
        // given
        var result = default(ApiResult);

        // when
        var failed = result.TryGetError(out var error);

        // then
        result.IsSuccess.Should().BeFalse();
        result.IsFailure.Should().BeFalse();
        failed.Should().BeFalse();
        error.Should().BeNull();
    }

    [Fact]
    public void should_reject_null_failure_error()
    {
        // when
        var action = () => ApiResult.Fail(null!);

        // then
        action.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void should_create_conflict_result_from_multiple_descriptors()
    {
        // given
        var errors = new[]
        {
            new ErrorDescriptor("account:duplicate_email", "Email already exists"),
            new ErrorDescriptor("account:duplicate_phone", "Phone already exists"),
        };

        // when
        var result = ApiResult.Conflict(errors);

        // then
        result.Error.Should().BeOfType<ConflictError>().Which.Errors.Should().Equal(errors);
    }

    [Fact]
    public void should_create_structured_validation_result()
    {
        // given
        var descriptor = new ErrorDescriptor("g:must_be_not_empty", "Email is required");
        var errors = new Dictionary<string, IReadOnlyList<ErrorDescriptor>>(StringComparer.Ordinal)
        {
            ["email"] = [descriptor],
        };

        // when
        var result = ApiResult.ValidationFailed(errors);

        // then
        result
            .Error.Should()
            .BeOfType<ValidationError>()
            .Which.Errors["email"]
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeSameAs(descriptor);
    }

    [Fact]
    public void should_create_unauthorized_result_from_message_like_exception_constructor()
    {
        // when
        var result = ApiResult.Unauthorized("Session expired", "auth:expired");

        // then
        result
            .Error.Should()
            .BeOfType<UnauthorizedError>()
            .Which.Error.Should()
            .BeEquivalentTo(new ErrorDescriptor("auth:expired", "Session expired"));
    }

    [Fact]
    public void should_match_success()
    {
        // given
        var result = ApiResult.Ok();

        // when
        var value = result.Match(() => "Success", e => $"Error: {e.Code}");

        // then
        value.Should().Be("Success");
    }

    [Fact]
    public void should_match_failure()
    {
        // given
        var result = ApiResult.Fail(new NotFoundError { Entity = "User", Key = "123" });

        // when
        var value = result.Match(() => "Success", e => $"Error: {e.Code}");

        // then
        value.Should().Be("Error: notfound:user");
    }

    [Fact]
    public void should_implicitly_convert_error_to_failure_result()
    {
        // given
        var error = new NotFoundError { Entity = "User", Key = "123" };

        // when
        ApiResult result = error;

        // then
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    [Fact]
    public void should_create_not_found_result()
    {
        // when
        var result = ApiResult.NotFound("User", "123");

        // then
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<NotFoundError>();
        ((NotFoundError)result.Error).Entity.Should().Be("User");
        ((NotFoundError)result.Error).Key.Should().Be("123");
    }

    [Fact]
    public void should_create_conflict_result()
    {
        // when
        var result = ApiResult.Conflict("duplicate_email", "Email already exists");

        // then
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ConflictError>();
        result.Error.Code.Should().Be("duplicate_email");
        result.Error.Message.Should().Be("Email already exists");
    }

    [Fact]
    public void should_create_message_only_conflict_like_exception_constructor()
    {
        // when
        var result = ApiResult.Conflict("Email already exists");

        // then
        result.Error.Should().BeOfType<ConflictError>();
        result.Error.Code.Should().Be(ApiResultErrorCodes.Default);
        result.Error.Message.Should().Be("Email already exists");
    }

    [Fact]
    public void should_create_forbidden_result()
    {
        // when
        var result = ApiResult.Forbidden("Access denied");

        // then
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ForbiddenError>();
        result.Error!.Message.Should().Be("Access denied");
    }

    [Fact]
    public void should_create_unauthorized_result()
    {
        // when
        var result = ApiResult.Unauthorized();

        // then
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(UnauthorizedError.Instance);
    }

    [Fact]
    public void should_equal_success_results()
    {
        // given
        var result1 = ApiResult.Ok();
        var result2 = ApiResult.Ok();

        // then
        result1.Should().Be(result2);
        (result1 == result2).Should().BeTrue();
    }

    [Fact]
    public void should_not_equal_success_and_failure_results()
    {
        // given
        var result1 = ApiResult.Ok();
        var result2 = ApiResult.Fail(new NotFoundError { Entity = "User", Key = "123" });

        // then
        result1.Should().NotBe(result2);
        (result1 != result2).Should().BeTrue();
    }

    [Fact]
    public void should_throw_invalid_operation_when_matching_failure_on_default_struct()
    {
        // given - a default-initialized struct is neither success nor failure
        var result = default(ApiResult);

        // when
        var action = () => result.Match(() => "ok", _ => "err");

        // then - a clear InvalidOperationException, not a NullReferenceException
        result.IsFailure.Should().BeFalse();
        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void should_throw_invalid_operation_when_on_failure_runs_on_default_struct()
    {
        // given
        var result = default(ApiResult);

        // when
        var action = () => result.OnFailure(_ => { });

        // then
        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void should_stay_equal_after_reading_error_members_on_carried_not_found_error()
    {
        // given - two failures carrying logically-equal NotFoundErrors
        var result1 = ApiResult.NotFound("User", "123");
        var result2 = ApiResult.NotFound("User", "123");

        // when - reading the error's computed members on one (a field-backed cache here would poison ApiResult equality)
        _ = ((NotFoundError)result1.Error!).Code;
        _ = ((NotFoundError)result1.Error!).Metadata;

        // then
        result1.Should().Be(result2);
        (result1 == result2).Should().BeTrue();
    }
}
