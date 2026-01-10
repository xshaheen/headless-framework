// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Primitives;

namespace Tests.Primitives;

public sealed class OpResultTests
{
    [Fact]
    public void should_create_success_result_with_value()
    {
        // when
        var result = OpResult<int>.Ok(42);

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
        var result = OpResult<int>.Fail(error);

        // then
        result.IsSuccess.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    [Fact]
    public void should_throw_when_accessing_value_on_failed_result()
    {
        // given
        var result = OpResult<int>.Fail(new NotFoundError { Entity = "User", Key = "123" });

        // when
        var action = () => result.Value;

        // then
        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void should_throw_when_accessing_error_on_success_result()
    {
        // given
        var result = OpResult<int>.Ok(42);

        // when
        var action = () => result.Error;

        // then
        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void should_try_get_value_on_success()
    {
        // given
        var result = OpResult<int>.Ok(42);

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
        var result = OpResult<int>.Fail(new NotFoundError { Entity = "User", Key = "123" });

        // when
        var success = result.TryGetValue(out var value);

        // then
        success.Should().BeFalse();
        value.Should().Be(default);
    }

    [Fact]
    public void should_try_get_error_on_failure()
    {
        // given
        var error = new NotFoundError { Entity = "User", Key = "123" };
        var result = OpResult<int>.Fail(error);

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
        var result = OpResult<int>.Ok(42);

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
        var result = OpResult<int>.Ok(42);

        // when
        var value = result.Match(v => $"Success: {v}", e => $"Error: {e.Code}");

        // then
        value.Should().Be("Success: 42");
    }

    [Fact]
    public void should_match_failure()
    {
        // given
        var result = OpResult<int>.Fail(new NotFoundError { Entity = "User", Key = "123" });

        // when
        var value = result.Match(v => $"Success: {v}", e => $"Error: {e.Code}");

        // then
        value.Should().Be("Error: notfound:user");
    }

    [Fact]
    public void should_map_success_result()
    {
        // given
        var result = OpResult<int>.Ok(42);

        // when
        var mapped = result.Map(v => v.ToString());

        // then
        mapped.IsSuccess.Should().BeTrue();
        mapped.Value.Should().Be("42");
    }

    [Fact]
    public void should_propagate_error_when_mapping_failed_result()
    {
        // given
        var error = new NotFoundError { Entity = "User", Key = "123" };
        var result = OpResult<int>.Fail(error);

        // when
        var mapped = result.Map(v => v.ToString());

        // then
        mapped.IsFailure.Should().BeTrue();
        mapped.Error.Should().Be(error);
    }

    [Fact]
    public void should_bind_success_result()
    {
        // given
        var result = OpResult<int>.Ok(42);

        // when
        var bound = result.Bind(v => OpResult<string>.Ok(v.ToString()));

        // then
        bound.IsSuccess.Should().BeTrue();
        bound.Value.Should().Be("42");
    }

    [Fact]
    public void should_propagate_error_when_binding_failed_result()
    {
        // given
        var error = new NotFoundError { Entity = "User", Key = "123" };
        var result = OpResult<int>.Fail(error);

        // when
        var bound = result.Bind(v => OpResult<string>.Ok(v.ToString()));

        // then
        bound.IsFailure.Should().BeTrue();
        bound.Error.Should().Be(error);
    }

    [Fact]
    public void should_implicitly_convert_value_to_success_result()
    {
        // when
        OpResult<int> result = 42;

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
        OpResult<int> result = error;

        // then
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    [Fact]
    public void should_execute_on_success_action()
    {
        // given
        var result = OpResult<int>.Ok(42);
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
        var result = OpResult<int>.Fail(new NotFoundError { Entity = "User", Key = "123" });
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
        var result = OpResult<int>.Fail(new NotFoundError { Entity = "User", Key = "123" });
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
        var result = OpResult<int>.Ok(42);
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
        var result1 = OpResult<int>.Ok(42);
        var result2 = OpResult<int>.Ok(42);

        // then
        result1.Should().Be(result2);
        (result1 == result2).Should().BeTrue();
    }

    [Fact]
    public void should_not_equal_results_with_different_values()
    {
        // given
        var result1 = OpResult<int>.Ok(42);
        var result2 = OpResult<int>.Ok(99);

        // then
        result1.Should().NotBe(result2);
        (result1 != result2).Should().BeTrue();
    }
}
