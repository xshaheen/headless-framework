// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Dashboard;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Headless.RateLimiting;

/// <summary>
/// The Rate limits module's own read API at <c>{base}/api/rate-limiting</c>: the declared purposes, and a lookup of one
/// subject's current window.
/// </summary>
internal sealed class RateLimitingApi(
    IAttemptLimiter limiter,
    AttemptSubjectFingerprinter fingerprinter,
    AttemptLimiterOptions limiterOptions,
    RateLimitingCatalogue catalogue
)
{
    private static readonly RateLimitingLookupBodyValidator _LookupValidator = new();

    private readonly IAttemptLimiter _limiter = Argument.IsNotNull(limiter);
    private readonly AttemptSubjectFingerprinter _fingerprinter = Argument.IsNotNull(fingerprinter);
    private readonly AttemptLimiterOptions _limiterOptions = Argument.IsNotNull(limiterOptions);
    private readonly RateLimitingCatalogue _catalogue = Argument.IsNotNull(catalogue);

    public void Map(DashboardModuleEndpoints endpoints)
    {
        var routes = endpoints.Routes;

        routes.MapGet("/catalogue", _GetCatalogue);
        // A lookup reads and changes nothing; it is a POST so the subject (an email or a phone number) stays out of
        // URLs and request logs.
        routes.MapPost("/lookup", _LookupAsync);
    }

    private JsonHttpResult<RateLimitingCatalogue> _GetCatalogue() =>
        TypedResults.Json(_catalogue, RateLimitingDashboardJsonContext.Default.RateLimitingCatalogue);

    private async Task<
        Results<JsonHttpResult<RateLimitingLookupView>, ValidationProblem, ProblemHttpResult>
    > _LookupAsync(HttpContext context, CancellationToken cancellationToken)
    {
        RateLimitingLookupBody? body;

        try
        {
            body = await JsonSerializer
                .DeserializeAsync(
                    context.Request.Body,
                    RateLimitingDashboardJsonContext.Default.RateLimitingLookupBody,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            body = null;
        }

        // The problems name the fields, never the subject: a problem is served, and a host may log it.
        if (body is null)
        {
            return DashboardValidation.Problem(
                DashboardValidation.BodyField,
                DashboardValidation.JsonRule,
                "The body must be a lookup: { purpose, subject, quota? }."
            );
        }

        if (
            await _LookupValidator.ValidateAsync(body, cancellationToken).ConfigureAwait(false) is
            { IsValid: false } invalid
        )
        {
            return DashboardValidation.Problem(invalid);
        }

        var purpose = body.Purpose!;
        var subject = body.Subject!;
        var resolution = RateLimitingQuotas.Resolve(
            _limiterOptions,
            purpose,
            body.Quota?.Limit,
            body.Quota?.WindowSeconds
        );

        if (resolution.Quota is not { } quota)
        {
            return DashboardValidation.Problem("quota", DashboardValidation.InvalidRule, resolution.Refusal!);
        }

        AttemptSnapshot snapshot;

        try
        {
            snapshot = await _limiter.PeekAsync(purpose, subject, quota, cancellationToken).ConfigureAwait(false);
        }
        catch (AttemptLimiterUnavailableException exception)
        {
            // The dashboard maps no problem details of its own, so an unavailable store would surface as a 500; its
            // message names the purpose only, never the subject.
            return TypedResults.Problem(detail: exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        // Derived here rather than read from the snapshot, so a lookup and a reset name the subject the same way even
        // under a custom limiter.
        var view = new RateLimitingLookupView(
            purpose,
            _fingerprinter.Compute(purpose, subject),
            snapshot.Count,
            snapshot.Limit,
            snapshot.Remaining,
            snapshot.IsLockedOut,
            snapshot.WindowStartsAt,
            snapshot.WindowEndsAt,
            (long)snapshot.RetryAfter.TotalSeconds,
            new RateLimitingQuotaView(quota.Limit, (long)quota.Window.TotalSeconds),
            resolution.Declared
        );

        return TypedResults.Json(view, RateLimitingDashboardJsonContext.Default.RateLimitingLookupView);
    }
}
