// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Checks;
using Headless.Dashboard;
using Headless.Fencing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Headless.Leases;

/// <summary>
/// The Leases module's fenced-lease reads at <c>{base}/api/leases</c>: the summary of both halves, one keyset page of
/// leases, their counts per state, the kinds in use, and one lease. Each request makes at most one inspector call
/// besides the cached kinds, and none writes.
/// </summary>
internal sealed class FencedLeaseEndpoints(LeasesServices services, LeaseKindsCache kinds, TimeProvider timeProvider)
{
    internal const string MissingFencing =
        "Fencing is not registered on this host. Call services.AddHeadlessFencing(...) with a provider to show fenced "
        + "leases.";

    internal const int PageSize = 50;

    private readonly LeasesServices _services = Argument.IsNotNull(services);
    private readonly LeaseKindsCache _kinds = Argument.IsNotNull(kinds);
    private readonly TimeProvider _timeProvider = Argument.IsNotNull(timeProvider);

    public void Map(DashboardModuleEndpoints endpoints)
    {
        var routes = endpoints.Routes;

        routes.MapGet("/summary", _Summary);
        routes.MapGet("/fenced", _ListAsync);
        routes.MapGet("/fenced/counts", _CountsAsync);
        routes.MapGet("/fenced/kinds", _KindsAsync);
        routes.MapGet("/fenced/lease", _GetAsync);
    }

    // No store read: a fencing outage must not take down the summary the healthy lock view also needs.
    private JsonHttpResult<LeasesSummaryView> _Summary()
    {
        var fencing = _services.HasFencing ? new LeasesFencingView(Registered: true) : null;
        var locks = _services.HasLocks ? new LeasesLocksView(Registered: true) : null;

        return TypedResults.Json(
            new LeasesSummaryView(fencing, locks),
            LeasesDashboardJsonContext.Default.LeasesSummaryView
        );
    }

    private async Task<Results<JsonHttpResult<FencedLeaseList>, ValidationProblem, ProblemHttpResult>> _ListAsync(
        string? state,
        string? kind,
        string? scope,
        string? tenant,
        string? prefix,
        string? after,
        CancellationToken cancellationToken
    )
    {
        if (!_services.HasFencing)
        {
            return NotRegistered();
        }

        if (!_TryParseState(state, out var stateFilter))
        {
            return DashboardValidation.Problem(
                "state",
                DashboardValidation.EnumRule,
                "state must be All, Live, Expired, or Ended."
            );
        }

        if (_TryBuildQuery(kind, scope, tenant, prefix, stateFilter, out var query) is { } problem)
        {
            return problem;
        }

        FencedLeasePosition? position = null;

        if (!string.IsNullOrEmpty(after) && !FencedLeaseCursor.TryDecode(after, out position))
        {
            return DashboardValidation.Problem(
                "after",
                DashboardValidation.FormatRule,
                "after must be the nextCursor of an earlier page."
            );
        }

        FencedLeasePage page;

        try
        {
            page = await _services
                .Inspector.ListAsync(query, position, PageSize, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ArgumentException exception) when (position is not null)
        {
            // The filter was checked above, so a refusal here is the cursor's: a position no lease key can have.
            return DashboardValidation.Problem("after", DashboardValidation.InvalidRule, exception.Message);
        }

        return TypedResults.Json(
            new FencedLeaseList(
                [.. page.Items.Select(FencedLeaseView.From)],
                page.Next is null ? null : FencedLeaseCursor.Encode(page.Next),
                _timeProvider.GetUtcNow()
            ),
            LeasesDashboardJsonContext.Default.FencedLeaseList
        );
    }

    private async Task<
        Results<JsonHttpResult<FencedLeaseCountsView>, ValidationProblem, ProblemHttpResult>
    > _CountsAsync(string? kind, string? scope, string? tenant, string? prefix, CancellationToken cancellationToken)
    {
        if (!_services.HasFencing)
        {
            return NotRegistered();
        }

        if (_TryBuildQuery(kind, scope, tenant, prefix, FencedLeaseStateFilter.All, out var query) is { } problem)
        {
            return problem;
        }

        var counts = await _services.Inspector.CountAsync(query, cancellationToken).ConfigureAwait(false);

        return TypedResults.Json(
            FencedLeaseCountsView.From(counts),
            LeasesDashboardJsonContext.Default.FencedLeaseCountsView
        );
    }

    private async Task<Results<JsonHttpResult<FencedLeaseKindList>, ProblemHttpResult>> _KindsAsync(
        CancellationToken cancellationToken
    )
    {
        if (!_services.HasFencing)
        {
            return NotRegistered();
        }

        var items = await _kinds.GetAsync(_services.Inspector, cancellationToken).ConfigureAwait(false);

        return TypedResults.Json(
            new FencedLeaseKindList(items),
            LeasesDashboardJsonContext.Default.FencedLeaseKindList
        );
    }

    private async Task<
        Results<JsonHttpResult<FencedLeaseDetail>, NotFound, ValidationProblem, ProblemHttpResult>
    > _GetAsync(string? id, CancellationToken cancellationToken)
    {
        if (!_services.HasFencing)
        {
            return NotRegistered();
        }

        if (string.IsNullOrEmpty(id))
        {
            return DashboardValidation.Problem("id", DashboardValidation.RequiredRule, "id is required.");
        }

        if (!FencedLeaseId.TryParse(id, out var tenantId, out var kind, out var resource))
        {
            return DashboardValidation.Problem(
                "id",
                DashboardValidation.FormatRule,
                "id must be a lease id this module returned."
            );
        }

        LeaseSnapshot? snapshot;

        try
        {
            snapshot = await _services
                .Inspector.GetAsync(tenantId, kind, resource, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ArgumentException exception)
        {
            return DashboardValidation.Problem("id", DashboardValidation.InvalidRule, exception.Message);
        }

        if (snapshot is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Json(
            new FencedLeaseDetail(FencedLeaseView.From(snapshot), _timeProvider.GetUtcNow()),
            LeasesDashboardJsonContext.Default.FencedLeaseDetail
        );
    }

    /// <summary>
    /// Builds the inspector's query from the filter parameters, or returns the problem naming the first bad one. Each
    /// value is held to the rules the store keys by, so a value no lease can carry answers 400 on its own field
    /// instead of on the query as a whole.
    /// </summary>
    private static ValidationProblem? _TryBuildQuery(
        string? kind,
        string? scope,
        string? tenant,
        string? prefix,
        FencedLeaseStateFilter state,
        out FencedLeaseQuery query
    )
    {
        query = new FencedLeaseQuery();
        var tenantFilter = LeaseTenantFilter.Any;

        if (!string.IsNullOrEmpty(scope))
        {
            if (!string.Equals(scope, "host", StringComparison.OrdinalIgnoreCase))
            {
                return DashboardValidation.Problem("scope", DashboardValidation.EnumRule, "scope must be host.");
            }

            if (!string.IsNullOrEmpty(tenant))
            {
                return DashboardValidation.Problem(
                    "tenant",
                    DashboardValidation.InvalidRule,
                    "Send scope=host or tenant=<id>, not both."
                );
            }

            tenantFilter = LeaseTenantFilter.Host;
        }
        else if (!string.IsNullOrEmpty(tenant))
        {
            if (_KeyProblem(tenant, FencingFieldLimits.TenantIdMaxLength) is { } reason)
            {
                return DashboardValidation.Problem("tenant", DashboardValidation.InvalidRule, "tenant " + reason);
            }

            tenantFilter = LeaseTenantFilter.Of(tenant);
        }

        if (!string.IsNullOrEmpty(kind) && _KeyProblem(kind, FencingFieldLimits.KindMaxLength) is { } kindReason)
        {
            return DashboardValidation.Problem("kind", DashboardValidation.InvalidRule, "kind " + kindReason);
        }

        if (!string.IsNullOrEmpty(prefix))
        {
            if (prefix.Length > FencingFieldLimits.ResourceMaxLength)
            {
                return DashboardValidation.Problem(
                    "prefix",
                    DashboardValidation.MaxLengthRule,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"prefix must be at most {FencingFieldLimits.ResourceMaxLength} characters."
                    )
                );
            }

            // A prefix may end inside a resource's interior white space, so only its end is exempt from the key rules.
            if (_PortableProblem(prefix + "x") is { } prefixReason)
            {
                return DashboardValidation.Problem("prefix", DashboardValidation.InvalidRule, "prefix " + prefixReason);
            }
        }

        query = new FencedLeaseQuery
        {
            Tenant = tenantFilter,
            Kind = string.IsNullOrEmpty(kind) ? null : kind,
            ResourcePrefix = string.IsNullOrEmpty(prefix) ? null : prefix,
            State = state,
        };

        return null;
    }

    private static string? _KeyProblem(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "must not be white space.";
        }

        if (value.Length > maxLength)
        {
            return string.Create(CultureInfo.InvariantCulture, $"must be at most {maxLength} characters.");
        }

        return _PortableProblem(value);
    }

    private static string? _PortableProblem(string value)
    {
        try
        {
            Argument.IsPortableKey(value);

            return null;
        }
        catch (ArgumentException exception)
        {
            return "is not text every lease store keeps unchanged: " + exception.Message;
        }
    }

    private static bool _TryParseState(string? value, out FencedLeaseStateFilter state)
    {
        state = FencedLeaseStateFilter.All;

        // A declared name only, ignoring case: Enum.TryParse also accepts numbers and comma-joined names.
        return string.IsNullOrEmpty(value)
            || (
                Enum.GetNames<FencedLeaseStateFilter>().Contains(value, StringComparer.OrdinalIgnoreCase)
                && Enum.TryParse(value, ignoreCase: true, out state)
            );
    }

    internal static ProblemHttpResult NotRegistered() =>
        TypedResults.Problem(
            detail: MissingFencing,
            statusCode: StatusCodes.Status404NotFound,
            title: "Fencing is not registered"
        );
}
