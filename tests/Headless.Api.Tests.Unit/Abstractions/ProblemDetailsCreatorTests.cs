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
        var result = creator.EntityNotFound();

        // then
        result.Status.Should().Be(StatusCodes.Status404NotFound);
        result.Title.Should().Be(HeadlessProblemDetailsConstants.Titles.EntityNotFound);
        result.Extensions.Should().NotContainKey("error");
    }

    [Fact]
    public void should_attach_supplied_error_to_entity_not_found()
    {
        // given
        var creator = _CreateCreator();
        var error = new ErrorDescriptor("custom-code", "custom description");

        // when
        var result = creator.EntityNotFound(error);

        // then
        result
            .Extensions.Should()
            .ContainKey("error")
            .WhoseValue.Should()
            .BeEquivalentTo(new ProblemErrorInfo(error.Code, error.Description));
    }

    [Fact]
    public void should_emit_default_detail_in_entity_not_found()
    {
        // given
        var creator = _CreateCreator();

        // when
        var result = creator.EntityNotFound();

        // then
        result.Detail.Should().Be(HeadlessProblemDetailsConstants.Details.EntityNotFound);
    }

    [Fact]
    public void should_create_bad_request_with_400()
    {
        // given
        var creator = _CreateCreator();

        // when
        var result = creator.BadRequest();

        // then
        result.Status.Should().Be(StatusCodes.Status400BadRequest);
        result.Title.Should().Be(HeadlessProblemDetailsConstants.Titles.BadRequest);
        result.Detail.Should().Be(HeadlessProblemDetailsConstants.Details.BadRequest);
        result.Extensions.Should().NotContainKey("error");
    }

    [Fact]
    public void should_use_supplied_detail_in_bad_request()
    {
        // given
        var creator = _CreateCreator();

        // when
        var result = creator.BadRequest(detail: "custom detail");

        // then
        result.Detail.Should().Be("custom detail");
    }

    [Fact]
    public void should_attach_supplied_error_to_bad_request()
    {
        // given
        var creator = _CreateCreator();
        var error = HeadlessProblemDetailsConstants.Errors.TenantContextRequired;

        // when
        var result = creator.BadRequest(error: error);

        // then
        result
            .Extensions.Should()
            .ContainKey("error")
            .WhoseValue.Should()
            .BeEquivalentTo(new ProblemErrorInfo(error.Code, error.Description));
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

    [Fact]
    public void should_normalize_bad_request_response_when_error_supplied()
    {
        // given
        var creator = _CreateCreator();

        // when
        var result = creator.BadRequest(
            detail: HeadlessProblemDetailsConstants.Details.TenantContextRequired,
            error: HeadlessProblemDetailsConstants.Errors.TenantContextRequired
        );

        // then - Normalize ran (traceId/buildNumber/commitNumber/timestamp present)
        result.Extensions.Should().ContainKey("traceId");
        result.Extensions.Should().ContainKey("buildNumber");
        result.Extensions.Should().ContainKey("commitNumber");
        result.Extensions.Should().ContainKey("timestamp");
    }

    [Fact]
    public void should_create_request_timeout_with_408()
    {
        // given
        var creator = _CreateCreator();

        // when
        var result = creator.RequestTimeout();

        // then
        result.Status.Should().Be(StatusCodes.Status408RequestTimeout);
        result.Title.Should().Be(HeadlessProblemDetailsConstants.Titles.RequestTimeout);
        result.Detail.Should().Be(HeadlessProblemDetailsConstants.Details.RequestTimeout);
        result.Extensions.Should().NotContainKey("errors");
        result.Extensions.Should().NotContainKey("error");
        result.Extensions.Should().ContainKey("traceId");
        result.Extensions.Should().ContainKey("buildNumber");
        result.Extensions.Should().ContainKey("commitNumber");
        result.Extensions.Should().ContainKey("timestamp");
    }

    [Fact]
    public void should_create_not_implemented_with_501()
    {
        // given
        var creator = _CreateCreator();

        // when
        var result = creator.NotImplemented();

        // then
        result.Status.Should().Be(StatusCodes.Status501NotImplemented);
        result.Title.Should().Be(HeadlessProblemDetailsConstants.Titles.NotImplemented);
        result.Detail.Should().Be(HeadlessProblemDetailsConstants.Details.NotImplemented);
        result.Extensions.Should().NotContainKey("errors");
        result.Extensions.Should().NotContainKey("error");
        result.Extensions.Should().ContainKey("traceId");
        result.Extensions.Should().ContainKey("buildNumber");
        result.Extensions.Should().ContainKey("commitNumber");
        result.Extensions.Should().ContainKey("timestamp");
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

    [Fact]
    public void should_fill_request_timeout_title_type_and_detail_for_408_when_unset()
    {
        // given - bare 408 (e.g., from RequestTimeoutsMiddleware via UseStatusCodePages)
        var creator = _CreateCreator();
        var problemDetails = new ProblemDetails { Status = 408 };

        // when
        creator.Normalize(problemDetails);

        // then
        problemDetails.Title.Should().Be(HeadlessProblemDetailsConstants.Titles.RequestTimeout);
        problemDetails.Type.Should().Be(HeadlessProblemDetailsConstants.Types.RequestTimeout);
        problemDetails.Detail.Should().Be(HeadlessProblemDetailsConstants.Details.RequestTimeout);
    }

    [Fact]
    public void should_preserve_existing_title_type_and_detail_for_408()
    {
        // given - factory-built 408 (TimeoutException path) already has fields set
        var creator = _CreateCreator();
        var problemDetails = new ProblemDetails
        {
            Status = 408,
            Title = "custom-title",
            Type = "custom-type",
            Detail = "custom-detail",
        };

        // when
        creator.Normalize(problemDetails);

        // then - Normalize must not overwrite caller-supplied values
        problemDetails.Title.Should().Be("custom-title");
        problemDetails.Type.Should().Be("custom-type");
        problemDetails.Detail.Should().Be("custom-detail");
    }

    [Fact]
    public void should_fill_not_implemented_title_type_and_detail_for_501_when_unset()
    {
        // given
        var creator = _CreateCreator();
        var problemDetails = new ProblemDetails { Status = 501 };

        // when
        creator.Normalize(problemDetails);

        // then
        problemDetails.Title.Should().Be(HeadlessProblemDetailsConstants.Titles.NotImplemented);
        problemDetails.Type.Should().Be(HeadlessProblemDetailsConstants.Types.NotImplemented);
        problemDetails.Detail.Should().Be(HeadlessProblemDetailsConstants.Details.NotImplemented);
    }

    [Fact]
    public void should_preserve_existing_title_type_and_detail_for_501()
    {
        // given
        var creator = _CreateCreator();
        var problemDetails = new ProblemDetails
        {
            Status = 501,
            Title = "custom-title",
            Type = "custom-type",
            Detail = "custom-detail",
        };

        // when
        creator.Normalize(problemDetails);

        // then
        problemDetails.Title.Should().Be("custom-title");
        problemDetails.Type.Should().Be("custom-type");
        problemDetails.Detail.Should().Be("custom-detail");
    }

    #endregion
}
