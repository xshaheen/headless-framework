// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Testing.Tests;
using Headless.Messaging.RedisStreams;

namespace Tests;

/// <summary>
/// Unit tests for <see cref="RedisErrorExtensions"/>.
/// </summary>
public sealed class RedisErrorExtensionsTests : TestBase
{
    [Fact]
    public void should_return_group_already_exists_for_busygroup_error()
    {
        // given
        const string error = "BUSYGROUP Consumer Group name already exists";

        // when
        var errorType = error.GetRedisErrorType();

        // then
        errorType.Should().Be(RedisErrorTypes.GroupAlreadyExists);
    }

    [Fact]
    public void should_return_no_group_info_exists_for_no_such_key_error()
    {
        // given
        const string error = "ERR no such key";

        // when
        var errorType = error.GetRedisErrorType();

        // then
        errorType.Should().Be(RedisErrorTypes.NoGroupInfoExists);
    }

    [Fact]
    public void should_return_no_group_info_exists_case_insensitive()
    {
        // given
        const string error = "err NO SUCH KEY";

        // when
        var errorType = error.GetRedisErrorType();

        // then
        errorType.Should().Be(RedisErrorTypes.NoGroupInfoExists);
    }

    [Fact]
    public void should_return_unknown_for_unrecognized_error()
    {
        // given
        const string error = "Some other Redis error";

        // when
        var errorType = error.GetRedisErrorType();

        // then
        errorType.Should().Be(RedisErrorTypes.Unknown);
    }

    [Fact]
    public void should_return_unknown_for_empty_string()
    {
        // given
        var error = string.Empty;

        // when
        var errorType = error.GetRedisErrorType();

        // then
        errorType.Should().Be(RedisErrorTypes.Unknown);
    }

    [Fact]
    public void should_extract_error_type_from_exception_message()
    {
        // given
        var exception = new InvalidOperationException("BUSYGROUP Consumer Group name already exists");

        // when
        var errorType = exception.GetRedisErrorType();

        // then
        errorType.Should().Be(RedisErrorTypes.GroupAlreadyExists);
    }

    [Fact]
    public void should_return_unknown_for_exception_with_different_message()
    {
        // given
        var exception = new InvalidOperationException("Connection timeout");

        // when
        var errorType = exception.GetRedisErrorType();

        // then
        errorType.Should().Be(RedisErrorTypes.Unknown);
    }
}
