// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Constants;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

// NOTE: GetIpAddress now returns Connection.RemoteIpAddress only. UseForwardedHeaders (when
// configured with trusted proxies) rewrites RemoteIpAddress from X-Forwarded-For / X-Real-IP
// before the extension is called, so reading headers directly is no longer done here.

namespace Tests.Extensions;

public sealed class HttpContextExtensionsTests : TestBase
{
    private static DefaultHttpContext _CreateContext()
    {
        return new DefaultHttpContext();
    }

    #region GetIpAddress

    [Fact]
    public void should_get_ip_from_connection()
    {
        // given
        var context = _CreateContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("172.16.0.1");

        // when
        var result = context.GetIpAddress();

        // then
        result.Should().Be("172.16.0.1");
    }

    [Fact]
    public void should_map_ipv4_mapped_ipv6_to_ipv4_from_connection()
    {
        // given
        var context = _CreateContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("::ffff:172.16.0.1");

        // when
        var result = context.GetIpAddress();

        // then
        result.Should().Be("172.16.0.1");
    }

    [Fact]
    public void should_return_native_ipv6_address_from_connection()
    {
        // given
        var context = _CreateContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("2001:db8::1");

        // when
        var result = context.GetIpAddress();

        // then
        result.Should().Be("2001:db8::1");
    }

    [Fact]
    public void should_return_null_when_no_ip_available()
    {
        // given
        var context = _CreateContext();

        // when
        var result = context.GetIpAddress();

        // then
        result.Should().BeNull();
    }

    [Fact]
    public void should_ignore_x_forwarded_for_header()
    {
        // X-Forwarded-For headers are forged by clients; UseForwardedHeaders rewrites
        // RemoteIpAddress before this extension is called. Without that middleware the header
        // is untrusted and must not be read directly.
        var context = _CreateContext();
        context.Request.Headers["X-Forwarded-For"] = "192.168.1.100";
        // RemoteIpAddress left null — simulates no UseForwardedHeaders in pipeline

        // when
        var result = context.GetIpAddress();

        // then
        result.Should().BeNull();
    }

    [Fact]
    public void should_ignore_x_real_ip_header()
    {
        // Same rationale as X-Forwarded-For above.
        var context = _CreateContext();
        context.Request.Headers["X-Real-IP"] = "10.0.0.1";

        // when
        var result = context.GetIpAddress();

        // then
        result.Should().BeNull();
    }

    #endregion

    #region GetIpAddressPartition

    [Theory]
    [InlineData("172.16.0.1", "172.16.0.1")]
    [InlineData("::ffff:172.16.0.1", "172.16.0.1")]
    [InlineData("2001:db8:1:2:aaaa:bbbb:cccc:dddd", "2001:db8:1:2::/64")]
    [InlineData("fe80::1%3", "fe80::/64")]
    public void should_partition_ip_address(string remoteIp, string expected)
    {
        // given
        var context = _CreateContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);

        // when
        var result = context.GetIpAddressPartition();

        // then
        result.Should().Be(expected);
    }

    [Fact]
    public void should_share_one_partition_across_ipv6_addresses_in_the_same_64()
    {
        // given
        var first = _CreateContext();
        first.Connection.RemoteIpAddress = IPAddress.Parse("2001:db8:1:2::1");
        var second = _CreateContext();
        second.Connection.RemoteIpAddress = IPAddress.Parse("2001:db8:1:2:ffff:ffff:ffff:ffff");
        var otherNetwork = _CreateContext();
        otherNetwork.Connection.RemoteIpAddress = IPAddress.Parse("2001:db8:1:3::1");

        // when
        var firstPartition = first.GetIpAddressPartition();
        var secondPartition = second.GetIpAddressPartition();
        var otherPartition = otherNetwork.GetIpAddressPartition();

        // then
        firstPartition.Should().Be(secondPartition);
        otherPartition.Should().NotBe(firstPartition);
    }

    [Fact]
    public void should_keep_ipv4_mapped_clients_out_of_the_shared_ipv6_partition()
    {
        // given
        var first = _CreateContext();
        first.Connection.RemoteIpAddress = IPAddress.Parse("::ffff:10.0.0.1");
        var second = _CreateContext();
        second.Connection.RemoteIpAddress = IPAddress.Parse("::ffff:10.0.0.2");

        // when
        var firstPartition = first.GetIpAddressPartition();
        var secondPartition = second.GetIpAddressPartition();

        // then
        firstPartition.Should().NotBe(secondPartition);
    }

    [Theory]
    [InlineData(48, "2001:db8:1::/48")]
    [InlineData(56, "2001:db8:1:200::/56")]
    [InlineData(60, "2001:db8:1:2a0::/60")]
    [InlineData(128, "2001:db8:1:2ab::1/128")]
    [InlineData(0, "::/0")]
    public void should_mask_ipv6_to_the_configured_prefix(int prefixLength, string expected)
    {
        // given
        var context = _CreateContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("2001:db8:1:2ab::1");

        // when
        var result = context.GetIpAddressPartition(prefixLength);

        // then
        result.Should().Be(expected);
    }

    [Fact]
    public void should_return_null_partition_when_no_remote_address()
    {
        // given
        var context = _CreateContext();

        // when
        var result = context.GetIpAddressPartition();

        // then
        result.Should().BeNull();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(129)]
    public void should_reject_ipv6_prefix_length_outside_0_to_128(int prefixLength)
    {
        // given
        var context = _CreateContext();

        // when
        var act = () => context.GetIpAddressPartition(prefixLength);

        // then
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    #endregion

    #region GetUserAgent

    [Fact]
    public void should_get_user_agent_from_header()
    {
        // given
        var context = _CreateContext();
        const string userAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/91.0";
        context.Request.Headers[HeaderNames.UserAgent] = userAgent;

        // when
        var result = context.GetUserAgent();

        // then
        result.Should().Be(userAgent);
    }

    [Fact]
    public void should_return_null_when_user_agent_not_present()
    {
        // given
        var context = _CreateContext();

        // when
        var result = context.GetUserAgent();

        // then
        result.Should().BeNull();
    }

    #endregion

    #region GetCorrelationId

    [Fact]
    public void should_get_correlation_id_from_header()
    {
        // given
        var context = _CreateContext();
        const string correlationId = "abc-123-def-456";
        context.Request.Headers[HttpHeaderNames.CorrelationId] = correlationId;

        // when
        var result = context.GetCorrelationId();

        // then
        result.Should().Be(correlationId);
    }

    [Fact]
    public void should_return_null_when_correlation_id_not_present()
    {
        // given
        var context = _CreateContext();

        // when
        var result = context.GetCorrelationId();

        // then
        result.Should().BeNull();
    }

    #endregion

    #region AddNoCacheHeaders

    [Fact]
    public void should_set_no_cache_headers()
    {
        // given
        var context = _CreateContext();

        // when
        context.AddNoCacheHeaders();

        // then
        context
            .Response.Headers[HeaderNames.CacheControl]
            .ToString()
            .Should()
            .Be("no-cache, no-store, must-revalidate");
        context.Response.Headers[HeaderNames.Pragma].ToString().Should().Be("no-cache");
        context.Response.Headers[HeaderNames.Expires].ToString().Should().Be("-1");
    }

    [Fact]
    public void should_remove_etag_header_when_setting_no_cache()
    {
        // given
        var context = _CreateContext();
        context.Response.Headers[HeaderNames.ETag] = "\"abc123\"";

        // when
        context.AddNoCacheHeaders();

        // then
        context.Response.Headers.Should().NotContainKey(HeaderNames.ETag);
    }

    #endregion

    #region ApplyCacheProfile

    [Fact]
    public void should_set_cache_headers_with_ttl()
    {
        // given
        var context = _CreateContext();
        var cacheProfile = new CacheProfile { Duration = 3600, Location = ResponseCacheLocation.Any };

        // when
        context.ApplyCacheProfile(cacheProfile);

        // then
        context.Response.Headers[HeaderNames.CacheControl].ToString().Should().Be("public,max-age=3600");
    }

    [Fact]
    public void should_set_private_cache_for_client_location()
    {
        // given
        var context = _CreateContext();
        var cacheProfile = new CacheProfile { Duration = 1800, Location = ResponseCacheLocation.Client };

        // when
        context.ApplyCacheProfile(cacheProfile);

        // then
        context.Response.Headers[HeaderNames.CacheControl].ToString().Should().Be("private,max-age=1800");
    }

    [Fact]
    public void should_set_no_cache_with_max_age_for_none_location()
    {
        // given
        var context = _CreateContext();
        var cacheProfile = new CacheProfile { Duration = 600, Location = ResponseCacheLocation.None };

        // when
        context.ApplyCacheProfile(cacheProfile);

        // then
        context.Response.Headers[HeaderNames.CacheControl].ToString().Should().Be("no-cache,max-age=600");
        context.Response.Headers[HeaderNames.Pragma].ToString().Should().Be("no-cache");
    }

    [Fact]
    public void should_set_no_store_when_no_store_true()
    {
        // given
        var context = _CreateContext();
        var cacheProfile = new CacheProfile { NoStore = true };

        // when
        context.ApplyCacheProfile(cacheProfile);

        // then
        context.Response.Headers[HeaderNames.CacheControl].ToString().Should().Be("no-store");
    }

    [Fact]
    public void should_set_no_store_no_cache_when_no_store_and_none_location()
    {
        // given
        var context = _CreateContext();
        var cacheProfile = new CacheProfile { NoStore = true, Location = ResponseCacheLocation.None };

        // when
        context.ApplyCacheProfile(cacheProfile);

        // then
        context.Response.Headers[HeaderNames.CacheControl].ToString().Should().Be("no-store,no-cache");
        context.Response.Headers[HeaderNames.Pragma].ToString().Should().Be("no-cache");
    }

    [Fact]
    public void should_set_vary_header_when_specified()
    {
        // given
        var context = _CreateContext();
        var cacheProfile = new CacheProfile
        {
            Duration = 3600,
            Location = ResponseCacheLocation.Any,
            VaryByHeader = "Accept-Encoding",
        };

        // when
        context.ApplyCacheProfile(cacheProfile);

        // then
        context.Response.Headers[HeaderNames.Vary].ToString().Should().Be("Accept-Encoding");
    }

    [Fact]
    public void should_throw_when_context_null()
    {
        // given
        HttpContext context = null!;
        var cacheProfile = new CacheProfile();

        // when
        var act = () => context.ApplyCacheProfile(cacheProfile);

        // then
        act.Should().Throw<ArgumentNullException>().WithParameterName("context");
    }

    [Fact]
    public void should_throw_when_cache_profile_null()
    {
        // given
        var context = _CreateContext();

        // when
        var act = () => context.ApplyCacheProfile(null!);

        // then
        act.Should().Throw<ArgumentNullException>().WithParameterName("cacheProfile");
    }

    #endregion
}
