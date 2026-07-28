// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

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
        result.Error.Should().BeOfType<NotFoundError>();
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
        result.Error.Should().BeOfType<NotFoundError>();
    }

    [Fact]
    public void should_snapshot_errors_when_materializing_result()
    {
        // given
        var builder = new ApiResultErrorBuilder();
        builder.Add(new NotFoundError { Entity = "User", Key = "1" });
        builder.Add(new NotFoundError { Entity = "Order", Key = "2" });
        var result = builder.ToApiResult();

        // when
        builder.Add(new ConflictError("user:duplicate", "Duplicate user"));

        // then
        result.Error.Should().BeOfType<AggregateError>().Which.Errors.Should().HaveCount(2);
    }

    [Fact]
    public void should_reject_null_error_when_adding()
    {
        // when
        var action = static () =>
        {
            var builder = new ApiResultErrorBuilder();
            builder.Add(null!);
        };

        // then
        action.Should().Throw<ArgumentNullException>();
    }
}
