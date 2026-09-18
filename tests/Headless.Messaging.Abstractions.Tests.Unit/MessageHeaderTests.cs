// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
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
        header.Should().HaveCount(2);
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
        header.Should().ContainKey("existing-key");
        header.Should().NotContainKey("non-existing-key");
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
        header.Should().HaveCount(2);
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
}
