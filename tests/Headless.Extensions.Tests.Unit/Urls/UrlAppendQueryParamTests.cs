// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Urls;

namespace Tests.Urls;

public sealed class UrlAppendQueryParamTests
{
    [Fact]
    public void should_append_params_without_overwriting_when_using_params_array()
    {
        // given
        var url = new Url("http://example.com?a=1");

        // when
        url.AppendQueryParam("a", "2");
        url.AppendQueryParam("b", "c");

        // then
        url.ToString().Should().Be("http://example.com?a=1&a=2&b=c");
    }

    [Fact]
    public void should_append_multiple_params_without_values_when_using_params_array()
    {
        // given
        var url = new Url("http://example.com?x");

        // when - note: this overload adds params without values (valueless query params)
        url.AppendQueryParam("x", "y", "z");

        // then - x is appended (not overwritten), y and z are added
        url.ToString().Should().Be("http://example.com?x&x&y&z");
    }

    [Fact]
    public void should_append_params_from_enumerable_without_overwriting()
    {
        // given
        var url = new Url("http://example.com?a=1");
        var names = new List<string> { "a", "b" };

        // when
        url.AppendQueryParam(names);

        // then
        url.ToString().Should().Be("http://example.com?a=1&a&b");
    }
}
