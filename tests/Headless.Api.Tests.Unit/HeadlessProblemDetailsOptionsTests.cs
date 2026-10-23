// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Api;
using Headless.Context;
using Headless.Primitives;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class HeadlessProblemDetailsOptionsTests : TestBase
{
    [Fact]
    public void should_refuse_to_map_an_exception_the_framework_already_maps()
    {
        // given
        var options = new HeadlessProblemDetailsOptions();

        // when
        var mapConflict = () => options.MapException<ConflictException>(StatusCodes.Status400BadRequest);
        var mapDerivedTimeout = () =>
            options.MapException<SlowDependencyException>(StatusCodes.Status503ServiceUnavailable);

        // then - a derived type is refused too: the handler's own case would catch it first
        mapConflict.Should().Throw<InvalidOperationException>().WithMessage("*ConflictException*");
        mapDerivedTimeout.Should().Throw<InvalidOperationException>().WithMessage("*SlowDependencyException*");
    }

    [Fact]
    public void should_refuse_to_map_an_error_the_framework_already_maps()
    {
        // given
        var options = new HeadlessProblemDetailsOptions();

        // when
        var mapNotFound = () => options.MapError<NotFoundError>(StatusCodes.Status410Gone);

        // then
        mapNotFound.Should().Throw<InvalidOperationException>().WithMessage("*NotFoundError*");
    }

    [Theory]
    [InlineData(200)]
    [InlineData(399)]
    [InlineData(600)]
    public void should_refuse_a_status_that_is_not_an_error(int statusCode)
    {
        // given
        var options = new HeadlessProblemDetailsOptions();

        // when
        var mapException = () => options.MapException<QuotaException>(statusCode);
        var mapError = () => options.MapError<QuotaError>(statusCode);

        // then
        mapException.Should().Throw<ArgumentOutOfRangeException>();
        mapError.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void should_use_the_most_derived_mapping_and_let_a_later_registration_replace_an_earlier_one()
    {
        // given
        var options = new HeadlessProblemDetailsOptions()
            .MapError<QuotaError>(StatusCodes.Status409Conflict)
            .MapError<QuotaError>(StatusCodes.Status429TooManyRequests)
            .MapError<DailyQuotaError>(StatusCodes.Status403Forbidden);

        // when
        options.TryGetStatusCode(new QuotaError(), out var quotaStatus).Should().BeTrue();
        options.TryGetStatusCode(new DailyQuotaError(), out var dailyStatus).Should().BeTrue();

        // then
        quotaStatus.Should().Be(StatusCodes.Status429TooManyRequests);
        dailyStatus.Should().Be(StatusCodes.Status403Forbidden);
        options
            .MappedStatusCodes.Should()
            .BeEquivalentTo([StatusCodes.Status429TooManyRequests, StatusCodes.Status403Forbidden]);
    }

    [Fact]
    public void should_add_up_the_mappings_of_every_registration_call()
    {
        // given - AddHeadless() registers the problem-details services without mappings; the application adds its own
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Substitute.For<IBuildInformationAccessor>());
        services.AddHttpContextAccessor();
        services.AddHeadlessProblemDetails();
        services.AddHeadlessProblemDetails(problems =>
            problems.MapError<QuotaError>(StatusCodes.Status429TooManyRequests)
        );
        services.AddHeadlessProblemDetails(problems =>
            problems.MapException<QuotaException>(StatusCodes.Status402PaymentRequired)
        );

        // when
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<HeadlessProblemDetailsOptions>>().Value;

        // then
        options
            .MappedStatusCodes.Should()
            .BeEquivalentTo([StatusCodes.Status429TooManyRequests, StatusCodes.Status402PaymentRequired]);
    }

    [Fact]
    public void should_build_a_mapped_error_with_its_status_and_descriptor()
    {
        // given
        var creator = _CreateCreator();
        var options = new HeadlessProblemDetailsOptions().MapError<QuotaError>(StatusCodes.Status429TooManyRequests);
        var error = new QuotaError();

        // when
        var status = ApiResultErrorProblemDetails.GetStatusCode(error, options);
        var problem = ApiResultErrorProblemDetails.Create(error, creator, options);

        // then
        status.Should().Be(StatusCodes.Status429TooManyRequests);
        problem.Status.Should().Be(StatusCodes.Status429TooManyRequests);
        problem.Detail.Should().Be("The quota is used up.");
        problem.Extensions["error"].Should().BeOfType<ErrorDescriptor>().Which.Code.Should().Be("app:quota");
    }

    [Fact]
    public void should_report_no_status_for_an_unknown_error_until_the_mappings_are_known()
    {
        // when
        var unknown = ApiResultErrorProblemDetails.GetStatusCode(new QuotaError(), mappings: null);
        var known = ApiResultErrorProblemDetails.GetStatusCode(
            new ConflictError("app:taken", "Taken."),
            mappings: null
        );
        var unmapped = ApiResultErrorProblemDetails.GetStatusCode(
            new QuotaError(),
            new HeadlessProblemDetailsOptions()
        );

        // then
        unknown.Should().BeNull();
        known.Should().Be(StatusCodes.Status409Conflict);
        unmapped.Should().Be(StatusCodes.Status409Conflict);
    }

    private static IProblemDetailsCreator _CreateCreator()
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Substitute.For<IBuildInformationAccessor>());
        services.AddHttpContextAccessor();
        services.AddHeadlessProblemDetails();

        return services.BuildServiceProvider().GetRequiredService<IProblemDetailsCreator>();
    }

    private sealed class SlowDependencyException : TimeoutException;

    private sealed class QuotaException : Exception;

    private record QuotaError : ApiResultError
    {
        public override string Code => "app:quota";

        public override string Message => "The quota is used up.";
    }

    private sealed record DailyQuotaError : QuotaError;
}
