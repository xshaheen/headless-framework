// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Domain.Results;

namespace Tests.Results;

public sealed class ResultTests
{
    [Fact]
    public void should_create_success_result_with_value()
    {
        var result = Result<int>.Success(42);

        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void should_create_failure_result_with_error()
    {
        var error = Error.NotFound("Item not found");
        var result = Result<int>.Failure(error);

        result.IsSuccess.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    [Fact]
    public void should_throw_when_accessing_value_on_failure()
    {
        var result = Result<int>.Failure(Error.NotFound("Not found"));

        var act = () => _ = result.Value;

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void should_throw_when_accessing_error_on_success()
    {
        var result = Result<int>.Success(42);

        var act = () => _ = result.Error;

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void should_implicitly_convert_value_to_success_result()
    {
        Result<string> result = "test";

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("test");
    }

    [Fact]
    public void should_implicitly_convert_error_to_failure_result()
    {
        var error = Error.Validation("Invalid input");
        Result<string> result = error;

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    [Fact]
    public void should_match_success_result()
    {
        var result = Result<int>.Success(42);

        var output = result.Match(
            onSuccess: v => $"Value: {v}",
            onFailure: e => $"Error: {e.Message}"
        );

        output.Should().Be("Value: 42");
    }

    [Fact]
    public void should_match_failure_result()
    {
        var result = Result<int>.Failure(Error.NotFound("Item not found"));

        var output = result.Match(
            onSuccess: v => $"Value: {v}",
            onFailure: e => $"Error: {e.Message}"
        );

        output.Should().Be("Error: Item not found");
    }

    [Fact]
    public void should_map_success_result()
    {
        var result = Result<int>.Success(42);

        var mapped = result.Map(v => v.ToString());

        mapped.IsSuccess.Should().BeTrue();
        mapped.Value.Should().Be("42");
    }

    [Fact]
    public void should_not_map_failure_result()
    {
        var error = Error.NotFound("Not found");
        var result = Result<int>.Failure(error);

        var mapped = result.Map(v => v.ToString());

        mapped.IsFailure.Should().BeTrue();
        mapped.Error.Should().Be(error);
    }

    [Fact]
    public async Task should_match_success_result_async()
    {
        var result = Result<int>.Success(42);

        var output = await result.MatchAsync(
            onSuccess: v => Task.FromResult($"Value: {v}"),
            onFailure: e => Task.FromResult($"Error: {e.Message}")
        );

        output.Should().Be("Value: 42");
    }

    [Fact]
    public async Task should_match_failure_result_async()
    {
        var result = Result<int>.Failure(Error.NotFound("Item not found"));

        var output = await result.MatchAsync(
            onSuccess: v => Task.FromResult($"Value: {v}"),
            onFailure: e => Task.FromResult($"Error: {e.Message}")
        );

        output.Should().Be("Error: Item not found");
    }

    [Fact]
    public void should_be_equal_when_same_success_value()
    {
        var result1 = Result<int>.Success(42);
        var result2 = Result<int>.Success(42);

        result1.Equals(result2).Should().BeTrue();
        (result1 == result2).Should().BeTrue();
        (result1 != result2).Should().BeFalse();
    }

    [Fact]
    public void should_not_be_equal_when_different_values()
    {
        var result1 = Result<int>.Success(42);
        var result2 = Result<int>.Success(43);

        result1.Equals(result2).Should().BeFalse();
        (result1 == result2).Should().BeFalse();
        (result1 != result2).Should().BeTrue();
    }

    [Fact]
    public void should_be_equal_when_same_error()
    {
        var error = Error.NotFound("Not found");
        var result1 = Result<int>.Failure(error);
        var result2 = Result<int>.Failure(error);

        result1.Equals(result2).Should().BeTrue();
        (result1 == result2).Should().BeTrue();
    }

    [Fact]
    public void should_not_be_equal_success_and_failure()
    {
        var result1 = Result<int>.Success(42);
        var result2 = Result<int>.Failure(Error.NotFound("Not found"));

        result1.Equals(result2).Should().BeFalse();
        (result1 == result2).Should().BeFalse();
    }

    [Fact]
    public void should_have_consistent_hash_code_for_equal_results()
    {
        var result1 = Result<int>.Success(42);
        var result2 = Result<int>.Success(42);

        result1.GetHashCode().Should().Be(result2.GetHashCode());
    }

    [Fact]
    public void should_use_from_value_factory_method()
    {
        var result = Result<string>.FromValue("test");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("test");
    }

    [Fact]
    public void should_use_from_error_factory_method()
    {
        var error = Error.Conflict("Conflict occurred");
        var result = Result<string>.FromError(error);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }
}
