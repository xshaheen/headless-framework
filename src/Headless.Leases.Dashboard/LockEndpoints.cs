// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers.Text;
using System.Text;
using Headless.Checks;
using Headless.Dashboard;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Headless.Leases;

/// <summary>
/// The Leases module's distributed-lock reads at <c>{base}/api/leases/locks</c>: one page of the locks the provider
/// lists, and one lock. They call only the provider's list, count, and info members; none acquires, renews, or
/// releases a lock.
/// </summary>
/// <remarks>
/// The provider returns its whole list in one call, so each page request reads it once, sorts it by resource, and
/// returns the rows after the cursor. The cursor is the base64url of the last resource of the previous page.
/// </remarks>
internal sealed class LockEndpoints(LeasesServices services, TimeProvider timeProvider)
{
    internal const string MissingLocks =
        "No distributed lock provider is registered on this host. Call services.AddHeadlessDistributedLocks(...) "
        + "with a provider to show held locks.";

    internal const int PageSize = 100;

    private readonly LeasesServices _services = Argument.IsNotNull(services);
    private readonly TimeProvider _timeProvider = Argument.IsNotNull(timeProvider);

    public void Map(DashboardModuleEndpoints endpoints)
    {
        var routes = endpoints.Routes;

        routes.MapGet("/locks", _ListAsync);
        routes.MapGet("/locks/lock", _GetAsync);
    }

    private async Task<Results<JsonHttpResult<DistributedLockList>, ValidationProblem, ProblemHttpResult>> _ListAsync(
        string? after,
        CancellationToken cancellationToken
    )
    {
        if (!_services.HasLocks)
        {
            return NotRegistered();
        }

        string? afterResource = null;

        if (!string.IsNullOrEmpty(after) && !TryDecodeCursor(after, out afterResource))
        {
            return DashboardValidation.Problem(
                "after",
                DashboardValidation.FormatRule,
                "after must be the nextCursor of an earlier page."
            );
        }

        var readAt = _timeProvider.GetUtcNow();
        var all = await _services.Lock.ListActiveLocksAsync(cancellationToken).ConfigureAwait(false);
        var total = await _services.Lock.GetActiveLocksCountAsync(cancellationToken).ConfigureAwait(false);

        var remaining = all.Where(info =>
                afterResource is null || string.CompareOrdinal(info.Resource, afterResource) > 0
            )
            .OrderBy(static info => info.Resource, StringComparer.Ordinal)
            .Take(PageSize + 1)
            .ToList();
        var page = remaining.Take(PageSize).Select(DistributedLockView.From).ToList();
        var next = remaining.Count > PageSize ? EncodeCursor(page[^1].Resource) : null;

        return TypedResults.Json(
            new DistributedLockList(page, next, total, readAt),
            LeasesDashboardJsonContext.Default.DistributedLockList
        );
    }

    private async Task<
        Results<JsonHttpResult<DistributedLockDetail>, NotFound, ValidationProblem, ProblemHttpResult>
    > _GetAsync(string? resource, CancellationToken cancellationToken)
    {
        if (!_services.HasLocks)
        {
            return NotRegistered();
        }

        if (string.IsNullOrWhiteSpace(resource))
        {
            return DashboardValidation.Problem("resource", DashboardValidation.RequiredRule, "resource is required.");
        }

        var readAt = _timeProvider.GetUtcNow();
        var info = await _services.Lock.GetLockInfoAsync(resource, cancellationToken).ConfigureAwait(false);

        if (info is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Json(
            new DistributedLockDetail(DistributedLockView.From(info), readAt),
            LeasesDashboardJsonContext.Default.DistributedLockDetail
        );
    }

    internal static string EncodeCursor(string resource) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(resource));

    internal static bool TryDecodeCursor(string value, [NotNullWhen(true)] out string? resource)
    {
        resource = null;

        try
        {
            var bytes = Base64Url.DecodeFromChars(value);
            resource = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(
                bytes
            );

            return resource.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            // Invalid UTF-8 surfaces as DecoderFallbackException, an ArgumentException.
            return false;
        }
    }

    internal static ProblemHttpResult NotRegistered() =>
        TypedResults.Problem(
            detail: MissingLocks,
            statusCode: StatusCodes.Status404NotFound,
            title: "No distributed lock provider is registered"
        );
}
