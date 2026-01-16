// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Primitives;

namespace Tests.Primitives;

public sealed class ApiResultErrorBuilderTests
{
    [Fact]
    public void should_return_success_when_no_errors()
    {
        // given
        var builder = new ApiResultErrorBuilder();

        // when
        var result = builder.ToApiResult(42);

        // then
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void should_return_failure_when_has_errors()
    {
        // given
        var builder = new ApiResultErrorBuilder();
        builder.Add(new NotFoundError { Entity = "User", Key = "1" });

        // when
        var result = builder.ToApiResult(42);

        // then
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<AggregateError>();
        ((AggregateError)result.Error).Errors.Should().ContainSingle();
    }

    [Fact]
    public void should_accumulate_multiple_errors()
    {
        // given
        var builder = new ApiResultErrorBuilder();
        builder.Add(new NotFoundError { Entity = "User", Key = "1" });
        builder.Add(new ConflictError("error", "message"));

        // when
        var result = builder.ToApiResult(42);

        // then
        result.IsFailure.Should().BeTrue();
        var aggregate = result.Error.Should().BeOfType<AggregateError>().Subject;
        aggregate.Errors.Should().HaveCount(2);
    }

    [Fact]
    public void should_return_success_non_generic_when_no_errors()
    {
        // given
        var builder = new ApiResultErrorBuilder();

        // when
        var result = builder.ToApiResult();

        // then
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void should_return_failure_non_generic_when_has_errors()
    {
        // given
        var builder = new ApiResultErrorBuilder();
        builder.Add(new NotFoundError { Entity = "User", Key = "1" });

        // when
        var result = builder.ToApiResult();

        // then
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<AggregateError>();
    }
}
