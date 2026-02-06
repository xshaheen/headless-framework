// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Abstractions;
using Headless.Api.Abstractions;
using Headless.Constants;
using Headless.Primitives;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Abstractions;

public sealed class ProblemDetailsCreatorTests : TestBase
{
    private static ProblemDetailsCreator _CreateCreator(
        TimeProvider? timeProvider = null,
        IBuildInformationAccessor? buildInfo = null,
        IHttpContextAccessor? httpContextAccessor = null
    )
    {
        timeProvider ??= new FakeTimeProvider(DateTimeOffset.UtcNow);

        if (buildInfo is null)
        {
            buildInfo = Substitute.For<IBuildInformationAccessor>();
            buildInfo.GetBuildNumber().Returns("1.0.0");
            buildInfo.GetCommitNumber().Returns("abc123");
        }

        if (httpContextAccessor is null)
        {
            httpContextAccessor = Substitute.For<IHttpContextAccessor>();
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Path = "/api/test";
            httpContextAccessor.HttpContext.Returns(httpContext);
        }

        var apiBehaviorOptions = Options.Create(new ApiBehaviorOptions());

        return new ProblemDetailsCreator(timeProvider, buildInfo, httpContextAccessor, apiBehaviorOptions);
    }

    #region Factory Methods

    [Fact]
    public void should_create_endpoint_not_found_with_404()
    {
        // given
        var creator = _CreateCreator();

        // when
        var result = creator.EndpointNotFound();

        // then
        result.Status.Should().Be(StatusCodes.Status404NotFound);
        result.Title.Should().Be(HeadlessProblemDetailsConstants.Titles.EndpointNotFound);
    }

    [Fact]
    public void should_include_request_path_in_endpoint_not_found()
    {
        // given
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/users/123";
        httpContextAccessor.HttpContext.Returns(httpContext);
        var creator = _CreateCreator(httpContextAccessor: httpContextAccessor);

        // when
        var result = creator.EndpointNotFound();

        // then
        result.Detail.Should().Contain("/api/users/123");
    }

    [Fact]
    public void should_create_entity_not_found_with_404()
    {
        // given
        var creator = _CreateCreator();

        // when
        var result = creator.EntityNotFound("User", "123");

        // then
        result.Status.Should().Be(StatusCodes.Status404NotFound);
        result.Title.Should().Be(HeadlessProblemDetailsConstants.Titles.EntityNotFound);
    }

    [Fact]
    public void should_not_expose_entity_or_key_in_entity_not_found()
    {
        // given
        var creator = _CreateCreator();

        // when
        var result = creator.EntityNotFound("User", "secret-id-123");

        // then
        result.Detail.Should().NotContain("User");
        result.Detail.Should().NotContain("secret-id-123");
        result.Detail.Should().Be(HeadlessProblemDetailsConstants.Details.EntityNotFound);
    }

    [Fact]
    public void should_create_malformed_syntax_with_400()
    {
        // given
        var creator = _CreateCreator();

        // when
        var result = creator.MalformedSyntax();

        // then
        result.Status.Should().Be(StatusCodes.Status400BadRequest);
        result.Title.Should().Be(HeadlessProblemDetailsConstants.Titles.BadRequest);
        result.Detail.Should().Be(HeadlessProblemDetailsConstants.Details.BadRequest);
    }

    [Fact]
    public void should_create_too_many_requests_with_429()
    {
        // given
        var creator = _CreateCreator();

        // when
        var result = creator.TooManyRequests(60);

        // then
        result.Status.Should().Be(StatusCodes.Status429TooManyRequests);
        result.Title.Should().Be(HeadlessProblemDetailsConstants.Titles.TooManyRequests);
        result.Detail.Should().Be(HeadlessProblemDetailsConstants.Details.TooManyRequests);
    }

    [Fact]
    public void should_include_retry_after_in_too_many_requests()
    {
        // given
        var creator = _CreateCreator();
        const int retryAfterSeconds = 120;

        // when
        var result = creator.TooManyRequests(retryAfterSeconds);

        // then
        result.Extensions.Should().ContainKey("retryAfter");
        result.Extensions["retryAfter"].Should().Be(retryAfterSeconds);
    }

    [Fact]
    public void should_create_unprocessable_entity_with_422()
    {
        // given
        var creator = _CreateCreator();
        var errors = new Dictionary<string, List<ErrorDescriptor>>(StringComparer.Ordinal)
        {
            ["Name"] = [new ErrorDescriptor("Name is required", "REQUIRED")],
        };

        // when
        var result = creator.UnprocessableEntity(errors);

        // then
        result.Status.Should().Be(StatusCodes.Status422UnprocessableEntity);
        result.Title.Should().Be(HeadlessProblemDetailsConstants.Titles.UnprocessableEntity);
        result.Detail.Should().Be(HeadlessProblemDetailsConstants.Details.UnprocessableEntity);
    }

    [Fact]
    public void should_include_errors_in_unprocessable_entity()
    {
        // given
        var creator = _CreateCreator();
        var errors = new Dictionary<string, List<ErrorDescriptor>>(StringComparer.Ordinal)
        {
            ["Email"] = [new ErrorDescriptor("Invalid email", "INVALID_EMAIL")],
            ["Age"] = [new ErrorDescriptor("Must be positive", "POSITIVE")],
        };

        // when
        var result = creator.UnprocessableEntity(errors);

        // then
        result.Extensions.Should().ContainKey("errors");
        result.Extensions["errors"].Should().BeEquivalentTo(errors);
    }

    [Fact]
    public void should_create_conflict_with_409()
    {
        // given
        var creator = _CreateCreator();
        var errors = new List<ErrorDescriptor> { new("Duplicate entry", "DUPLICATE") };

        // when
        var result = creator.Conflict(errors);

        // then
        result.Status.Should().Be(StatusCodes.Status409Conflict);
        result.Title.Should().Be(HeadlessProblemDetailsConstants.Titles.Conflict);
        result.Detail.Should().Be(HeadlessProblemDetailsConstants.Details.Conflict);
    }

    [Fact]
    public void should_include_errors_in_conflict()
    {
        // given
        var creator = _CreateCreator();
        var errors = new List<ErrorDescriptor>
        {
            new("Resource already exists", "EXISTS"),
            new("Cannot modify locked resource", "LOCKED"),
        };

        // when
        var result = creator.Conflict(errors);

        // then
        result.Extensions.Should().ContainKey("errors");
        result.Extensions["errors"].Should().BeEquivalentTo(errors);
    }

    [Fact]
    public void should_create_unauthorized_with_401()
    {
        // given
        var creator = _CreateCreator();

        // when
        var result = creator.Unauthorized();

        // then
        result.Status.Should().Be(StatusCodes.Status401Unauthorized);
        result.Title.Should().Be(HeadlessProblemDetailsConstants.Titles.Unauthorized);
        result.Detail.Should().Be(HeadlessProblemDetailsConstants.Details.Unauthorized);
    }

    [Fact]
    public void should_create_forbidden_with_403()
    {
        // given
        var creator = _CreateCreator();

        // when
        var result = creator.Forbidden([]);

        // then
        result.Status.Should().Be(StatusCodes.Status403Forbidden);
        result.Title.Should().Be(HeadlessProblemDetailsConstants.Titles.Forbidden);
        result.Detail.Should().Be(HeadlessProblemDetailsConstants.Details.Forbidden);
    }

    [Fact]
    public void should_include_errors_in_forbidden_when_provided()
    {
        // given
        var creator = _CreateCreator();
        var errors = new List<ErrorDescriptor> { new("Missing permission: admin", "PERMISSION") };

        // when
        var result = creator.Forbidden(errors);

        // then
        result.Extensions.Should().ContainKey("errors");
        result.Extensions["errors"].Should().BeEquivalentTo(errors);
    }

    [Fact]
    public void should_omit_errors_in_forbidden_when_empty()
    {
        // given
        var creator = _CreateCreator();

        // when
        var result = creator.Forbidden([]);

        // then
        result.Extensions.Should().NotContainKey("errors");
    }

    #endregion

    #region Normalize Tests

    [Fact]
    public void should_add_trace_id_from_activity()
    {
        // given
        var creator = _CreateCreator();
        var problemDetails = new ProblemDetails { Status = 400 };
        using var activity = new Activity("test-activity");
        activity.Start();

        // when
        creator.Normalize(problemDetails);

        // then
        problemDetails.Extensions.Should().ContainKey("traceId");
        problemDetails.Extensions["traceId"].Should().Be(activity.Id);
    }

    [Fact]
    public void should_add_trace_id_from_http_context_when_no_activity()
    {
        // given - ensure no activity is running
        Activity.Current?.Stop();

        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = new DefaultHttpContext { TraceIdentifier = "http-trace-123" };
        httpContext.Request.Path = "/api/test";
        httpContextAccessor.HttpContext.Returns(httpContext);
        var creator = _CreateCreator(httpContextAccessor: httpContextAccessor);
        var problemDetails = new ProblemDetails { Status = 400 };

        // when
        creator.Normalize(problemDetails);

        // then
        problemDetails.Extensions.Should().ContainKey("traceId");
        problemDetails.Extensions["traceId"].Should().Be("http-trace-123");
    }

    [Fact]
    public void should_add_build_number()
    {
        // given
        var buildInfo = Substitute.For<IBuildInformationAccessor>();
        buildInfo.GetBuildNumber().Returns("2.5.0");
        buildInfo.GetCommitNumber().Returns("xyz789");
        var creator = _CreateCreator(buildInfo: buildInfo);
        var problemDetails = new ProblemDetails { Status = 400 };

        // when
        creator.Normalize(problemDetails);

        // then
        problemDetails.Extensions.Should().ContainKey("buildNumber");
        problemDetails.Extensions["buildNumber"].Should().Be("2.5.0");
    }

    [Fact]
    public void should_add_commit_number()
    {
        // given
        var buildInfo = Substitute.For<IBuildInformationAccessor>();
        buildInfo.GetBuildNumber().Returns("1.0.0");
        buildInfo.GetCommitNumber().Returns("def456");
        var creator = _CreateCreator(buildInfo: buildInfo);
        var problemDetails = new ProblemDetails { Status = 400 };

        // when
        creator.Normalize(problemDetails);

        // then
        problemDetails.Extensions.Should().ContainKey("commitNumber");
        problemDetails.Extensions["commitNumber"].Should().Be("def456");
    }

    [Fact]
    public void should_add_timestamp_in_iso_format()
    {
        // given
        var fixedTime = new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(fixedTime);
        var creator = _CreateCreator(timeProvider: timeProvider);
        var problemDetails = new ProblemDetails { Status = 400 };

        // when
        creator.Normalize(problemDetails);

        // then
        problemDetails.Extensions.Should().ContainKey("timestamp");
        problemDetails.Extensions["timestamp"].Should().Be(fixedTime.ToString("O"));
    }

    [Fact]
    public void should_set_instance_from_request_path()
    {
        // given
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/orders/42";
        httpContextAccessor.HttpContext.Returns(httpContext);
        var creator = _CreateCreator(httpContextAccessor: httpContextAccessor);
        var problemDetails = new ProblemDetails { Status = 400 };

        // when
        creator.Normalize(problemDetails);

        // then
        problemDetails.Instance.Should().Be("/api/orders/42");
    }

    [Fact]
    public void should_set_internal_error_title_for_500()
    {
        // given
        var creator = _CreateCreator();
        var problemDetails = new ProblemDetails { Status = 500, Title = "Some other title" };

        // when
        creator.Normalize(problemDetails);

        // then
        problemDetails.Title.Should().Be(HeadlessProblemDetailsConstants.Titles.InternalError);
        problemDetails.Detail.Should().Be(HeadlessProblemDetailsConstants.Details.InternalError);
    }

    [Fact]
    public void should_not_overwrite_existing_trace_id()
    {
        // given
        var creator = _CreateCreator();
        var problemDetails = new ProblemDetails { Status = 400, Extensions = { ["traceId"] = "existing-trace-id" } };

        // when
        creator.Normalize(problemDetails);

        // then
        problemDetails.Extensions["traceId"].Should().Be("existing-trace-id");
    }

    #endregion
}
