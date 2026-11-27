// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Dashboard;
using Headless.MultiTenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging;

namespace Headless.Idempotency;

/// <summary>
/// The Idempotency module's own read API at <c>{base}/api/idempotency</c>: the store description, and a lookup of one
/// key's record metadata in a named tenant. Nothing here admits, releases, or changes a record, or returns its bytes.
/// </summary>
internal sealed class IdempotencyApi(IIdempotencyInspector inspector, ICurrentTenant currentTenant, ILogger logger)
{
    private const string _UnavailableDetail = "The idempotency store is unavailable. Try again.";

    private static readonly IdempotencyLookupBodyValidator _LookupValidator = new();

    private readonly IIdempotencyInspector _inspector = Argument.IsNotNull(inspector);
    private readonly ICurrentTenant _currentTenant = Argument.IsNotNull(currentTenant);
    private readonly ILogger _logger = Argument.IsNotNull(logger);

    public void Map(DashboardModuleEndpoints endpoints)
    {
        var routes = endpoints.Routes;

        routes.MapGet("/store", _GetStore);
        // A lookup reads and changes nothing; it is a POST so the key (often an order or payment reference) stays out
        // of URLs and request logs.
        routes.MapPost("/lookup", _LookupAsync);
    }

    private JsonHttpResult<IdempotencyStoreView> _GetStore() =>
        TypedResults.Json(
            IdempotencyStoreView.From(_inspector.Describe()),
            IdempotencyDashboardJsonContext.Default.IdempotencyStoreView
        );

    private async Task<
        Results<JsonHttpResult<IdempotencyLookupView>, ValidationProblem, ProblemHttpResult>
    > _LookupAsync(HttpContext context, CancellationToken cancellationToken)
    {
        IdempotencyLookupBody? body;

        try
        {
            body = await JsonSerializer
                .DeserializeAsync(
                    context.Request.Body,
                    IdempotencyDashboardJsonContext.Default.IdempotencyLookupBody,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            body = null;
        }

        // The problems name the fields, never the key or the tenant: a problem is served, and a host may log it.
        if (body is null)
        {
            return DashboardValidation.Problem(
                DashboardValidation.BodyField,
                DashboardValidation.JsonRule,
                "The body must be a lookup: { key, tenantId? }."
            );
        }

        if (
            await _LookupValidator.ValidateAsync(body, cancellationToken).ConfigureAwait(false) is
            { IsValid: false } invalid
        )
        {
            return DashboardValidation.Problem(invalid);
        }

        IdempotencyRecordSnapshot? snapshot;

        try
        {
            // Always changed, the host scope included: the ambient tenant can come from a header the caller controls,
            // so it never picks the namespace a lookup reads. The scope spans the read alone and restores the
            // request's tenant even when the store throws.
            using (_currentTenant.Change(body.TenantId))
            {
                snapshot = await _inspector.InspectAsync(body.Key!, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ArgumentException exception) when (exception.ParamName is "key" or "tenantId")
        {
            // The validator applies the store's rules first, so this is a rule it does not know yet. Only the two
            // input names map to 400: any other argument failure is the store's own fault and answers 503 below. The
            // store's own message can quote the input, so the answer uses the module's.
            var field = string.Equals(exception.ParamName, "tenantId", StringComparison.Ordinal) ? "tenantId" : "key";

            return DashboardValidation.Problem(
                field,
                DashboardValidation.InvalidRule,
                $"The idempotency store refuses this {field}."
            );
        }
#pragma warning disable CA1031 // A boundary: every store failure answers 503; the providers throw no common type.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            // Only the type is logged: a provider's message, and its stack's data, can quote the key and the tenant.
            _logger.LookupStoreFailed(
                exception.GetType().FullName ?? exception.GetType().Name,
                body.TenantId is not null
            );

            return TypedResults.Problem(
                detail: _UnavailableDetail,
                statusCode: StatusCodes.Status503ServiceUnavailable
            );
        }

        var view = new IdempotencyLookupView(
            body.TenantId,
            snapshot is not null,
            snapshot is null ? null : IdempotencyRecordView.From(snapshot)
        );

        return TypedResults.Json(view, IdempotencyDashboardJsonContext.Default.IdempotencyLookupView);
    }
}

internal static partial class IdempotencyDashboardLog
{
    [LoggerMessage(
        EventId = 2240,
        EventName = "IdempotencyDashboardLookupStoreFailed",
        Level = LogLevel.Warning,
        Message = "An idempotency dashboard lookup failed in the store with {ExceptionType} (tenant named: {TenantNamed})"
    )]
    internal static partial void LookupStoreFailed(this ILogger logger, string exceptionType, bool tenantNamed);
}
