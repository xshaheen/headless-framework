// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Tests.Primitives;

public sealed class ApiResultTests
{
    [Fact]
    public void should_create_success_result_with_value()
    {
        // when
        var result = ApiResult<int>.Ok(42);

        // then
        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void should_create_failure_result_with_error()
    {
        // given
        var error = new NotFoundError { Entity = "User", Key = "123" };

        // when
        var result = ApiResult<int>.Fail(error);

        // then
        result.IsSuccess.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    [Fact]
    public void should_throw_when_accessing_value_on_failed_result()
    {
        // given
        var result = ApiResult<int>.Fail(new NotFoundError { Entity = "User", Key = "123" });

        // when
        var action = () => result.Value;

        // then
        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void should_throw_when_accessing_error_on_success_result()
    {
        // given
        var result = ApiResult<int>.Ok(42);

        // when
        var action = () => result.Error;

        // then
        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void should_try_get_value_on_success()
    {
        // given
        var result = ApiResult<int>.Ok(42);

        // when
        var success = result.TryGetValue(out var value);

        // then
        success.Should().BeTrue();
        value.Should().Be(42);
    }

    [Fact]
    public void should_not_try_get_value_on_failure()
    {
        // given
        var result = ApiResult<int>.Fail(new NotFoundError { Entity = "User", Key = "123" });

        // when
        var success = result.TryGetValue(out var value);

        // then
        success.Should().BeFalse();
        value.Should().Be(0);
    }

    [Fact]
    public void should_try_get_error_on_failure()
    {
        // given
        var error = new NotFoundError { Entity = "User", Key = "123" };
        var result = ApiResult<int>.Fail(error);

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
        var result = ApiResult<int>.Ok(42);

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
        var result = default(ApiResult<int>);

        // when
        var failed = result.TryGetError(out var error);

        // then
        result.IsSuccess.Should().BeFalse();
        result.IsFailure.Should().BeFalse();
        failed.Should().BeFalse();
        error.Should().BeNull();
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
        var result = ApiResult<string>.Conflict(errors);

        // then
        result.Error.Should().BeOfType<ConflictError>().Which.Errors.Should().Equal(errors);
    }

    [Fact]
    public void should_create_message_only_conflict_like_exception_constructor()
    {
        // when
        var result = ApiResult<string>.Conflict("Email already exists");

        // then
        result.Error.Should().BeOfType<ConflictError>();
        result.Error.Code.Should().Be(ApiResultErrorCodes.Default);
        result.Error.Message.Should().Be("Email already exists");
    }

    [Fact]
    public void should_create_unauthorized_result_from_error_descriptor()
    {
        // given
        var error = new ErrorDescriptor("auth:expired", "Session expired");

        // when
        var result = ApiResult<string>.Unauthorized(error);

        // then
        result.Error.Should().BeOfType<UnauthorizedError>().Which.Error.Should().BeSameAs(error);
    }

    [Fact]
    public void should_create_not_found_result_from_guid_key()
    {
        // given
        var key = Guid.NewGuid();

        // when
        var result = ApiResult<string>.NotFound("User", key);

        // then
        result.Error.Should().BeOfType<NotFoundError>().Which.Key.Should().Be(key.ToString());
    }

    [Fact]
    public void should_match_success()
    {
        // given
        var result = ApiResult<int>.Ok(42);

        // when
        var value = result.Match(v => $"Success: {v}", e => $"Error: {e.Code}");

        // then
        value.Should().Be("Success: 42");
    }

    [Fact]
    public void should_match_failure()
    {
        // given
        var result = ApiResult<int>.Fail(new NotFoundError { Entity = "User", Key = "123" });

        // when
        var value = result.Match(v => $"Success: {v}", e => $"Error: {e.Code}");

        // then
        value.Should().Be("Error: notfound:user");
    }

    [Fact]
    public void should_map_success_result()
    {
        // given
        var result = ApiResult<int>.Ok(42);

        // when
        var mapped = result.Map(v => v.ToString(CultureInfo.InvariantCulture));

        // then
        mapped.IsSuccess.Should().BeTrue();
        mapped.Value.Should().Be("42");
    }

    [Fact]
    public void should_propagate_error_when_mapping_failed_result()
    {
        // given
        var error = new NotFoundError { Entity = "User", Key = "123" };
        var result = ApiResult<int>.Fail(error);

        // when
        var mapped = result.Map(v => v.ToString(CultureInfo.InvariantCulture));

        // then
        mapped.IsFailure.Should().BeTrue();
        mapped.Error.Should().Be(error);
    }

    [Fact]
    public void should_bind_success_result()
    {
        // given
        var result = ApiResult<int>.Ok(42);

        // when
        var bound = result.Bind(v => ApiResult<string>.Ok(v.ToString(CultureInfo.InvariantCulture)));

        // then
        bound.IsSuccess.Should().BeTrue();
        bound.Value.Should().Be("42");
    }

    [Fact]
    public void should_propagate_error_when_binding_failed_result()
    {
        // given
        var error = new NotFoundError { Entity = "User", Key = "123" };
        var result = ApiResult<int>.Fail(error);

        // when
        var bound = result.Bind(v => ApiResult<string>.Ok(v.ToString(CultureInfo.InvariantCulture)));

        // then
        bound.IsFailure.Should().BeTrue();
        bound.Error.Should().Be(error);
    }

    [Fact]
    public void should_implicitly_convert_value_to_success_result()
    {
        // when
        ApiResult<int> result = 42;

        // then
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void should_implicitly_convert_error_to_failure_result()
    {
        // given
        var error = new NotFoundError { Entity = "User", Key = "123" };

        // when
        ApiResult<int> result = error;

        // then
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    [Fact]
    public void should_execute_on_success_action()
    {
        // given
        var result = ApiResult<int>.Ok(42);
        var executed = false;

        // when
        result.OnSuccess(_ => executed = true);

        // then
        executed.Should().BeTrue();
    }

    [Fact]
    public void should_not_execute_on_success_action_for_failure()
    {
        // given
        var result = ApiResult<int>.Fail(new NotFoundError { Entity = "User", Key = "123" });
        var executed = false;

        // when
        result.OnSuccess(_ => executed = true);

        // then
        executed.Should().BeFalse();
    }

    [Fact]
    public void should_execute_on_failure_action()
    {
        // given
        var result = ApiResult<int>.Fail(new NotFoundError { Entity = "User", Key = "123" });
        var executed = false;

        // when
        result.OnFailure(_ => executed = true);

        // then
        executed.Should().BeTrue();
    }

    [Fact]
    public void should_not_execute_on_failure_action_for_success()
    {
        // given
        var result = ApiResult<int>.Ok(42);
        var executed = false;

        // when
        result.OnFailure(_ => executed = true);

        // then
        executed.Should().BeFalse();
    }

    [Fact]
    public void should_equal_results_with_same_value()
    {
        // given
        var result1 = ApiResult<int>.Ok(42);
        var result2 = ApiResult<int>.Ok(42);

        // then
        result1.Should().Be(result2);
        (result1 == result2).Should().BeTrue();
    }

    [Fact]
    public void should_not_equal_results_with_different_values()
    {
        // given
        var result1 = ApiResult<int>.Ok(42);
        var result2 = ApiResult<int>.Ok(99);

        // then
        result1.Should().NotBe(result2);
        (result1 != result2).Should().BeTrue();
    }

    [Fact]
    public void should_throw_invalid_operation_not_nre_when_accessing_error_on_default_struct()
    {
        // given - a default-initialized struct is neither success nor failure
        var result = default(ApiResult<int>);

        // when
        var action = () => result.Error;

        // then - a clear InvalidOperationException, not a NullReferenceException
        result.IsFailure.Should().BeFalse();
        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void should_throw_invalid_operation_when_matching_failure_on_default_struct()
    {
        // given
        var result = default(ApiResult<int>);

        // when
        var action = () => result.Match(value => value, _ => -1);

        // then
        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void should_throw_invalid_operation_when_on_failure_runs_on_default_struct()
    {
        // given
        var result = default(ApiResult<int>);

        // when
        var action = () => result.OnFailure(_ => { });

        // then
        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void should_throw_invalid_operation_when_getting_fallback_from_default_struct()
    {
        // given
        var result = default(ApiResult<int>);

        // when
        var action = () => result.GetValueOrDefault(42);

        // then
        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void should_return_fallback_from_initialized_failure()
    {
        // given
        var result = ApiResult<int>.Conflict("No value");

        // when
        var value = result.GetValueOrDefault(42);

        // then
        value.Should().Be(42);
    }
}
