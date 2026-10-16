// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Headless.Context;
using Headless.Primitives;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class ApiResultErrorProblemDetailsTests : TestBase
{
    [Fact]
    public void should_report_the_same_status_the_problem_body_carries_for_every_error_kind()
    {
        // given
        var creator = _CreateCreator(localizer: null);
        ApiResultError[] errors =
        [
            new NotFoundError { Entity = "Order", Key = "1" },
            ValidationError.FromFields(("name", "Name is required")),
            new ForbiddenError(new ErrorDescriptor("app:no_access", "No access.")),
            UnauthorizedError.Instance,
            new AggregateError { Errors = [ValidationError.FromFields(("name", "Name is required"))] },
            new AggregateError
            {
                Errors = [ValidationError.FromFields(("name", "Name is required")), new ConflictError("a", "b")],
            },
            new ConflictError("app:taken", "Taken."),
            ApiResultError.Custom("app:custom", "Custom."),
        ];

        foreach (var error in errors)
        {
            // when
            var status = ApiResultErrorProblemDetails.GetStatusCode(error);
            var problem = ApiResultErrorProblemDetails.Create(error, creator);

            // then
            problem.Status.Should().Be(status, "the status of {0} must match its body", error.GetType().Name);
        }
    }

    [Fact]
    public void should_use_the_forbidden_error_description_as_the_detail()
    {
        // given
        var creator = _CreateCreator(localizer: null);
        var error = new ForbiddenError(new ErrorDescriptor("app:no_access", "Only owners can archive."));

        // when
        var problem = ApiResultErrorProblemDetails.Create(error, creator);

        // then
        problem.Status.Should().Be(StatusCodes.Status403Forbidden);
        problem.Detail.Should().Be("Only owners can archive.");
    }

    [Fact]
    public void should_use_the_localized_description_as_the_forbidden_detail()
    {
        // given
        var creator = _CreateCreator(new NoAccessLocalizer());
        var error = new ForbiddenError(new ErrorDescriptor("app:no_access", "Only owners can archive."));

        // when
        var problem = ApiResultErrorProblemDetails.Create(error, creator);

        // then
        problem.Detail.Should().Be("المالك فقط يمكنه الأرشفة");
        problem.Extensions["error"].Should().BeOfType<ErrorDescriptor>().Which.Code.Should().Be("app:no_access");
    }

    [Fact]
    public void should_use_a_carried_unauthorized_description_as_the_detail()
    {
        // given
        var creator = _CreateCreator(localizer: null);
        var error = new UnauthorizedError(new ErrorDescriptor("auth:expired", "Session expired."));

        // when
        var problem = ApiResultErrorProblemDetails.Create(error, creator);

        // then
        problem.Status.Should().Be(StatusCodes.Status401Unauthorized);
        problem.Detail.Should().Be("Session expired.");
    }

    [Fact]
    public void should_keep_the_default_detail_for_an_unauthorized_error_without_a_descriptor()
    {
        // given
        var creator = _CreateCreator(localizer: null);

        // when
        var problem = ApiResultErrorProblemDetails.Create(UnauthorizedError.Instance, creator);

        // then
        problem.Detail.Should().Be(creator.Unauthorized().Detail);
        problem.Extensions.Should().NotContainKey("error");
    }

    private static IProblemDetailsCreator _CreateCreator(IErrorDescriptionLocalizer? localizer)
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Substitute.For<IBuildInformationAccessor>());
        services.AddHttpContextAccessor();

        if (localizer is not null)
        {
            services.AddSingleton(localizer);
        }

        services.AddHeadlessProblemDetails();

        return services.BuildServiceProvider().GetRequiredService<IProblemDetailsCreator>();
    }

    private sealed class NoAccessLocalizer : IErrorDescriptionLocalizer
    {
        public string? Localize(ErrorDescriptor error) =>
            string.Equals(error.Code, "app:no_access", StringComparison.Ordinal) ? "المالك فقط يمكنه الأرشفة" : null;
    }
}
