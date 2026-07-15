// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Messaging.Redis;
using Headless.Testing.Tests;
using StackExchange.Redis;

namespace Tests;

/// <summary>
/// Unit tests for <see cref="RedisMessagingOptions"/>.
/// </summary>
public sealed class RedisMessagingOptionsTests : TestBase
{
    [Fact]
    public void should_have_null_configuration_by_default()
    {
        // when
        var options = new RedisMessagingOptions();

        // then
        options.Configuration.Should().BeNull();
    }

    [Fact]
    public void should_have_default_stream_entries_count_of_ten()
    {
        // when
        var options = new RedisMessagingOptions();

        // then
        options.StreamEntriesCount.Should().Be(10);
    }

    [Fact]
    public void should_have_default_connection_pool_size_of_ten()
    {
        // when
        var options = new RedisMessagingOptions();

        // then
        options.ConnectionPoolSize.Should().Be(10);
    }

    [Fact]
    public void should_have_null_on_consume_error_callback_by_default()
    {
        // when
        var options = new RedisMessagingOptions();

        // then
        options.OnConsumeError.Should().BeNull();
    }

    [Fact]
    public void should_return_empty_string_when_configuration_is_null()
    {
        // given
        var options = new RedisMessagingOptions { Configuration = null };

        // when - using reflection to access internal property
        var endpoint = _GetEndpoint(options);

        // then
        endpoint.Should().BeEmpty();
    }

    [Fact]
    public void should_return_endpoint_from_configuration()
    {
        // given
        var options = new RedisMessagingOptions
        {
            Configuration = ConfigurationOptions.Parse("redis.example.com:6380"),
        };

        // when
        var endpoint = _GetEndpoint(options);

        // then
        endpoint.Should().Contain("redis.example.com:6380");
    }

    [Fact]
    public void should_expose_only_endpoints_when_configuration_contains_password()
    {
        // given
        var options = new RedisMessagingOptions
        {
            Configuration = ConfigurationOptions.Parse("redis.example.com:6380,password=secret"),
        };

        // when
        var endpoint = _GetEndpoint(options);

        // then
        endpoint.Should().Be("redis.example.com:6380");
    }

    [Fact]
    public void should_allow_setting_stream_entries_count()
    {
        // given
        var options = new RedisMessagingOptions
        {
            // when
            StreamEntriesCount = 100,
        };

        // then
        options.StreamEntriesCount.Should().Be(100);
    }

    [Fact]
    public void should_allow_setting_connection_pool_size()
    {
        // given
        var options = new RedisMessagingOptions
        {
            // when
            ConnectionPoolSize = 20,
        };

        // then
        options.ConnectionPoolSize.Should().Be(20);
    }

    [Fact]
    public void should_allow_setting_on_consume_error_callback()
    {
        // given
        var options = new RedisMessagingOptions();
        Func<RedisMessagingOptions.ConsumeErrorContext, Task> callback = _ => Task.CompletedTask;

        // when
        options.OnConsumeError = callback;

        // then
        options.OnConsumeError.Should().BeSameAs(callback);
    }

    [Fact]
    public void should_contain_exception_and_entry_when_consume_error_context()
    {
        // given
        var exception = new InvalidOperationException("Test error");
        var entry = new StreamEntry("1234567-0", []);

        // when
        var context = new RedisMessagingOptions.ConsumeErrorContext(exception, entry);

        // then
        context.Exception.Should().BeSameAs(exception);
        context.Entry.Should().Be(entry);
    }

    [Fact]
    public void should_allow_null_entry_when_consume_error_context()
    {
        // given
        var exception = new InvalidOperationException("Test error");

        // when
        var context = new RedisMessagingOptions.ConsumeErrorContext(exception, null);

        // then
        context.Exception.Should().BeSameAs(exception);
        context.Entry.Should().BeNull();
    }

    // Validator tests

    [Fact]
    public void should_pass_for_valid_options_when_validator()
    {
        var options = new RedisMessagingOptions { StreamEntriesCount = 5, ConnectionPoolSize = 5 };
        var validator = new RedisMessagingOptionsValidator();

        var result = validator.Validate(options);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void should_fail_for_zero_stream_entries_count_when_validator()
    {
        var options = new RedisMessagingOptions { StreamEntriesCount = 0 };
        var validator = new RedisMessagingOptionsValidator();

        var result = validator.Validate(options);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(RedisMessagingOptions.StreamEntriesCount));
    }

    [Fact]
    public void should_fail_for_zero_connection_pool_size_when_validator()
    {
        var options = new RedisMessagingOptions { ConnectionPoolSize = 0 };
        var validator = new RedisMessagingOptionsValidator();

        var result = validator.Validate(options);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(RedisMessagingOptions.ConnectionPoolSize));
    }

    [Fact]
    public void should_fail_for_negative_stream_entries_count_when_validator()
    {
        var options = new RedisMessagingOptions { StreamEntriesCount = -1 };
        var validator = new RedisMessagingOptionsValidator();

        var result = validator.Validate(options);

        result.IsValid.Should().BeFalse();
    }

    private static string _GetEndpoint(RedisMessagingOptions options)
    {
        // Access internal Endpoint property via reflection (can't use nameof for internal members)
        var property = typeof(RedisMessagingOptions).GetProperty(
            nameof(RedisMessagingOptions.DisplayEndpoint),
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
        );
        return (string)property!.GetValue(options)!;
    }
}
