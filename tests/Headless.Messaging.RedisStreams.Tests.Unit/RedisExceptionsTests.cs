// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Testing.Tests;
using Headless.Messaging.RedisStreams;
using StackExchange.Redis;

namespace Tests;

/// <summary>
/// Unit tests for Redis-specific exceptions.
/// </summary>
public sealed class RedisExceptionsTests : TestBase
{
    [Fact]
    public void redis_consume_missing_headers_exception_should_include_entry_id()
    {
        // given
        var entry = new StreamEntry("9876543-0", []);

        // when
        var exception = new RedisConsumeMissingHeadersException(entry);

        // then
        exception.Message.Should().Contain("9876543-0");
        exception.Message.Should().Contain("missing message headers");
    }

    [Fact]
    public void redis_consume_missing_body_exception_should_include_entry_id()
    {
        // given
        var entry = new StreamEntry("1111111-0", []);

        // when
        var exception = new RedisConsumeMissingBodyException(entry);

        // then
        exception.Message.Should().Contain("1111111-0");
        exception.Message.Should().Contain("missing message body");
    }

    [Fact]
    public void redis_consume_invalid_headers_exception_should_include_entry_id_and_inner_exception()
    {
        // given
        var entry = new StreamEntry("2222222-0", []);
        var innerException = new FormatException("Invalid JSON");

        // when
        var exception = new RedisConsumeInvalidHeadersException(entry, innerException);

        // then
        exception.Message.Should().Contain("2222222-0");
        exception.Message.Should().Contain("headers");
        exception.Message.Should().Contain("formatted properly");
        exception.InnerException.Should().BeSameAs(innerException);
    }

    [Fact]
    public void redis_consume_invalid_body_exception_should_include_entry_id_and_inner_exception()
    {
        // given
        var entry = new StreamEntry("3333333-0", []);
        var innerException = new FormatException("Invalid base64");

        // when
        var exception = new RedisConsumeInvalidBodyException(entry, innerException);

        // then
        exception.Message.Should().Contain("3333333-0");
        exception.Message.Should().Contain("body");
        exception.Message.Should().Contain("formatted properly");
        exception.InnerException.Should().BeSameAs(innerException);
    }
}
