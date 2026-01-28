// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;
using Headless.Testing.Tests;

namespace Tests;

public sealed class MessageHeaderTests : TestBase
{
    [Fact]
    public void should_create_from_dictionary()
    {
        // given
        var dictionary = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["key1"] = "value1",
            ["key2"] = "value2",
        };

        // when
        var header = new MessageHeader(dictionary);

        // then
        header.Count.Should().Be(2);
        header["key1"].Should().Be("value1");
        header["key2"].Should().Be("value2");
    }

    [Fact]
    public void should_be_readonly()
    {
        // given
        var dictionary = new Dictionary<string, string?>(StringComparer.Ordinal) { ["key1"] = "value1" };
        var header = new MessageHeader(dictionary);

        // when/then - MessageHeader inherits from ReadOnlyDictionary so it doesn't have Add method
        header.Should().BeAssignableTo<IReadOnlyDictionary<string, string?>>();
    }

    [Fact]
    public void should_support_containskey_operation()
    {
        // given
        var dictionary = new Dictionary<string, string?>(StringComparer.Ordinal) { ["existing-key"] = "value" };
        var header = new MessageHeader(dictionary);

        // when/then
        header.ContainsKey("existing-key").Should().BeTrue();
        header.ContainsKey("non-existing-key").Should().BeFalse();
    }

    [Fact]
    public void should_support_trygetvalue_operation()
    {
        // given
        var dictionary = new Dictionary<string, string?>(StringComparer.Ordinal) { ["existing-key"] = "test-value" };
        var header = new MessageHeader(dictionary);

        // when
        var existsResult = header.TryGetValue("existing-key", out var existingValue);
        var notExistsResult = header.TryGetValue("non-existing-key", out var nonExistingValue);

        // then
        existsResult.Should().BeTrue();
        existingValue.Should().Be("test-value");
        notExistsResult.Should().BeFalse();
        nonExistingValue.Should().BeNull();
    }

    [Fact]
    public void should_support_case_sensitive_keys()
    {
        // given
        var dictionary = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["MyKey"] = "value1",
            ["mykey"] = "value2",
        };
        var header = new MessageHeader(dictionary);

        // when/then
        header["MyKey"].Should().Be("value1");
        header["mykey"].Should().Be("value2");
        header.Count.Should().Be(2);
    }

    [Fact]
    public void should_allow_null_values()
    {
        // given
        var dictionary = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["nullable-key"] = null,
            ["non-nullable-key"] = "value",
        };
        var header = new MessageHeader(dictionary);

        // when/then
        header["nullable-key"].Should().BeNull();
        header["non-nullable-key"].Should().Be("value");
    }

    [Fact]
    public void should_enumerate_keys_and_values()
    {
        // given
        var dictionary = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["key1"] = "value1",
            ["key2"] = "value2",
            ["key3"] = "value3",
        };
        var header = new MessageHeader(dictionary);

        // when
        var keys = header.Keys.ToList();
        var values = header.Values.ToList();

        // then
        keys.Should().HaveCount(3);
        keys.Should().Contain(["key1", "key2", "key3"]);
        values.Should().HaveCount(3);
        values.Should().Contain(["value1", "value2", "value3"]);
    }

    [Fact]
    public void should_add_response_header()
    {
        // given
        var dictionary = new Dictionary<string, string?>(StringComparer.Ordinal);
        var header = new MessageHeader(dictionary);

        // when
        header.AddResponseHeader("response-key", "response-value");

        // then
        header.ResponseHeader.Should().NotBeNull();
        header.ResponseHeader!["response-key"].Should().Be("response-value");
    }

    [Fact]
    public void should_add_multiple_response_headers()
    {
        // given
        var dictionary = new Dictionary<string, string?>(StringComparer.Ordinal);
        var header = new MessageHeader(dictionary);

        // when
        header.AddResponseHeader("key1", "value1");
        header.AddResponseHeader("key2", "value2");

        // then
        header.ResponseHeader.Should().HaveCount(2);
        header.ResponseHeader!["key1"].Should().Be("value1");
        header.ResponseHeader["key2"].Should().Be("value2");
    }

    [Fact]
    public void should_overwrite_response_header_with_same_key()
    {
        // given
        var dictionary = new Dictionary<string, string?>(StringComparer.Ordinal);
        var header = new MessageHeader(dictionary);

        // when
        header.AddResponseHeader("key", "original-value");
        header.AddResponseHeader("key", "new-value");

        // then
        header.ResponseHeader!["key"].Should().Be("new-value");
    }

    [Fact]
    public void should_allow_null_response_header_value()
    {
        // given
        var dictionary = new Dictionary<string, string?>(StringComparer.Ordinal);
        var header = new MessageHeader(dictionary);

        // when
        header.AddResponseHeader("nullable-key", null);

        // then
        header.ResponseHeader!["nullable-key"].Should().BeNull();
    }

    [Fact]
    public void should_remove_callback()
    {
        // given
        var dictionary = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.CallbackName] = "my-callback",
            ["other-key"] = "other-value",
        };
        var header = new MessageHeader(dictionary);

        // when
        header.RemoveCallback();

        // then
        header.ContainsKey(Headers.CallbackName).Should().BeFalse();
        header.ContainsKey("other-key").Should().BeTrue();
    }

    [Fact]
    public void should_not_throw_when_removing_nonexistent_callback()
    {
        // given
        var dictionary = new Dictionary<string, string?>(StringComparer.Ordinal) { ["some-key"] = "some-value" };
        var header = new MessageHeader(dictionary);

        // when
        var act = () => header.RemoveCallback();

        // then
        act.Should().NotThrow();
    }

    [Fact]
    public void should_rewrite_callback()
    {
        // given
        var dictionary = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.CallbackName] = "original-callback",
        };
        var header = new MessageHeader(dictionary);

        // when
        header.RewriteCallback("new-callback");

        // then
        header[Headers.CallbackName].Should().Be("new-callback");
    }

    [Fact]
    public void should_add_callback_when_rewriting_nonexistent()
    {
        // given
        var dictionary = new Dictionary<string, string?>(StringComparer.Ordinal);
        var header = new MessageHeader(dictionary);

        // when
        header.RewriteCallback("new-callback");

        // then
        header[Headers.CallbackName].Should().Be("new-callback");
    }

    [Fact]
    public void should_initialize_response_header_lazily()
    {
        // given
        var dictionary = new Dictionary<string, string?>(StringComparer.Ordinal);
        var header = new MessageHeader(dictionary);

        // when - before adding response header
        // then
        header.ResponseHeader.Should().BeNull();

        // when - after adding response header
        header.AddResponseHeader("key", "value");

        // then
        header.ResponseHeader.Should().NotBeNull();
    }
}
