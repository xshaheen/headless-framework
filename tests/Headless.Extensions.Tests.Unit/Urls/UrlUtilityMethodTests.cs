// Copyright (c) Mahmoud Shaheen. All rights reserved.
// Adapted from Flurl (https://github.com/tmenier/Flurl) under MIT License.

using Headless.Urls;

namespace Tests.Urls;

public sealed class UrlUtilityMethodTests
{
    [Fact]
    public void should_combine_url_parts()
    {
        var url = Url.Combine("http://www.foo.com/", "/too/", "/many/", "/slashes/", "too", "few", "one/two/");
        url.Should().Be("http://www.foo.com/too/many/slashes/too/few/one/two/");
    }

    [Theory]
    [InlineData("segment?", "foo=bar", "x=1&y=2&")]
    [InlineData("segment", "?foo=bar&x=1", "y=2&")]
    [InlineData("/segment?foo=bar&", "&x=1&", "&y=2&")]
    [InlineData(null, "segment?foo=bar&x=1&y=2&", "")]
    public void should_combine_with_query_support(string? a, string b, string c)
    {
        var url = Url.Combine("http://root.com", a, b, c);
        url.Should().Be("http://root.com/segment?foo=bar&x=1&y=2&");
    }

    [Fact]
    public void should_encode_illegal_chars_in_combine()
    {
        var url = Url.Combine("http://www.foo.com", "hi there");
        url.Should().Be("http://www.foo.com/hi%20there");
    }

    [Fact]
    public void should_encode_and_decode_very_long_value()
    {
        const int len = 500000;

        // every 10th char needs to be encoded
        var s = string.Concat(Enumerable.Repeat("xxxxxxxxx ", len / 10));
        s.Length.Should().Be(len);

        // encode space as %20
        var encoded = Url.Encode(s, false);
        encoded.Length.Should().Be(len + (2 * len / 10));
        var expected = string.Concat(Enumerable.Repeat("xxxxxxxxx%20", len / 10));
        encoded.Should().Be(expected);

        var decoded = Url.Decode(encoded, false);
        decoded.Should().Be(s);

        // encode space as +
        encoded = Url.Encode(s, true);
        encoded.Length.Should().Be(len);
        expected = string.Concat(Enumerable.Repeat("xxxxxxxxx+", len / 10));
        encoded.Should().Be(expected);

        // interpret + as space
        decoded = Url.Decode(encoded, true);
        decoded.Should().Be(s);

        // don't interpret + as space, encoded and decoded should be the same
        decoded = Url.Decode(encoded, false);
        decoded.Should().Be(encoded);
    }

    [Theory]
    [InlineData("http://www.mysite.com/more", true)]
    [InlineData("http://www.mysite.com/more?x=1&y=2", true)]
    [InlineData("http://www.mysite.com/more?x=1&y=2#frag", true)]
    [InlineData("http://www.mysite.com#frag", true)]
    [InlineData("", false)]
    [InlineData("blah", false)]
    [InlineData("http:/www.mysite.com", false)]
    [InlineData("www.mysite.com", false)]
    [InlineData("/path", false)]
    [InlineData("//path", false)]
    [InlineData("http://myhost.com/%26", true)]
    [InlineData("http://myhost.com/%C3%A9", true)]
    [InlineData("http://myhost.com/%26%C3%A9", true)]
    public void should_validate_url(string s, bool isValid)
    {
        Url.IsValid(s).Should().Be(isValid);
    }
}
