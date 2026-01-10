// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Primitives;

namespace Tests.Primitives;

public sealed class OpResultNonGenericTests
{
    [Fact]
    public void should_create_success_result()
    {
        // when
        var result = ApiResult.Ok();

        // then
        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
        result.Error.Should().BeNull();
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
}
