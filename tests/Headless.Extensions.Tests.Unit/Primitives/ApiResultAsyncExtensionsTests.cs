// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;
using Headless.Testing.Tests;

namespace Tests.Primitives;

public sealed class ApiResultAsyncExtensionsTests : TestBase
{
    private static readonly ApiResultError _Error = ApiResultError.Custom("test:failure", "It failed.");

    #region MapAsync

    [Fact]
    public async Task should_map_success_value_when_map_async()
    {
        // given
        var result = ApiResult<int>.Ok(21);

        // when
        var mapped = await result.MapAsync(x => Task.FromResult(x * 2));

        // then
        mapped.IsSuccess.Should().BeTrue();
        mapped.Value.Should().Be(42);
    }

    [Fact]
    public async Task should_propagate_failure_when_map_async()
    {
        // given
        var result = ApiResult<int>.Fail(_Error);

        // when
        var mapped = await result.MapAsync(x => Task.FromResult(x * 2));

        // then
        mapped.IsFailure.Should().BeTrue();
        mapped.Error.Should().Be(_Error);
    }

    [Fact]
    public async Task should_flow_cancellation_token_into_mapper_when_map_async()
    {
        // given
        var result = ApiResult<int>.Ok(21);
        var observedToken = CancellationToken.None;

        // when
        var mapped = await result.MapAsync(
            (x, ct) =>
            {
                observedToken = ct;
                return Task.FromResult(x * 2);
            },
            AbortToken
        );

        // then
        mapped.Value.Should().Be(42);
        observedToken.Should().Be(AbortToken);
    }

    [Fact]
    public async Task should_flow_cancellation_token_into_mapper_when_map_async_on_task()
    {
        // given
        var resultTask = Task.FromResult(ApiResult<int>.Ok(21));
        var observedToken = CancellationToken.None;

        // when
        var mapped = await resultTask.MapAsync(
            (x, ct) =>
            {
                observedToken = ct;
                return Task.FromResult(x * 2);
            },
            AbortToken
        );

        // then
        mapped.Value.Should().Be(42);
        observedToken.Should().Be(AbortToken);
    }

    #endregion

    #region BindAsync

    [Fact]
    public async Task should_bind_success_value_when_bind_async()
    {
        // given
        var result = ApiResult<int>.Ok(21);

        // when
        var bound = await result.BindAsync(x => Task.FromResult(ApiResult<string>.Ok($"value-{x}")));

        // then
        bound.IsSuccess.Should().BeTrue();
        bound.Value.Should().Be("value-21");
    }

    [Fact]
    public async Task should_propagate_failure_when_bind_async()
    {
        // given
        var result = ApiResult<int>.Fail(_Error);

        // when
        var bound = await result.BindAsync(x => Task.FromResult(ApiResult<string>.Ok($"value-{x}")));

        // then
        bound.IsFailure.Should().BeTrue();
        bound.Error.Should().Be(_Error);
    }

    [Fact]
    public async Task should_flow_cancellation_token_into_binder_when_bind_async()
    {
        // given
        var result = ApiResult<int>.Ok(21);
        var observedToken = CancellationToken.None;

        // when
        var bound = await result.BindAsync(
            (x, ct) =>
            {
                observedToken = ct;
                return Task.FromResult(ApiResult<string>.Ok($"value-{x}"));
            },
            AbortToken
        );

        // then
        bound.Value.Should().Be("value-21");
        observedToken.Should().Be(AbortToken);
    }

    [Fact]
    public async Task should_not_invoke_binder_on_failure_when_bind_async_on_task()
    {
        // given
        var resultTask = Task.FromResult(ApiResult<int>.Fail(_Error));
        var binderInvoked = false;

        // when
        var bound = await resultTask.BindAsync(
            (x, _) =>
            {
                binderInvoked = true;
                return Task.FromResult(ApiResult<string>.Ok($"value-{x}"));
            },
            AbortToken
        );

        // then - the binder is not invoked on failure and the error propagates
        bound.IsFailure.Should().BeTrue();
        bound.Error.Should().Be(_Error);
        binderInvoked.Should().BeFalse();
    }

    #endregion

    #region MatchAsync

    [Fact]
    public async Task should_invoke_success_branch_when_match_async()
    {
        // given
        var resultTask = Task.FromResult(ApiResult<int>.Ok(42));

        // when
        var output = await resultTask.MatchAsync(value => $"ok-{value}", error => $"fail-{error.Code}");

        // then
        output.Should().Be("ok-42");
    }

    [Fact]
    public async Task should_invoke_failure_branch_when_match_async()
    {
        // given
        var resultTask = Task.FromResult(ApiResult<int>.Fail(_Error));

        // when
        var output = await resultTask.MatchAsync(value => $"ok-{value}", error => $"fail-{error.Code}");

        // then
        output.Should().Be("fail-test:failure");
    }

    [Fact]
    public async Task should_flow_cancellation_token_into_branches_when_match_async()
    {
        // given
        var successTask = Task.FromResult(ApiResult<int>.Ok(42));
        var failureTask = Task.FromResult(ApiResult<int>.Fail(_Error));
        var successToken = CancellationToken.None;
        var failureToken = CancellationToken.None;

        // when
        var successOutput = await successTask.MatchAsync(
            (value, ct) =>
            {
                successToken = ct;
                return $"ok-{value}";
            },
            (error, _) => $"fail-{error.Code}",
            AbortToken
        );

        var failureOutput = await failureTask.MatchAsync(
            (value, _) => $"ok-{value}",
            (error, ct) =>
            {
                failureToken = ct;
                return $"fail-{error.Code}";
            },
            AbortToken
        );

        // then
        successOutput.Should().Be("ok-42");
        failureOutput.Should().Be("fail-test:failure");
        successToken.Should().Be(AbortToken);
        failureToken.Should().Be(AbortToken);
    }

    #endregion
}
