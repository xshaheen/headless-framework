// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;

namespace Tests.SameOrigin;

public sealed class SameOriginRequestValidatorTests : TestBase
{
    private const string _Self = "https://api.example.com";
    private const string _TrustedSpa = "https://app.example.com";

    private static readonly HeadlessSameOriginOptions _Options = new() { AllowedOrigins = [_TrustedSpa] };

    [Theory]
    // Origin is the API's own origin, alone or with each Sec-Fetch-Site value.
    [InlineData(_Self, null, true)]
    [InlineData(_Self, "same-origin", true)]
    [InlineData(_Self, "same-site", false)]
    [InlineData(_Self, "cross-site", false)]
    [InlineData(_Self, "none", false)]
    // Origin is a listed origin: only a same-site request passes.
    [InlineData(_TrustedSpa, null, true)]
    [InlineData(_TrustedSpa, "same-site", true)]
    [InlineData(_TrustedSpa, "cross-site", false)]
    [InlineData(_TrustedSpa, "none", false)]
    // Origin is neither own nor listed: refused whatever Sec-Fetch-Site says.
    [InlineData("https://evil.example.net", null, false)]
    [InlineData("https://evil.example.net", "same-origin", false)]
    [InlineData("https://other.example.com", "same-site", false)]
    [InlineData("null", null, false)]
    // No Origin: Sec-Fetch-Site decides, and same-site needs a listed Origin.
    [InlineData(null, "same-origin", true)]
    [InlineData(null, "same-site", false)]
    [InlineData(null, "cross-site", false)]
    [InlineData(null, "none", false)]
    [InlineData(null, "unknown-future-value", false)]
    // Neither header.
    [InlineData(null, null, false)]
    public void should_apply_the_decision_table(string? origin, string? fetchSite, bool expected)
    {
        var request = _Request(origin, fetchSite);

        SameOriginRequestValidator.IsSameOrigin(request, _Options).Should().Be(expected);
    }

    [Theory]
    [InlineData("HTTPS://API.EXAMPLE.COM")]
    [InlineData("https://api.example.com")]
    public void should_match_the_own_origin_case_insensitively(string origin)
    {
        SameOriginRequestValidator.IsSameOrigin(_Request(origin, null), _Options).Should().BeTrue();
    }

    [Fact]
    public void should_match_the_own_origin_when_the_host_header_spells_out_the_default_port()
    {
        var request = _Request(_Self, null);
        request.Host = new HostString("api.example.com:443");

        SameOriginRequestValidator.IsSameOrigin(request, _Options).Should().BeTrue();
    }

    [Fact]
    public void should_keep_a_non_default_port_in_the_own_origin()
    {
        var request = _Request("https://api.example.com:8443", null);
        request.Host = new HostString("api.example.com:8443");

        SameOriginRequestValidator.IsSameOrigin(request, _Options).Should().BeTrue();
        SameOriginRequestValidator
            .IsSameOrigin(_Request("https://api.example.com:8443", null), _Options)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void should_refuse_an_own_origin_on_another_scheme()
    {
        SameOriginRequestValidator.IsSameOrigin(_Request("http://api.example.com", null), _Options).Should().BeFalse();
    }

    [Fact]
    public void should_refuse_a_repeated_origin_or_fetch_site_header()
    {
        var repeatedOrigin = _Request(null, "same-origin");
        repeatedOrigin.Headers.Origin = new([_Self, _Self]);
        var repeatedSite = _Request(_Self, null);
        repeatedSite.Headers["Sec-Fetch-Site"] = new(["same-origin", "same-origin"]);

        SameOriginRequestValidator.IsSameOrigin(repeatedOrigin, _Options).Should().BeFalse();
        SameOriginRequestValidator.IsSameOrigin(repeatedSite, _Options).Should().BeFalse();
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("  ", false)]
    [InlineData("XMLHttpRequest", true)]
    public void should_require_the_configured_header_with_a_value(string? value, bool expected)
    {
        var options = new HeadlessSameOriginOptions { RequiredHeaderName = "X-Requested-With" };
        var request = _Request(_Self, "same-origin");

        if (value is not null)
        {
            request.Headers.XRequestedWith = value;
        }

        SameOriginRequestValidator.IsSameOrigin(request, options).Should().Be(expected);
    }

    private static HttpRequest _Request(string? origin, string? fetchSite)
    {
        var request = new DefaultHttpContext().Request;
        request.Scheme = "https";
        request.Host = new HostString("api.example.com");

        if (origin is not null)
        {
            request.Headers.Origin = origin;
        }

        if (fetchSite is not null)
        {
            request.Headers["Sec-Fetch-Site"] = fetchSite;
        }

        return request;
    }
}
