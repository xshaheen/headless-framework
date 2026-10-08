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
}
