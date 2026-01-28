// Copyright (c) Mahmoud Shaheen. All rights reserved.
// Adapted from Flurl (https://github.com/tmenier/Flurl) under MIT License.

using Headless.Urls;

namespace Tests.Urls;

public sealed class UrlParsingTests
{
    [Theory]
    // relative
    [InlineData("//relative/with/authority", "", "relative", "", "relative", null, "/with/authority", "", "")]
    [InlineData("/relative/without/authority", "", "", "", "", null, "/relative/without/authority", "", "")]
    [InlineData("relative/without/path/anchor", "", "", "", "", null, "relative/without/path/anchor", "", "")]
    // absolute
    [InlineData(
        "http://www.mysite.com/with/path?x=1",
        "http",
        "www.mysite.com",
        "",
        "www.mysite.com",
        null,
        "/with/path",
        "x=1",
        ""
    )]
    [InlineData(
        "https://www.mysite.com/with/path?x=1#foo",
        "https",
        "www.mysite.com",
        "",
        "www.mysite.com",
        null,
        "/with/path",
        "x=1",
        "foo"
    )]
    [InlineData(
        "http://user:pass@www.mysite.com:8080/with/path?x=1?y=2",
        "http",
        "user:pass@www.mysite.com:8080",
        "user:pass",
        "www.mysite.com",
        8080,
        "/with/path",
        "x=1?y=2",
        ""
    )]
    [InlineData(
        "http://www.mysite.com/#with/path?x=1?y=2",
        "http",
        "www.mysite.com",
        "",
        "www.mysite.com",
        null,
        "/",
        "",
        "with/path?x=1?y=2"
    )]
    // from https://en.wikipedia.org/wiki/Uniform_Resource_Identifier#Examples
    [InlineData(
        "https://john.doe@www.example.com:123/forum/questions/?tag=networking&order=newest#top",
        "https",
        "john.doe@www.example.com:123",
        "john.doe",
        "www.example.com",
        123,
        "/forum/questions/",
        "tag=networking&order=newest",
        "top"
    )]
    [InlineData(
        "ldap://[2001:db8::7]/c=GB?objectClass?one",
        "ldap",
        "[2001:db8::7]",
        "",
        "[2001:db8::7]",
        null,
        "/c=GB",
        "objectClass?one",
        ""
    )]
    [InlineData("mailto:John.Doe@example.com", "mailto", "", "", "", null, "John.Doe@example.com", "", "")]
    [InlineData(
        "news:comp.infosystems.www.servers.unix",
        "news",
        "",
        "",
        "",
        null,
        "comp.infosystems.www.servers.unix",
        "",
        ""
    )]
    [InlineData("tel:+1-816-555-1212", "tel", "", "", "", null, "+1-816-555-1212", "", "")]
    [InlineData("telnet://192.0.2.16:80/", "telnet", "192.0.2.16:80", "", "192.0.2.16", 80, "/", "", "")]
    [InlineData(
        "urn:oasis:names:specification:docbook:dtd:xml:4.1.2",
        "urn",
        "",
        "",
        "",
        null,
        "oasis:names:specification:docbook:dtd:xml:4.1.2",
        "",
        ""
    )]
    // with uppercase letters
    [InlineData(
        "http://www.mySite.com:8080/With/Path?x=1?Y=2",
        "http",
        "www.mysite.com:8080",
        "",
        "www.mysite.com",
        8080,
        "/With/Path",
        "x=1?Y=2",
        ""
    )]
    [InlineData("HTTP://www.mysite.com:8080", "http", "www.mysite.com:8080", "", "www.mysite.com", 8080, "", "", "")]
    public void should_parse_url_parts(
        string url,
        string scheme,
        string authority,
        string userInfo,
        string host,
        int? port,
        string path,
        string query,
        string fragment
    )
    {
        foreach (
            var parsed in new[] { new Url(url), Url.Parse(url), new Url(new Uri(url, UriKind.RelativeOrAbsolute)) }
        )
        {
            parsed.Scheme.Should().Be(scheme);
            parsed.Authority.Should().Be(authority);
            parsed.UserInfo.Should().Be(userInfo);
            parsed.Host.Should().Be(host);
            parsed.Port.Should().Be(port);
            parsed.Path.Should().Be(path);
            parsed.Query.Should().Be(query);
            parsed.Fragment.Should().Be(fragment);
        }
    }

    [Theory]
    [InlineData("http://www.trailing-slash.com/", "/")]
    [InlineData("http://www.trailing-slash.com/a/b/", "/a/b/")]
    [InlineData("http://www.trailing-slash.com/a/b/?x=y", "/a/b/")]
    [InlineData("http://www.no-trailing-slash.com", "")]
    [InlineData("http://www.no-trailing-slash.com/a/b", "/a/b")]
    [InlineData("http://www.no-trailing-slash.com/a/b?x=y", "/a/b")]
    public void should_retain_trailing_slash_in_path(string original, string path)
    {
        var url = Url.Parse(original);
        url.ToString().Should().Be(original);
        url.Path.Should().Be(path);
    }

    [Theory]
    [InlineData("https://foo.com/x?")]
    [InlineData("https://foo.com/x#")]
    [InlineData("https://foo.com/x?#")]
    public void should_retain_trailing_chars(string original)
    {
        var url = Url.Parse(original);
        url.ToString().Should().Be(original);
    }

    [Fact]
    public void should_parse_query_params()
    {
        var q = new Url("http://www.mysite.com/more?x=1&y=2&z=3&y=4&abc&xyz&foo=&=bar&y=6").QueryParams;

        q.Count.Should().Be(9);
        q.FirstOrDefault("x").Should().Be("1");
        q.GetAll("y").Should().BeEquivalentTo(new[] { "2", "4", "6" });
        q.FirstOrDefault("z").Should().Be("3");
        q.FirstOrDefault("abc").Should().BeNull();
        q.FirstOrDefault("xyz").Should().BeNull();
        q.FirstOrDefault("foo").Should().Be("");
        q.FirstOrDefault("").Should().Be("bar");
    }

    [Theory]
    [InlineData("http://www.mysite.com/more?x=1&y=2")]
    [InlineData("//how/about/this#hi")]
    [InlineData("/how/about/this#hi")]
    [InlineData("how/about/this#hi")]
    [InlineData("")]
    public void should_convert_to_uri(string s)
    {
        var url = new Url(s);
        var uri = url.ToUri();
        uri.OriginalString.Should().Be(s);
    }

    [Fact]
    public void should_interpret_plus_as_space()
    {
        var url = new Url("http://www.mysite.com/foo+bar?x=1+2");
        url.QueryParams.FirstOrDefault("x").Should().Be("1 2");
    }

    [Fact]
    public void should_interpret_encoded_plus_as_plus()
    {
        var urlStr = "http://google.com/search?q=param_with_%2B";
        var url = new Url(urlStr);
        var paramValue = url.QueryParams.FirstOrDefault("q");
        paramValue.Should().Be("param_with_+");
    }

    [Fact]
    public void should_not_alter_url_passed_to_constructor()
    {
        var expected = "http://www.mysite.com/hi%20there/more?x=%CD%EE%E2%FB%E9%20%E3%EE%E4";
        var url = new Url(expected);
        url.ToString().Should().Be(expected);
    }

    [Fact]
    public void should_use_equals_in_queryparams_contains()
    {
        var url = new Url("http://www.mysite.com?param=1");
        var contains = url.QueryParams.Contains("param", "1");
        contains.Should().BeTrue();
    }
}
