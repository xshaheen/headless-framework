// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Checks;
using Headless.Dashboard;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Sequences;

/// <summary>
/// The Sequences module's reads at <c>{base}/api/sequences</c>: the summary, the counter list, one counter, and one
/// counter's partitions. Every route reads through <see cref="ISequenceInspector"/>, which never takes a number, and
/// the module maps no route that changes a counter.
/// </summary>
/// <remarks>
/// A filter the module parses answers 400 naming its query field. A key part the inspector refuses answers 400 on the
/// field that carried it, with the inspector's message. Any other inspector failure answers 503 with a fixed message
/// and is logged, so no key text or SQL reaches the response.
/// </remarks>
internal sealed class SequencesEndpoints(
    ISequenceInspector inspector,
    IOptionsMonitor<SequencesOptions> options,
    ILogger<SequencesEndpoints> logger
)
{
    internal const string Unavailable = "The sequence store is unavailable.";

    /// <summary>The partitions read per lookup page, the inspector's largest.</summary>
    internal const int LookupPageSize = 200;

    /// <summary>The lookup pages read before a counter id is reported missing: the newest 1,000 partitions.</summary>
    internal const int LookupPages = 5;

    private const int _DefaultLimit = 50;
    private const int _MaxLimit = 200;

    private readonly ISequenceInspector _inspector = Argument.IsNotNull(inspector);
    private readonly IOptionsMonitor<SequencesOptions> _options = Argument.IsNotNull(options);
    private readonly ILogger<SequencesEndpoints> _logger = Argument.IsNotNull(logger);

    public void Map(DashboardModuleEndpoints endpoints)
    {
        var routes = endpoints.Routes;

        routes.MapGet("/summary", _SummaryAsync);
        routes.MapGet("/counters", _CountersAsync);
        routes.MapGet("/counter", _CounterAsync);
        routes.MapGet("/counter/partitions", _PartitionsAsync);
    }

    private async Task<
        Results<JsonHttpResult<SequencesSummaryView>, ValidationProblem, ProblemHttpResult>
    > _SummaryAsync(string? scope, string? tenant, string? q, CancellationToken cancellationToken)
    {
        if (_ParseScope(scope, tenant, out var tenantId) is { } scopeProblem)
        {
            return scopeProblem;
        }

        var query = new SequenceSummaryQuery { TenantId = tenantId, Search = string.IsNullOrEmpty(q) ? null : q };

        try
        {
            var all = await _inspector.GetSummaryAsync(query, cancellationToken).ConfigureAwait(false);
            var current = await _inspector
                .GetSummaryAsync(query with { CurrentPartitionsOnly = true }, cancellationToken)
                .ConfigureAwait(false);

            return TypedResults.Json(
                new SequencesSummaryView(
                    all.Total,
                    all.Fast,
                    all.GapFree,
                    all.Reported,
                    all.Capped,
                    current.Total,
                    current.Capped,
                    all.NextResetAt
                ),
                SequencesDashboardJsonContext.Default.SequencesSummaryView
            );
        }
        catch (ArgumentException exception)
        {
            return _Refused(exception, hasTenant: tenantId is { Length: > 0 }, cursorField: null);
        }
        catch (Exception exception) when (_IsStoreFailure(exception, cancellationToken))
        {
            return _StoreFailed(exception, "summary");
        }
    }

    private async Task<
        Results<JsonHttpResult<SequenceCounterList>, ValidationProblem, ProblemHttpResult>
    > _CountersAsync(
        string? scope,
        string? tenant,
        string? q,
        string? mode,
        string? current,
        string? after,
        string? limit,
        CancellationToken cancellationToken
    )
    {
        if (_ParseScope(scope, tenant, out var tenantId) is { } scopeProblem)
        {
            return scopeProblem;
        }

        SequenceMode? modeFilter = null;

        if (!string.IsNullOrEmpty(mode))
        {
            if (!DashboardValidation.TryParseEnumName<SequenceMode>(mode, out var parsedMode))
            {
                return DashboardValidation.Problem(
                    "mode",
                    DashboardValidation.EnumRule,
                    "mode must be Fast, GapFree, or Reported."
                );
            }

            modeFilter = parsedMode;
        }

        if (!_TryParseBoolean(current, out var currentOnly))
        {
            return DashboardValidation.Problem(
                "current",
                DashboardValidation.FormatRule,
                "current must be true or false."
            );
        }

        SequenceKey? afterKey = null;

        if (!string.IsNullOrEmpty(after))
        {
            if (!SequenceCounterId.TryParseListCursor(after, out var parsedAfter))
            {
                return DashboardValidation.Problem(
                    "after",
                    DashboardValidation.FormatRule,
                    "after is not a cursor this list returned."
                );
            }

            afterKey = parsedAfter;
        }

        if (!_TryParseLimit(limit, out var pageSize))
        {
            return _LimitProblem();
        }

        try
        {
            var page = await _inspector
                .ListCountersAsync(
                    new SequenceCounterQuery
                    {
                        TenantId = tenantId,
                        Search = string.IsNullOrEmpty(q) ? null : q,
                        Mode = modeFilter,
                        CurrentPartitionsOnly = currentOnly,
                        After = afterKey,
                        Limit = pageSize,
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);

            return _List(page.Items, page.Next is { } next ? SequenceCounterId.FormatListCursor(next) : null);
        }
        catch (ArgumentException exception)
        {
            return _Refused(
                exception,
                hasTenant: tenantId is { Length: > 0 },
                cursorField: afterKey is null ? null : "after"
            );
        }
        catch (Exception exception) when (_IsStoreFailure(exception, cancellationToken))
        {
            return _StoreFailed(exception, "counters");
        }
    }

    private async Task<
        Results<JsonHttpResult<SequenceCounterView>, ValidationProblem, ProblemHttpResult>
    > _CounterAsync(string? id, CancellationToken cancellationToken)
    {
        if (!SequenceCounterId.TryParse(id, out var key))
        {
            return _IdProblem();
        }

        try
        {
            if (await FindAsync(key, cancellationToken).ConfigureAwait(false) is not { } counter)
            {
                return TypedResults.Problem(
                    detail: "No counter with this id is among the newest 1,000 partitions of its name.",
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Counter not found"
                );
            }

            return TypedResults.Json(
                SequenceCounterView.From(counter, _options.CurrentValue),
                SequencesDashboardJsonContext.Default.SequenceCounterView
            );
        }
        catch (ArgumentException exception)
        {
            return DashboardValidation.Problem("id", DashboardValidation.InvalidRule, exception.Message);
        }
        catch (Exception exception) when (_IsStoreFailure(exception, cancellationToken))
        {
            return _StoreFailed(exception, "counter");
        }
    }

    private async Task<
        Results<JsonHttpResult<SequenceCounterList>, ValidationProblem, ProblemHttpResult>
    > _PartitionsAsync(string? id, string? before, string? limit, CancellationToken cancellationToken)
    {
        if (!SequenceCounterId.TryParse(id, out var key))
        {
            return _IdProblem();
        }

        string? beforePartition = null;

        if (!string.IsNullOrEmpty(before))
        {
            if (!SequenceCounterId.TryParsePartitionCursor(before, out beforePartition))
            {
                return DashboardValidation.Problem(
                    "before",
                    DashboardValidation.FormatRule,
                    "before is not a cursor this list returned."
                );
            }
        }

        if (!_TryParseLimit(limit, out var pageSize))
        {
            return _LimitProblem();
        }

        try
        {
            var page = await _inspector
                .ListPartitionsAsync(
                    new SequencePartitionQuery
                    {
                        TenantId = key.TenantId,
                        Name = key.Name,
                        Before = beforePartition,
                        Limit = pageSize,
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);

            return _List(page.Items, page.Next is { } next ? SequenceCounterId.FormatPartitionCursor(next) : null);
        }
        catch (ArgumentException exception)
        {
            var field = string.Equals(exception.ParamName, "partition", StringComparison.Ordinal) ? "before" : "id";

            return DashboardValidation.Problem(field, DashboardValidation.InvalidRule, exception.Message);
        }
        catch (Exception exception) when (_IsStoreFailure(exception, cancellationToken))
        {
            return _StoreFailed(exception, "partitions");
        }
    }

    /// <summary>
    /// Finds one counter by its key. The inspector has no single-key read, so this reads the newest partitions of the
    /// key's name until the partition appears; the newest sorts first, so most lookups take one page.
    /// </summary>
    /// <returns>The counter, or <see langword="null"/> when it is not among the newest 1,000 partitions.</returns>
    /// <exception cref="ArgumentException">A key part is not one the inspector accepts.</exception>
    internal async Task<SequenceCounter?> FindAsync(SequenceKey key, CancellationToken cancellationToken)
    {
        string? before = null;

        for (var page = 0; page < LookupPages; page++)
        {
            var partitions = await _inspector
                .ListPartitionsAsync(
                    new SequencePartitionQuery
                    {
                        TenantId = key.TenantId,
                        Name = key.Name,
                        Before = before,
                        Limit = LookupPageSize,
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);

            foreach (var counter in partitions.Items)
            {
                if (string.Equals(counter.Key.Partition, key.Partition, StringComparison.Ordinal))
                {
                    return counter;
                }
            }

            if (partitions.Next is not { } next)
            {
                return null;
            }

            before = next;
        }

        return null;
    }

    /// <summary>
    /// Reads the tenant filter a scope names into <paramref name="tenantId"/>: every tenant (<see langword="null"/>),
    /// the host scope (empty), or one exact tenant id.
    /// </summary>
    /// <returns>The problem naming the refused field, or <see langword="null"/> when the pair is accepted.</returns>
    private static ValidationProblem? _ParseScope(string? scope, string? tenant, out string? tenantId)
    {
        tenantId = null;

        if (string.IsNullOrEmpty(scope))
        {
            return string.IsNullOrEmpty(tenant) ? null : _TenantNotAcceptedProblem();
        }

        if (string.Equals(scope, "host", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrEmpty(tenant))
            {
                return _TenantNotAcceptedProblem();
            }

            tenantId = string.Empty;

            return null;
        }

        if (string.Equals(scope, "tenant", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(tenant))
            {
                return DashboardValidation.Problem(
                    "tenant",
                    DashboardValidation.RequiredRule,
                    "tenant is required when scope is tenant."
                );
            }

            tenantId = tenant;

            return null;
        }

        return DashboardValidation.Problem("scope", DashboardValidation.EnumRule, "scope must be host or tenant.");
    }

    private static ValidationProblem _TenantNotAcceptedProblem() =>
        DashboardValidation.Problem(
            "tenant",
            DashboardValidation.InvalidRule,
            "tenant is accepted only when scope is tenant."
        );

    private static bool _TryParseBoolean(string? value, out bool result)
    {
        result = false;

        return string.IsNullOrEmpty(value) || bool.TryParse(value, out result);
    }

    private static bool _TryParseLimit(string? value, out int limit)
    {
        limit = _DefaultLimit;

        if (string.IsNullOrEmpty(value))
        {
            return true;
        }

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        limit = Math.Clamp(parsed, 1, _MaxLimit);

        return true;
    }

    private static ValidationProblem _LimitProblem() =>
        DashboardValidation.Problem("limit", DashboardValidation.FormatRule, "limit must be a whole number.");

    private static ValidationProblem _IdProblem() =>
        DashboardValidation.Problem("id", DashboardValidation.FormatRule, "id is not a counter id this module served.");

    /// <summary>
    /// Answers a key part the inspector refused on the query field that carried it. The inspector names the key part,
    /// so a part from the cursor is told from the same part in a filter by whether the filter was given.
    /// </summary>
    private static ValidationProblem _Refused(ArgumentException exception, bool hasTenant, string? cursorField)
    {
        var field = exception.ParamName switch
        {
            "search" => "q",
            "tenantId" when hasTenant || cursorField is null => "tenant",
            _ => cursorField ?? "q",
        };

        return DashboardValidation.Problem(field, DashboardValidation.InvalidRule, exception.Message);
    }

    private JsonHttpResult<SequenceCounterList> _List(IEnumerable<SequenceCounter> counters, string? next)
    {
        var settings = _options.CurrentValue;

        return TypedResults.Json(
            new SequenceCounterList([.. counters.Select(counter => SequenceCounterView.From(counter, settings))], next),
            SequencesDashboardJsonContext.Default.SequenceCounterList
        );
    }

    private static bool _IsStoreFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested;

    private ProblemHttpResult _StoreFailed(Exception exception, string read)
    {
        _logger.LogSequencesReadFailed(exception, read);

        // The dashboard maps no problem details of its own, so a failing store would surface as a 500 with nothing
        // the view can say; the fixed message keeps key text and SQL out of the response.
        return TypedResults.Problem(detail: Unavailable, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}

internal static partial class SequencesEndpointsLogs
{
    [LoggerMessage(
        EventId = 2400,
        EventName = "SequencesDashboardReadFailed",
        Level = LogLevel.Warning,
        Message = "Sequences dashboard could not read the {Read}; the view shows the store as unavailable."
    )]
    internal static partial void LogSequencesReadFailed(this ILogger logger, Exception exception, string read);
}
