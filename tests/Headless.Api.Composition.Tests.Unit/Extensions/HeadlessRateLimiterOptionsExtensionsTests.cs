// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Threading.RateLimiting;
using Headless.Abstractions;
using Headless.Api.Resources;
using Headless.Primitives;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Extensions;

public sealed class HeadlessRateLimiterOptionsExtensionsTests : TestBase
{
    [Fact]
    public void should_set_429_rejection_status_code()
    {
        // given
        var options = new RateLimiterOptions();

        // when
        var result = options.UseHeadlessProblemDetails();

        // then
        result.Should().BeSameAs(options);
        options.RejectionStatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
        options.OnRejected.Should().NotBeNull();
    }

    [Theory]
    [InlineData(1500, 2)]
    [InlineData(60_000, 60)]
    [InlineData(1, 1)]
    public async Task should_write_problem_details_with_retry_after_rounded_up_from_the_lease(
        int retryAfterMilliseconds,
        int expectedSeconds
    )
    {
        // given
        var creator = _CreateProblemDetailsCreator();
        var context = _CreateContext(creator);
        using var lease = new MetadataLease(TimeSpan.FromMilliseconds(retryAfterMilliseconds));

        // when
        await _RejectAsync(context, lease);

        // then
        context.Response.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
        context
            .Response.Headers.RetryAfter.ToString()
            .Should()
            .Be(expectedSeconds.ToString(CultureInfo.InvariantCulture));
        context.Response.Headers.CacheControl.ToString().Should().Be("no-store");
        context.Response.ContentType.Should().StartWith("application/problem+json");
        creator
            .Received(1)
            .TooManyRequests(
                expectedSeconds,
                Arg.Is<ErrorDescriptor>(error => error.Code == GeneralErrorCodes.RateLimitExceeded)
            );

        using var body = await _ReadBodyAsync(context);
        body.RootElement.GetProperty("status").GetInt32().Should().Be(StatusCodes.Status429TooManyRequests);
    }

    [Fact]
    public async Task should_fall_back_to_one_second_when_the_lease_has_no_retry_after()
    {
        // given
        var creator = _CreateProblemDetailsCreator();
        var context = _CreateContext(creator);
        await using var limiter = new ConcurrencyLimiter(
            new ConcurrencyLimiterOptions { PermitLimit = 1, QueueLimit = 0 }
        );
        using var held = limiter.AttemptAcquire();
        using var rejected = limiter.AttemptAcquire();

        // when
        await _RejectAsync(context, rejected);

        // then
        rejected.IsAcquired.Should().BeFalse();
        context.Response.Headers.RetryAfter.ToString().Should().Be("1");
        creator.Received(1).TooManyRequests(1, Arg.Any<ErrorDescriptor>());
    }

    [Fact]
    public async Task should_use_the_retry_after_a_real_window_limiter_publishes()
    {
        // given
        var creator = _CreateProblemDetailsCreator();
        var context = _CreateContext(creator);
        await using var limiter = new FixedWindowRateLimiter(
            new FixedWindowRateLimiterOptions
            {
                PermitLimit = 1,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = false,
            }
        );
        using var first = limiter.AttemptAcquire();
        using var rejected = limiter.AttemptAcquire();

        // when
        await _RejectAsync(context, rejected);

        // then
        rejected.IsAcquired.Should().BeFalse();
        context.Response.Headers.RetryAfter.ToString().Should().Be("60");
    }

    [Fact]
    public async Task should_not_touch_the_response_when_it_has_already_started()
    {
        // given
        var creator = _CreateProblemDetailsCreator();
        var context = _CreateContext(creator);
        context.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());
        using var lease = new MetadataLease(TimeSpan.FromSeconds(5));

        // when
        await _RejectAsync(context, lease);

        // then
        context.Response.Headers.RetryAfter.Should().BeEmpty();
        creator.DidNotReceiveWithAnyArgs().TooManyRequests(default);
    }

    private static async Task _RejectAsync(HttpContext context, RateLimitLease lease)
    {
        var options = new RateLimiterOptions().UseHeadlessProblemDetails();

        await options.OnRejected!(new OnRejectedContext { HttpContext = context, Lease = lease }, AbortToken);
    }

    private static IProblemDetailsCreator _CreateProblemDetailsCreator()
    {
        var creator = Substitute.For<IProblemDetailsCreator>();

        creator
            .TooManyRequests(Arg.Any<int>(), Arg.Any<ErrorDescriptor?>())
            .Returns(call => new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Extensions = { ["retryAfter"] = call.ArgAt<int>(0) },
            });

        return creator;
    }

    private static DefaultHttpContext _CreateContext(IProblemDetailsCreator creator)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection()
                .AddLogging()
                .AddProblemDetails()
                .AddSingleton(creator)
                .BuildServiceProvider(),
        };
        context.Response.Body = new MemoryStream();

        return context;
    }

    private static async Task<JsonDocument> _ReadBodyAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;

        return await JsonDocument.ParseAsync(context.Response.Body, cancellationToken: AbortToken);
    }

    private sealed class MetadataLease(TimeSpan retryAfter) : RateLimitLease
    {
        public override bool IsAcquired => false;

        public override IEnumerable<string> MetadataNames => [MetadataName.RetryAfter.Name];

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            metadata = string.Equals(metadataName, MetadataName.RetryAfter.Name, StringComparison.Ordinal)
                ? retryAfter
                : null;

            return metadata is not null;
        }
    }

    private sealed class StartedResponseFeature : IHttpResponseFeature
    {
        public int StatusCode { get; set; } = 200;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = Stream.Null;
        public bool HasStarted => true;

        public void OnStarting(Func<object, Task> callback, object state) { }

        public void OnCompleted(Func<object, Task> callback, object state) { }
    }
}
