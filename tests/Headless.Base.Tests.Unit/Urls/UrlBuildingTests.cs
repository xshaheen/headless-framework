// Copyright (c) Mahmoud Shaheen. All rights reserved.
// Adapted from Flurl (https://github.com/tmenier/Flurl) under MIT License.

using Headless.Urls;

namespace Tests.Urls;

public sealed class UrlBuildingTests
{
    [Fact]
    public void should_construct_from_uri()
    {
        var s = "http://www.mysite.com/with/path?x=1&y=2#foo";
        var uri = new Uri(s);
        var url = new Url(uri);
        url.ToString().Should().Be(s);
    }

    [Fact]
    public void should_throw_when_uri_is_null()
    {
        var action = () => new Url((Uri)null!);
        action.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void should_set_query_params()
    {
        var url = "http://www.mysite.com/more"
            .SetQueryParam("x", 1)
            .SetQueryParam("y", new[] { "2", "4", "6" })
            .SetQueryParam("z", 3)
            .SetQueryParam("abc")
            .SetQueryParam("xyz")
            .SetQueryParam("foo", "")
            .SetQueryParam("", "bar");

        url.ToString().Should().Be("http://www.mysite.com/more?x=1&y=2&y=4&y=6&z=3&abc&xyz&foo=&=bar");
    }

    [Fact]
    public void should_append_query_params()
    {
        var url = "http://www.mysite.com/more"
            .AppendQueryParam("x", 1)
            .AppendQueryParam("x", new[] { "2", "4", "6" })
            .AppendQueryParam("x", 3)
            .AppendQueryParam("x")
            .AppendQueryParam("x", "");

        url.ToString().Should().Be("http://www.mysite.com/more?x=1&x=2&x=4&x=6&x=3&x&x=");
    }

    [Theory]
    [InlineData("http://www.mysite.com/more")]
    [InlineData("http://www.mysite.com/more?x=1")]
    public void should_ignore_null_or_empty_query_params(string original)
    {
        var modified1 = original.SetQueryParams("").ToString();
        modified1.Should().Be(original);
        var modified2 = original.SetQueryParams(null!).ToString();
        modified2.Should().Be(original);
    }

    [Fact]
    public void should_change_query_param()
    {
        var url = "http://www.mysite.com?x=1".SetQueryParam("x", 2);
        url.ToString().Should().Be("http://www.mysite.com?x=2");
    }

    [Fact]
    public void should_split_enumerable_query_param_into_multiple()
    {
        var url = "http://www.mysite.com".SetQueryParam("x", new[] { "a", "b", null, "c" });
        url.ToString().Should().Be("http://www.mysite.com?x=a&x=b&x=c");
    }

    [Fact]
    public void should_set_multiple_query_params_from_anon_object()
    {
        var url = "http://www.mysite.com".SetQueryParams(
            new
            {
                x = 1,
                y = 2,
                z = new[] { 3, 4 },
                exclude_me = (string?)null,
            }
        );
        url.ToString().Should().Be("http://www.mysite.com?x=1&y=2&z=3&z=4");
    }

    [Fact]
    public void should_replace_query_params_from_anon_object()
    {
        var url = "http://www.mysite.com?x=1&y=2&z=3".SetQueryParams(
            new
            {
                x = 8,
                y = new[] { "a", "b" },
                z = (int?)null,
            }
        );
        url.ToString().Should().Be("http://www.mysite.com?x=8&y=a&y=b");
    }

    [Fact]
    public void should_set_query_params_from_string()
    {
        // Our implementation parses string into key-value pairs when passing as object
        var url = "http://www.mysite.com".SetQueryParams(new { x = 1, y = "this&that" });
        url.ToString().Should().Be("http://www.mysite.com?x=1&y=this%26that");
    }

    [Fact]
    public void should_set_query_params_from_dictionary()
    {
        var url = "http://www.mysite.com".SetQueryParams(new Dictionary<int, string> { { 1, "x" }, { 2, "y" } });
        url.ToString().Should().Be("http://www.mysite.com?1=x&2=y");
    }

    [Fact]
    public void should_set_query_params_from_kv_pairs()
    {
        var url = "http://foo.com".SetQueryParams(
            new[] { new { key = "x", value = 1 }, new { key = "y", value = 2 }, new { key = "x", value = 3 } }
        );

        url.ToString().Should().Be("http://foo.com?x=1&y=2&x=3");
    }

    [Fact]
    public void should_set_multiple_no_value_query_params_from_params()
    {
        var url = "http://www.mysite.com".SetQueryParams("abc", "123", null!, "456");
        url.ToString().Should().Be("http://www.mysite.com?abc&123&456");
    }

    [Fact]
    public void should_remove_query_param()
    {
        var url = "http://www.mysite.com/more?x=1&y=2".RemoveQueryParam("x");
        url.ToString().Should().Be("http://www.mysite.com/more?y=2");
    }

    [Fact]
    public void should_remove_query_params_by_multi_args()
    {
        var url = "http://www.mysite.com/more?x=1&y=2".RemoveQueryParams("x", "y");
        url.ToString().Should().Be("http://www.mysite.com/more");
    }

    [Fact]
    public void should_remove_query_params_by_enumerable()
    {
        var url = "http://www.mysite.com/more?x=1&y=2&z=3".RemoveQueryParams(new[] { "x", "z" });
        url.ToString().Should().Be("http://www.mysite.com/more?y=2");
    }

    [Theory]
    [InlineData("http://www.mysite.com/?x=1&y=2&z=3#foo", "http://www.mysite.com/#foo")]
    [InlineData("http://www.mysite.com/more?x=1&y=2&z=3#foo", "http://www.mysite.com/more#foo")]
    [InlineData("relative/path?foo", "relative/path")]
    public void should_remove_query(string original, string expected)
    {
        var url = original.RemoveQuery();
        url.ToString().Should().Be(expected);
    }

    [Theory]
    [InlineData("http://www.mysite.com", "endpoint", "http://www.mysite.com/endpoint")]
    [InlineData("path1", "path2", "path1/path2")]
    [InlineData("/path1/path2", "path3", "/path1/path2/path3")]
    public void should_append_path_segment(string original, string segment, string result)
    {
        original.AppendPathSegment(segment).ToString().Should().Be(result);
        original.AppendPathSegment("/" + segment).ToString().Should().Be(result);
        (original + "/").AppendPathSegment(segment).ToString().Should().Be(result);
        (original + "/").AppendPathSegment("/" + segment).ToString().Should().Be(result);
    }

    [Fact]
    public void should_throw_when_appending_null_path_segment()
    {
        var action = () => "http://www.mysite.com".AppendPathSegment(null!);
        action.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void should_append_multiple_path_segments_by_multi_args()
    {
        var url = "http://www.mysite.com".AppendPathSegments("category", "/endpoint/");
        url.ToString().Should().Be("http://www.mysite.com/category/endpoint/");
    }

    [Fact]
    public void should_append_multiple_path_segments_by_enumerable()
    {
        IEnumerable<string> segments = new[] { "/category/", "endpoint" };
        var url = "http://www.mysite.com".AppendPathSegments(segments);
        url.ToString().Should().Be("http://www.mysite.com/category/endpoint");
    }

    [Theory]
    [InlineData("http://www.site.com/path1/path2/?x=y", "http://www.site.com/path1/?x=y")]
    [InlineData("http://www.site.com/path1/path2?x=y", "http://www.site.com/path1?x=y")]
    [InlineData("http://www.site.com/path1/", "http://www.site.com/")]
    [InlineData("http://www.site.com/path1", "http://www.site.com/")]
    [InlineData("http://www.site.com/", "http://www.site.com/")]
    [InlineData("http://www.site.com", "http://www.site.com")]
    [InlineData("/path1/path2", "/path1")]
    [InlineData("path1/path2", "path1")]
    public void should_remove_path_segment(string original, string expected)
    {
        var url = original.RemovePathSegment();
        url.ToString().Should().Be(expected);
    }

    [Theory]
    [InlineData("http://www.site.com/path1/path2/?x=y", "http://www.site.com?x=y")]
    [InlineData("http://www.site.com/path1/path2?x=y", "http://www.site.com?x=y")]
    [InlineData("http://www.site.com/", "http://www.site.com")]
    [InlineData("http://www.site.com", "http://www.site.com")]
    [InlineData("/path1/path2", "")]
    [InlineData("path1/path2", "")]
    [InlineData("news:comp.infosystems.www.servers.unix", "news:")]
    public void should_remove_path(string original, string expected)
    {
        var url = original.RemovePath();
        url.ToString().Should().Be(expected);
    }

    [Fact]
    public void should_use_invariant_culture_in_tostring()
    {
        var originalCulture = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("es-ES");
            var url = "http://www.mysite.com".SetQueryParam("x", 1.1);
            url.ToString().Should().Be("http://www.mysite.com?x=1.1");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void should_reset_to_root()
    {
        var url = "http://www.mysite.com/more?x=1&y=2#foo".ResetToRoot();
        url.ToString().Should().Be("http://www.mysite.com");
    }

    [Fact]
    public void should_do_crazy_long_fluent_expression()
    {
        var url = "http://www.mysite.com"
            .SetQueryParams(
                new
                {
                    a = 1,
                    b = 2,
                    c = 999,
                }
            )
            .SetFragment("fooey")
            .AppendPathSegment("category")
            .RemoveQueryParam("c")
            .SetQueryParam("z", 55)
            .RemoveQueryParams("a", "z")
            .SetQueryParams(new { n = "hi", m = "bye" })
            .AppendPathSegment("endpoint");

        url.ToString().Should().Be("http://www.mysite.com/category/endpoint?b=2&n=hi&m=bye#fooey");
    }

    [Fact]
    public void should_encode_illegal_path_chars()
    {
        var url = "http://www.mysite.com".AppendPathSegment("hi there/bye now");
        url.ToString().Should().Be("http://www.mysite.com/hi%20there/bye%20now");
    }

    [Fact]
    public void should_encode_reserved_path_chars_when_fullyencode()
    {
        var url = "http://www.mysite.com".AppendPathSegment("hi there/bye now", true);
        url.ToString().Should().Be("http://www.mysite.com/hi%20there%2Fbye%20now");
    }

    [Fact]
    public void should_encode_spaces_in_path_as_percent20()
    {
        var url = "http://www.mysite.com".AppendPathSegment("hi there");
        url.ToString().Should().Be("http://www.mysite.com/hi%20there");
    }

    [Fact]
    public void should_encode_query_params()
    {
        var url = "http://www.mysite.com".SetQueryParams(new { x = "$50", y = "2+2=4" });
        url.ToString().Should().Be("http://www.mysite.com?x=%2450&y=2%2B2%3D4");
    }

    [Fact]
    public void should_not_reencode_encoded_query_values()
    {
        var url = "http://www.mysite.com".SetQueryParam("x", "%CD%EE%E2%FB%E9%20%E3%EE%E4", true);
        url.ToString().Should().Be("http://www.mysite.com?x=%CD%EE%E2%FB%E9%20%E3%EE%E4");
    }

    [Fact]
    public void should_reencode_when_isEncoded_false()
    {
        var url = "http://www.mysite.com".SetQueryParam("x", "%CD%EE%E2%FB%E9%20%E3%EE%E4", false);
        url.ToString().Should().Be("http://www.mysite.com?x=%25CD%25EE%25E2%25FB%25E9%2520%25E3%25EE%25E4");
    }

    [Fact]
    public void should_encode_plus()
    {
        var url = new Url("http://www.mysite.com").SetQueryParam("x", "1+2");
        url.ToString().Should().Be("http://www.mysite.com?x=1%2B2");
    }

    [Fact]
    public void should_encode_space_as_plus_when_requested()
    {
        var url = new Url("http://www.mysite.com").AppendPathSegment("a b").SetQueryParam("c d", "1 2");
        url.ToString().Should().Be("http://www.mysite.com/a%20b?c%20d=1%202");
        url.ToString(true).Should().Be("http://www.mysite.com/a+b?c+d=1+2");
    }

    [Fact]
    public void should_add_and_remove_fragment_fluently()
    {
        var url = "http://www.mysite.com".SetFragment("foo");
        url.ToString().Should().Be("http://www.mysite.com#foo");
        url = "http://www.mysite.com#foo".RemoveFragment();
        url.ToString().Should().Be("http://www.mysite.com");
        url = "http://www.mysite.com"
            .SetFragment("foo")
            .SetFragment("bar")
            .AppendPathSegment("more")
            .SetQueryParam("x", 1);
        url.ToString().Should().Be("http://www.mysite.com/more?x=1#bar");
    }

    [Fact]
    public void should_have_fragment_after_setqueryparam()
    {
        var expected = "http://www.mysite.com/more?x=1#first";
        var url = new Url(expected).SetQueryParam("x", 3).SetQueryParam("y", 4);
        url.ToString().Should().Be("http://www.mysite.com/more?x=3&y=4#first");
    }

    [Fact]
    public void should_return_true_for_same_values_in_equals()
    {
        var url1 = new Url("http://mysite.com/hello");
        var url2 = new Url("http://mysite.com").AppendPathSegment("hello");
        var url3 = new Url("http://mysite.com/hello/");

        url1.Equals(url2).Should().BeTrue();
        url2.Equals(url1).Should().BeTrue();
        url1.GetHashCode().Should().Be(url2.GetHashCode());

        url1.Equals(url3).Should().BeFalse();
        url3.Equals(url1).Should().BeFalse();
    }

    [Fact]
    public void should_clone_create_copy()
    {
        var url1 = new Url("http://mysite.com").SetQueryParam("x", 1);
        var url2 = url1.Clone().AppendPathSegment("foo").SetQueryParam("y", 2);
        url1.SetQueryParam("z", 3);

        url1.ToString().Should().Be("http://mysite.com?x=1&z=3");
        url2.ToString().Should().Be("http://mysite.com/foo?x=1&y=2");
    }

    [Fact]
    public void should_write_scheme()
    {
        var url = new Url("https://api.com/foo") { Scheme = "ftp" };

        url.ToString().Should().Be("ftp://api.com/foo");
    }

    [Fact]
    public void should_write_host()
    {
        var url = new Url("https://api.com/foo") { Host = "www.othersite.net" };

        url.ToString().Should().Be("https://www.othersite.net/foo");
    }

    [Fact]
    public void should_write_port()
    {
        var url = new Url("https://api.com/foo") { Port = 1234 };

        url.ToString().Should().Be("https://api.com:1234/foo");
        url.Port = null;
        url.ToString().Should().Be("https://api.com/foo");
    }

    [Fact]
    public void should_write_path()
    {
        var url = new Url("https://api.com/foo") { Path = "/a/b/c/" };

        url.ToString().Should().Be("https://api.com/a/b/c/");
        url.Path = "a/b/c";
        url.ToString().Should().Be("https://api.com/a/b/c");
        url.Path = "/";
        url.ToString().Should().Be("https://api.com/");
        url.Path = null!;
        url.ToString().Should().Be("https://api.com");
    }

    [Fact]
    public void should_write_query()
    {
        var url = new Url("https://api.com/foo?x=0#bar") { Query = "y=1&z=2" };

        url.ToString().Should().Be("https://api.com/foo?y=1&z=2#bar");
        url.Query = null!;
        url.ToString().Should().Be("https://api.com/foo#bar");
    }

    [Fact]
    public void should_write_fragment()
    {
        var url = new Url("https://api.com/") { Fragment = "hello" };

        url.ToString().Should().Be("https://api.com/#hello");
        url.Fragment = null!;
        url.ToString().Should().Be("https://api.com/");
    }

    [Fact]
    public void should_parse_uri_with_default_port_correctly()
    {
        var originalString = "https://someurl.net:443/api/somepath";
        var uri = new Uri(originalString);
        var url = new Url(uri);
        url.Port.Should().Be(443);
        url.ToString().Should().Be(originalString);
    }

    [Fact]
    public void should_have_empty_ctor()
    {
        var url1 = new Url();
        url1.ToString().Should().Be("");

        var url2 = new Url { Host = "192.168.1.1", Scheme = "http" };
        url2.ToString().Should().Be("http://192.168.1.1");
    }

    [Fact]
    public void should_append_trailing_slash()
    {
        var url = new Url("https://www.site.com/a/b/c");
        url.ToString().Should().Be("https://www.site.com/a/b/c");
        url.AppendPathSegment("/");
        url.ToString().Should().Be("https://www.site.com/a/b/c/");
    }

    [Fact]
    public void should_trim_leading_and_trailing_whitespace()
    {
        var url = new Url("  https://www.site.com \t");
        url.ToString().Should().Be("https://www.site.com");
    }
}
