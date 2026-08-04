// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Filters;
using Headless.Api.Middlewares;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Tests.Middlewares;

public sealed class RedirectToCanonicalUrlRuleTests : TestBase
{
    #region Helper Methods

    private static RedirectToCanonicalUrlRule _CreateRule(bool appendTrailingSlash = true, bool lowercaseUrls = true)
    {
        return new RedirectToCanonicalUrlRule(appendTrailingSlash, lowercaseUrls);
    }

    /// <summary>
    /// Builds a rewrite context. <paramref name="routed"/> models whether <c>UseRouting()</c> has already matched
    /// the request: only then does endpoint metadata (and with it the opt-out attributes) exist.
    /// </summary>
    private static RewriteContext _CreateRewriteContext(
        string path,
        string? queryString = null,
        string method = "GET",
        EndpointMetadataCollection? metadata = null,
        bool routed = true
    )
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = path;
        httpContext.Request.Method = method;
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("example.com");
        if (queryString is not null)
        {
            httpContext.Request.QueryString = new QueryString(queryString);
        }

        if (routed)
        {
            var endpoint = new Endpoint(null, metadata ?? EndpointMetadataCollection.Empty, "test");
            httpContext.SetEndpoint(endpoint);
        }

        return new RewriteContext { HttpContext = httpContext };
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void should_throw_when_options_null()
    {
        // given
        IOptions<RouteOptions>? options = null;

        // when
        var act = () => new RedirectToCanonicalUrlRule(options!);

        // then
        act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("optionsAccessor");
    }

    [Fact]
    public void should_use_options_values()
    {
        // given
        var routeOptions = new RouteOptions { AppendTrailingSlash = true, LowercaseUrls = false };
        var options = Options.Create(routeOptions);

        // when
        var rule = new RedirectToCanonicalUrlRule(options);

        // then
        rule.AppendTrailingSlash.Should().BeTrue();
        rule.LowercaseUrls.Should().BeFalse();
    }

    [Fact]
    public void should_accept_explicit_values()
    {
        // given / when
        var rule = new RedirectToCanonicalUrlRule(appendTrailingSlash: false, lowercaseUrls: true);

        // then
        rule.AppendTrailingSlash.Should().BeFalse();
        rule.LowercaseUrls.Should().BeTrue();
    }

    #endregion

    #region Trailing Slash Tests (AppendTrailingSlash=true)

    [Fact]
    public void should_append_trailing_slash()
    {
        // given
        var rule = _CreateRule(appendTrailingSlash: true, lowercaseUrls: false);
        var context = _CreateRewriteContext("/api/users");

        // when
        rule.ApplyRule(context);

        // then
        context.HttpContext.Response.StatusCode.Should().Be(StatusCodes.Status301MovedPermanently);
        context.Result.Should().Be(RuleResult.EndResponse);
        context.HttpContext.Response.Headers.Location.ToString().Should().Be("https://example.com/api/users/");
    }

    [Fact]
    public void should_not_modify_when_already_has_slash()
    {
        // given
        var rule = _CreateRule(appendTrailingSlash: true, lowercaseUrls: false);
        var context = _CreateRewriteContext("/api/users/");

        // when
        rule.ApplyRule(context);

        // then
        context.HttpContext.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        context.Result.Should().NotBe(RuleResult.EndResponse);
        context.HttpContext.Response.Headers.Location.Should().BeEmpty();
    }

    [Fact]
    public void should_not_modify_home_page()
    {
        // given
        var rule = _CreateRule(appendTrailingSlash: true, lowercaseUrls: false);
        var context = _CreateRewriteContext("/");

        // when
        rule.ApplyRule(context);

        // then
        context.HttpContext.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        context.Result.Should().NotBe(RuleResult.EndResponse);
        context.HttpContext.Response.Headers.Location.Should().BeEmpty();
    }

    [Fact]
    public void should_respect_no_trailing_slash_attribute()
    {
        // given
        var rule = _CreateRule(appendTrailingSlash: true, lowercaseUrls: false);
        var metadata = new EndpointMetadataCollection(new NoTrailingSlashAttribute());
        var context = _CreateRewriteContext("/api/users", metadata: metadata);

        // when
        rule.ApplyRule(context);

        // then
        context.HttpContext.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        context.Result.Should().NotBe(RuleResult.EndResponse);
        context.HttpContext.Response.Headers.Location.Should().BeEmpty();
    }

    #endregion

    #region Trailing Slash Tests (AppendTrailingSlash=false)

    [Fact]
    public void should_strip_trailing_slash()
    {
        // given
        var rule = _CreateRule(appendTrailingSlash: false, lowercaseUrls: false);
        var context = _CreateRewriteContext("/api/users/");

        // when
        rule.ApplyRule(context);

        // then
        context.HttpContext.Response.StatusCode.Should().Be(StatusCodes.Status301MovedPermanently);
        context.Result.Should().Be(RuleResult.EndResponse);
        context.HttpContext.Response.Headers.Location.ToString().Should().Be("https://example.com/api/users");
    }

    [Fact]
    public void should_not_modify_when_no_slash()
    {
        // given
        var rule = _CreateRule(appendTrailingSlash: false, lowercaseUrls: false);
        var context = _CreateRewriteContext("/api/users");

        // when
        rule.ApplyRule(context);

        // then
        context.HttpContext.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        context.Result.Should().NotBe(RuleResult.EndResponse);
        context.HttpContext.Response.Headers.Location.Should().BeEmpty();
    }

    #endregion

    #region Lowercase Tests (LowercaseUrls=true)

    [Fact]
    public void should_lowercase_path()
    {
        // given
        var rule = _CreateRule(appendTrailingSlash: false, lowercaseUrls: true);
        var context = _CreateRewriteContext("/API/Users");

        // when
        rule.ApplyRule(context);

        // then
        context.HttpContext.Response.StatusCode.Should().Be(StatusCodes.Status301MovedPermanently);
        context.Result.Should().Be(RuleResult.EndResponse);
        context.HttpContext.Response.Headers.Location.ToString().Should().Be("https://example.com/api/users");
    }

    [Fact]
    public void should_lowercase_query_string()
    {
        // given
        var rule = _CreateRule(appendTrailingSlash: false, lowercaseUrls: true);
        var context = _CreateRewriteContext("/api/users", queryString: "?Name=John");

        // when
        rule.ApplyRule(context);

        // then
        context.HttpContext.Response.StatusCode.Should().Be(StatusCodes.Status301MovedPermanently);
        context.Result.Should().Be(RuleResult.EndResponse);
        context.HttpContext.Response.Headers.Location.ToString().Should().Be("https://example.com/api/users?name=john");
    }

    [Fact]
    public void should_not_modify_already_lowercase()
    {
        // given
        var rule = _CreateRule(appendTrailingSlash: false, lowercaseUrls: true);
        var context = _CreateRewriteContext("/api/users");

        // when
        rule.ApplyRule(context);

        // then
        context.HttpContext.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        context.Result.Should().NotBe(RuleResult.EndResponse);
        context.HttpContext.Response.Headers.Location.Should().BeEmpty();
    }

    [Fact]
    public void should_respect_no_lowercase_query_string_attribute()
    {
        // given
        var rule = _CreateRule(appendTrailingSlash: false, lowercaseUrls: true);
        var metadata = new EndpointMetadataCollection(new NoLowercaseQueryStringAttribute());
        var context = _CreateRewriteContext("/api/users", queryString: "?Name=John", metadata: metadata);

        // when
        rule.ApplyRule(context);

        // then
        context.HttpContext.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        context.Result.Should().NotBe(RuleResult.EndResponse);
        context.HttpContext.Response.Headers.Location.Should().BeEmpty();
    }

    #endregion

    #region Combined Tests

    [Fact]
    public void should_apply_both_trailing_slash_and_lowercase()
    {
        // given
        var rule = _CreateRule(appendTrailingSlash: true, lowercaseUrls: true);
        var context = _CreateRewriteContext("/API/Users");

        // when
        rule.ApplyRule(context);

        // then
        context.HttpContext.Response.StatusCode.Should().Be(StatusCodes.Status301MovedPermanently);
        context.Result.Should().Be(RuleResult.EndResponse);
        context.HttpContext.Response.Headers.Location.ToString().Should().Be("https://example.com/api/users/");
    }

    [Fact]
    public void should_only_redirect_get_requests()
    {
        // given
        var rule = _CreateRule(appendTrailingSlash: true, lowercaseUrls: true);
        var context = _CreateRewriteContext("/API/Users", method: "POST");

        // when
        rule.ApplyRule(context);

        // then
        context.HttpContext.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        context.Result.Should().NotBe(RuleResult.EndResponse);
        context.HttpContext.Response.Headers.Location.Should().BeEmpty();
    }

    #endregion

    #region Endpoint Availability Tests

    // The opt-out attributes are only visible in endpoint metadata, which exists after UseRouting() has matched
    // the request. Registered before routing (the conventional UseRewriter placement) the rule must not redirect,
    // otherwise it silently overrides opt-outs it cannot see.

    [Fact]
    public void should_not_redirect_when_request_is_not_routed()
    {
        // given
        var rule = _CreateRule(appendTrailingSlash: true, lowercaseUrls: true);
        var context = _CreateRewriteContext("/API/Users", routed: false);

        // when
        rule.ApplyRule(context);

        // then
        context.HttpContext.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        context.Result.Should().NotBe(RuleResult.EndResponse);
        context.HttpContext.Response.Headers.Location.Should().BeEmpty();
    }

    [Fact]
    public void should_not_mutate_request_when_not_routed()
    {
        // given
        var rule = _CreateRule(appendTrailingSlash: true, lowercaseUrls: true);
        var context = _CreateRewriteContext("/API/Users", queryString: "?State=AbC123", routed: false);

        // when
        rule.ApplyRule(context);

        // then
        context.HttpContext.Request.Path.Value.Should().Be("/API/Users");
        context.HttpContext.Request.QueryString.Value.Should().Be("?State=AbC123");
    }

    [Fact]
    public void should_preserve_case_sensitive_query_string_when_not_routed()
    {
        // given — an OAuth callback whose state/code must survive verbatim
        var rule = _CreateRule(appendTrailingSlash: false, lowercaseUrls: true);
        var context = _CreateRewriteContext("/callback", queryString: "?state=AbC123&code=XyZ", routed: false);

        // when
        rule.ApplyRule(context);

        // then
        context.HttpContext.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        context.HttpContext.Response.Headers.Location.Should().BeEmpty();
        context.HttpContext.Request.QueryString.Value.Should().Be("?state=AbC123&code=XyZ");
    }

    [Fact]
    public void should_preserve_case_sensitive_query_string_when_routed_endpoint_opts_out()
    {
        // given
        var rule = _CreateRule(appendTrailingSlash: false, lowercaseUrls: true);
        var metadata = new EndpointMetadataCollection(new NoLowercaseQueryStringAttribute());
        var context = _CreateRewriteContext("/callback", queryString: "?state=AbC123&code=XyZ", metadata: metadata);

        // when
        rule.ApplyRule(context);

        // then
        context.HttpContext.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        context.HttpContext.Response.Headers.Location.Should().BeEmpty();
        context.HttpContext.Request.QueryString.Value.Should().Be("?state=AbC123&code=XyZ");
    }

    [Fact]
    public void should_redirect_when_routed_endpoint_does_not_opt_out()
    {
        // given
        var rule = _CreateRule(appendTrailingSlash: false, lowercaseUrls: true);
        var context = _CreateRewriteContext("/callback", queryString: "?state=AbC123&code=XyZ");

        // when
        rule.ApplyRule(context);

        // then
        context.HttpContext.Response.StatusCode.Should().Be(StatusCodes.Status301MovedPermanently);
        context.Result.Should().Be(RuleResult.EndResponse);
        context
            .HttpContext.Response.Headers.Location.ToString()
            .Should()
            .Be("https://example.com/callback?state=abc123&code=xyz");
    }

    [Fact]
    public void should_not_append_trailing_slash_when_not_routed()
    {
        // given
        var rule = _CreateRule(appendTrailingSlash: true, lowercaseUrls: false);
        var context = _CreateRewriteContext("/api/users", routed: false);

        // when
        rule.ApplyRule(context);

        // then
        context.HttpContext.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        context.HttpContext.Request.Path.Value.Should().Be("/api/users");
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void should_use_301_permanent_redirect()
    {
        // given
        var rule = _CreateRule(appendTrailingSlash: true, lowercaseUrls: false);
        var context = _CreateRewriteContext("/api/users");

        // when
        rule.ApplyRule(context);

        // then
        context.HttpContext.Response.StatusCode.Should().Be(StatusCodes.Status301MovedPermanently);
    }

    [Fact]
    public void should_set_end_response_result()
    {
        // given
        var rule = _CreateRule(appendTrailingSlash: true, lowercaseUrls: false);
        var context = _CreateRewriteContext("/api/users");

        // when
        rule.ApplyRule(context);

        // then
        context.Result.Should().Be(RuleResult.EndResponse);
    }

    [Fact]
    public void should_set_location_header()
    {
        // given
        var rule = _CreateRule(appendTrailingSlash: true, lowercaseUrls: false);
        var context = _CreateRewriteContext("/api/users");

        // when
        rule.ApplyRule(context);

        // then
        context.HttpContext.Response.Headers.Location.Should().NotBeEmpty();
        context.HttpContext.Response.Headers.Location.ToString().Should().Be("https://example.com/api/users/");
    }

    #endregion
}
