// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Messaging.RedisStreams;
using Headless.Testing.Tests;
using StackExchange.Redis;

namespace Tests;

/// <summary>
/// Unit tests for <see cref="MessagingRedisOptions"/>.
/// </summary>
public sealed class MessagingRedisOptionsTests : TestBase
{
    [Fact]
    public void should_have_null_configuration_by_default()
    {
        // when
        var options = new MessagingRedisOptions();

        // then
        options.Configuration.Should().BeNull();
    }

    [Fact]
    public void should_have_zero_stream_entries_count_by_default()
    {
        // when
        var options = new MessagingRedisOptions();

        // then
        options.StreamEntriesCount.Should().Be(0);
    }

    [Fact]
    public void should_have_zero_connection_pool_size_by_default()
    {
        // when
        var options = new MessagingRedisOptions();

        // then
        options.ConnectionPoolSize.Should().Be(0);
    }

    [Fact]
    public void should_have_null_on_consume_error_callback_by_default()
    {
        // when
        var options = new MessagingRedisOptions();

        // then
        options.OnConsumeError.Should().BeNull();
    }

    [Fact]
    public void should_return_empty_string_when_configuration_is_null()
    {
        // given
        var options = new MessagingRedisOptions { Configuration = null };

        // when - using reflection to access internal property
        var endpoint = _GetEndpoint(options);

        // then
        endpoint.Should().BeEmpty();
    }

    [Fact]
    public void should_return_endpoint_from_configuration()
    {
        // given
        var options = new MessagingRedisOptions
        {
            Configuration = ConfigurationOptions.Parse("redis.example.com:6380"),
        };

        // when
        var endpoint = _GetEndpoint(options);

        // then
        endpoint.Should().Contain("redis.example.com:6380");
    }

    [Fact]
    public void should_allow_setting_stream_entries_count()
    {
        // given
        var options = new MessagingRedisOptions();

        // when
        options.StreamEntriesCount = 100;

        // then
        options.StreamEntriesCount.Should().Be(100);
    }

    [Fact]
    public void should_allow_setting_connection_pool_size()
    {
        // given
        var options = new MessagingRedisOptions();

        // when
        options.ConnectionPoolSize = 20;

        // then
        options.ConnectionPoolSize.Should().Be(20);
    }

    [Fact]
    public void should_allow_setting_on_consume_error_callback()
    {
        // given
        var options = new MessagingRedisOptions();
        Func<MessagingRedisOptions.ConsumeErrorContext, Task> callback = _ => Task.CompletedTask;

        // when
        options.OnConsumeError = callback;

        // then
        options.OnConsumeError.Should().BeSameAs(callback);
    }

    [Fact]
    public void consume_error_context_should_contain_exception_and_entry()
    {
        // given
        var exception = new InvalidOperationException("Test error");
        var entry = new StreamEntry("1234567-0", []);

        // when
        var context = new MessagingRedisOptions.ConsumeErrorContext(exception, entry);

        // then
        context.Exception.Should().BeSameAs(exception);
        context.Entry.Should().Be(entry);
    }

    [Fact]
    public void consume_error_context_should_allow_null_entry()
    {
        // given
        var exception = new InvalidOperationException("Test error");

        // when
        var context = new MessagingRedisOptions.ConsumeErrorContext(exception, null);

        // then
        context.Exception.Should().BeSameAs(exception);
        context.Entry.Should().BeNull();
    }

    private static string _GetEndpoint(MessagingRedisOptions options)
    {
        // Access internal Endpoint property via reflection (can't use nameof for internal members)
        var property = typeof(MessagingRedisOptions).GetProperty(
            "Endpoint",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
        );
        return (string)property!.GetValue(options)!;
    }
}
