// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Api;
using Headless.Constants;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Tests.Extensions;

public sealed class EndpointsExtensionsTests : TestBase
{
    [Fact]
    public void should_preserve_path_and_query_on_configured_redirect_host()
    {
        // given
        var mainHost = new Uri("https://www.example.com");
        var path = new PathString("/docs/search");
        var query = new QueryString("?q=Headless&returnUrl=https%3A%2F%2Fevil.example");

        // when
        var result = EndpointsExtensions.BuildRedirectUri(mainHost, path, query);

        // then
        result
            .AbsoluteUri.Should()
            .Be("https://www.example.com/docs/search?q=Headless&returnUrl=https%3A%2F%2Fevil.example");
    }

    [Fact]
    public void should_keep_protocol_relative_paths_on_configured_redirect_host()
    {
        // given
        var mainHost = new Uri("https://www.example.com");
        var path = new PathString("//evil.example/login");
        var query = new QueryString("?continue=https%3A%2F%2Fevil.example");

        // when
        var result = EndpointsExtensions.BuildRedirectUri(mainHost, path, query);

        // then
        result.Scheme.Should().Be("https");
        result.Host.Should().Be("www.example.com");
        result
            .AbsoluteUri.Should()
            .Be("https://www.example.com//evil.example/login?continue=https%3A%2F%2Fevil.example");
    }

    [Fact]
    public void should_preserve_configured_non_default_port()
    {
        // given
        var mainHost = new Uri("https://www.example.com:8443");
        var path = new PathString("/health");
        var query = QueryString.Empty;

        // when
        var result = EndpointsExtensions.BuildRedirectUri(mainHost, path, query);

        // then
        result.AbsoluteUri.Should().Be("https://www.example.com:8443/health");
    }

    [Fact]
    public void should_return_bad_request_problem_details_when_host_does_not_match()
    {
        // given - a manufactured mismatched-host pair so the helper's open-redirect guard fires.
        // Use the URI-overload of the helper so the test can synthesize the mismatch directly
        // without depending on BuildRedirectUri's structural output.
        var mainHost = new Uri("https://www.example.com");
        var attackerRedirect = new Uri("https://attacker.example/login");
        var creator = Substitute.For<IProblemDetailsCreator>();
        var canonicalBadRequest = new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = HeadlessProblemDetailsConstants.Titles.BadRequest,
            Type = HeadlessProblemDetailsConstants.Types.BadRequest,
            Detail = HeadlessProblemDetailsConstants.Details.BadRequest,
        };
        creator.BadRequest().Returns(canonicalBadRequest);

        // when
        var result = EndpointsExtensions.BuildRedirectResultOrBadRequest(attackerRedirect, mainHost, creator);

        // then - the helper returns a ProblemHttpResult carrying the framework's canonical 400 ProblemDetails
        var problemResult = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problemResult.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        problemResult.ProblemDetails.Status.Should().Be(StatusCodes.Status400BadRequest);
        problemResult.ProblemDetails.Title.Should().Be(HeadlessProblemDetailsConstants.Titles.BadRequest);
        creator.Received(1).BadRequest();
    }

    [Fact]
    public void should_return_permanent_redirect_when_host_matches()
    {
        // given - the helper's happy path: redirectUri and mainHostBaseUri agree on scheme + host + port
        var mainHost = new Uri("https://www.example.com");
        var redirectUri = new Uri("https://www.example.com/docs/search?q=x");
        var creator = Substitute.For<IProblemDetailsCreator>();

        // when
        var result = EndpointsExtensions.BuildRedirectResultOrBadRequest(redirectUri, mainHost, creator);

        // then - permanent redirect; ProblemDetails creator must NOT have been touched
        var redirectResult = result.Should().BeAssignableTo<IResult>().Subject;
        redirectResult.Should().NotBeOfType<ProblemHttpResult>();
        creator.DidNotReceive().BadRequest(Arg.Any<string?>(), Arg.Any<Headless.Primitives.ErrorDescriptor?>());
    }
}
