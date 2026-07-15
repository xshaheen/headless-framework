// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Redis;
using Headless.Testing.Tests;

namespace Tests;

/// <summary>
/// Unit tests for Redis-specific exceptions.
/// </summary>
public sealed class RedisExceptionsTests : TestBase
{
    [Fact]
    public void should_include_entry_id_when_redis_consume_missing_headers_exception()
    {
        // given
        const string entryId = "9876543-0";

        // when
        var exception = new RedisConsumeMissingHeadersException(entryId);

        // then
        exception.Message.Should().Contain("9876543-0");
        exception.Message.Should().Contain("missing message headers");
    }

    [Fact]
    public void should_include_entry_id_when_redis_consume_missing_body_exception()
    {
        // given
        const string entryId = "1111111-0";

        // when
        var exception = new RedisConsumeMissingBodyException(entryId);

        // then
        exception.Message.Should().Contain("1111111-0");
        exception.Message.Should().Contain("missing message body");
    }

    [Fact]
    public void should_include_entry_id_and_inner_exception_when_redis_consume_invalid_headers_exception()
    {
        // given
        const string entryId = "2222222-0";
        var innerException = new FormatException("Invalid JSON");

        // when
        var exception = new RedisConsumeInvalidHeadersException(entryId, innerException);

        // then
        exception.Message.Should().Contain("2222222-0");
        exception.Message.Should().Contain("headers");
        exception.Message.Should().Contain("formatted properly");
        exception.InnerException.Should().BeSameAs(innerException);
    }

    [Fact]
    public void should_include_entry_id_and_inner_exception_when_redis_consume_invalid_body_exception()
    {
        // given
        const string entryId = "3333333-0";
        var innerException = new FormatException("Invalid base64");

        // when
        var exception = new RedisConsumeInvalidBodyException(entryId, innerException);

        // then
        exception.Message.Should().Contain("3333333-0");
        exception.Message.Should().Contain("body");
        exception.Message.Should().Contain("formatted properly");
        exception.InnerException.Should().BeSameAs(innerException);
    }
}
