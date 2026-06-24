// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Tests.Primitives;

public sealed class ResultTests
{
    [Fact]
    public void result_error_should_throw_invalid_operation_not_nre_on_default_struct()
    {
        // given - a default(Result<TError>) is a failure state carrying no error
        var result = default(Result<string>);

        // when
        var action = () => result.Error;

        // then - a clear InvalidOperationException, not a NullReferenceException
        result.IsFailure.Should().BeTrue();
        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void result_error_match_failure_should_throw_invalid_operation_on_default_struct()
    {
        // given
        var result = default(Result<string>);

        // when
        var action = () => result.Match(() => 1, _ => -1);

        // then
        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void result_error_on_failure_should_throw_invalid_operation_on_default_struct()
    {
        // given
        var result = default(Result<string>);

        // when
        var action = () => result.OnFailure(_ => { });

        // then
        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void result_value_error_should_throw_invalid_operation_not_nre_on_default_struct()
    {
        // given - a default(Result<TValue, TError>) is a failure state carrying no error
        var result = default(Result<int, string>);

        // when
        var action = () => result.Error;

        // then - a clear InvalidOperationException, not a NullReferenceException
        result.IsFailure.Should().BeTrue();
        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void result_value_error_match_failure_should_throw_invalid_operation_on_default_struct()
    {
        // given
        var result = default(Result<int, string>);

        // when
        var action = () => result.Match(value => value, _ => -1);

        // then
        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void result_value_error_on_failure_should_throw_invalid_operation_on_default_struct()
    {
        // given
        var result = default(Result<int, string>);

        // when
        var action = () => result.OnFailure(_ => { });

        // then
        action.Should().Throw<InvalidOperationException>();
    }
}
