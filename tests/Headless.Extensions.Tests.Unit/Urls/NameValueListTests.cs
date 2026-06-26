// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Urls;

namespace Tests.Urls;

public sealed class NameValueListTests
{
    [Fact]
    public void contains_name_value_should_honor_case_insensitive_names()
    {
        var list = new NameValueList<string>(caseSensitiveNames: false) { { "X", "v" } };

        // Name match must be case-insensitive (every other member honors caseSensitiveNames).
        list.Contains("x", "v").Should().BeTrue();
        list.Contains("X", "v").Should().BeTrue();

        // The value still has to match.
        list.Contains("x", "other").Should().BeFalse();
    }

    [Fact]
    public void contains_name_value_should_respect_case_sensitive_names()
    {
        var list = new NameValueList<string>(caseSensitiveNames: true) { { "X", "v" } };

        list.Contains("X", "v").Should().BeTrue();
        list.Contains("x", "v").Should().BeFalse(); // case-sensitive: "x" != "X"
    }
}
