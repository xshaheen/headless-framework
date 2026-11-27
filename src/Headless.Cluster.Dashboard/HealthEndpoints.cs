// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Checks;
using Headless.Dashboard;
using Headless.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Headless.Cluster;

/// <summary>
/// The Cluster module's health reads at <c>{base}/api/cluster/health</c>: the serving host's overall health, its
/// checks with their latest result, and one check's recent results. They read the monitor's memory only and never run
/// a check: probing is an operator action, done through the probe commands.
/// </summary>
internal sealed class HealthEndpoints(ClusterServices services)
{
    internal const string MissingHealthChecks =
        "Health checks are not registered on this host. Call services.AddHealthChecks() and add the host's checks.";

    internal const string MissingMonitor =
        "The health check monitor is not registered. Call services.AddHeadlessHealthCheckMonitor(), or register the "
        + "health checks before AddCluster() so the module registers it.";

    /// <summary>The status filter value for a check with no recorded result.</summary>
    internal const string NotProbed = "NotProbed";

    private readonly ClusterServices _services = Argument.IsNotNull(services);

    public void Map(DashboardModuleEndpoints endpoints)
    {
        var routes = endpoints.Routes;

        routes.MapGet("/health", _Health);
        routes.MapGet("/health/checks", _ListChecks);
        routes.MapGet("/health/checks/{name}", _GetCheck);
    }

    private Results<JsonHttpResult<ClusterHealthView>, ProblemHttpResult> _Health()
    {
        if (_Unavailable() is { } problem)
        {
            return problem;
        }

        var snapshot = _services.Monitor!.GetSnapshot();
        var latest = snapshot
            .Checks.Select(static check =>
                (check.Descriptor, Latest: check.Results.Count > 0 ? check.Results[^1] : null)
            )
            .ToList();
        var probed = latest.Where(static check => check.Latest is not null).ToList();
        var liveness = latest.Where(static check => check.Descriptor.IsLiveness).ToList();
        // MaxBy throws on an empty sequence of tuples, so an unprobed host takes the default, whose result is null.
        var (slowestDescriptor, slowestResult) =
            probed.Count > 0 ? probed.MaxBy(static check => check.Latest!.Duration) : default;

        return TypedResults.Json(
            new ClusterHealthView(
                _services.HostLabel,
                snapshot.Status,
                probed.Count > 0 ? probed.Max(static check => check.Latest!.CompletedAt) : null,
                new ClusterHealthTotals(
                    probed.Count(static check => check.Latest!.Status == HealthStatus.Healthy),
                    probed.Count(static check => check.Latest!.Status == HealthStatus.Degraded),
                    probed.Count(static check => check.Latest!.Status == HealthStatus.Unhealthy),
                    latest.Count - probed.Count
                ),
                liveness.Count > 0
                    ? new ClusterLivenessView(
                        liveness.Count(static check => check.Latest?.Status == HealthStatus.Healthy),
                        liveness.Count
                    )
                    : null,
                slowestResult is not null
                    ? new ClusterSlowestView(slowestDescriptor.Name, (long)slowestResult.Duration.TotalMilliseconds)
                    : null
            ),
            ClusterDashboardJsonContext.Default.ClusterHealthView
        );
    }

    private Results<JsonHttpResult<ClusterCheckList>, ValidationProblem, ProblemHttpResult> _ListChecks(string? status)
    {
        if (_Unavailable() is { } problem)
        {
            return problem;
        }

        var filter = _ParseStatusFilter(status, out var valid);

        if (!valid)
        {
            return DashboardValidation.Problem(
                "status",
                DashboardValidation.EnumRule,
                "status must be Healthy, Degraded, Unhealthy, or NotProbed."
            );
        }

        var items = _services
            .Monitor!.GetSnapshot()
            .Checks.Where(check => filter is null || filter(check))
            .OrderBy(static check => check.Descriptor.Name, StringComparer.Ordinal)
            .Select(ClusterCheckView.From)
            .ToList();

        return TypedResults.Json(new ClusterCheckList(items), ClusterDashboardJsonContext.Default.ClusterCheckList);
    }

    private Results<JsonHttpResult<ClusterCheckDetail>, NotFound, ProblemHttpResult> _GetCheck(string name)
    {
        if (_Unavailable() is { } problem)
        {
            return problem;
        }

        if (FindCheck(_services.Monitor!.GetSnapshot(), name) is not { } check)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Json(
            new ClusterCheckDetail(
                ClusterCheckView.From(check),
                [.. check.Results.Reverse().Select(ClusterProbeResultView.From)]
            ),
            ClusterDashboardJsonContext.Default.ClusterCheckDetail
        );
    }

    /// <summary>The check registered under <paramref name="name"/>, matched case-insensitively as the monitor does.</summary>
    internal static HealthCheckHistory? FindCheck(HealthCheckSnapshot snapshot, string name) =>
        snapshot.Checks.FirstOrDefault(check =>
            string.Equals(check.Descriptor.Name, name, StringComparison.OrdinalIgnoreCase)
        );

    /// <summary>
    /// The filter a <c>status</c> query names: a health status, or <see cref="NotProbed"/> for a check with no result
    /// yet, which is no health status. <paramref name="valid"/> is <see langword="false"/> for any other value.
    /// </summary>
    private static Func<HealthCheckHistory, bool>? _ParseStatusFilter(string? value, out bool valid)
    {
        valid = true;

        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (string.Equals(value, NotProbed, StringComparison.OrdinalIgnoreCase))
        {
            return static check => check.Results.Count == 0;
        }

        if (
            Enum.TryParse<HealthStatus>(value, ignoreCase: true, out var status)
            && Enum.IsDefined(status)
            && !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
        )
        {
            return check => check.Results.Count > 0 && check.Results[^1].Status == status;
        }

        valid = false;

        return null;
    }

    private ProblemHttpResult? _Unavailable()
    {
        if (!_services.HasHealthChecks)
        {
            return TypedResults.Problem(
                detail: MissingHealthChecks,
                statusCode: StatusCodes.Status404NotFound,
                title: "Health checks are not registered"
            );
        }

        if (_services.Monitor is null)
        {
            return TypedResults.Problem(
                detail: MissingMonitor,
                statusCode: StatusCodes.Status409Conflict,
                title: "The health check monitor is not registered"
            );
        }

        return null;
    }
}
