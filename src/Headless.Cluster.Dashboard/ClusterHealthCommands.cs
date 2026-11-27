// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Checks;
using Headless.Dashboard;

namespace Headless.Cluster;

/// <summary>
/// The module's two commands, both operator actions on the serving host's health checks: <c>cluster.probe</c> runs the
/// checks it names, and <c>cluster.probe-all</c> runs every check. The shell enforces the operate permission and
/// writes each run to the operator log. After a run each probed check goes out on this node's live streams, because
/// the results live in this process and no other node's change read could see them.
/// </summary>
internal sealed class ClusterHealthCommands(
    string moduleId,
    ClusterServices services,
    IDashboardEventPublisher publisher
)
{
    public const string ProbeId = "cluster.probe";
    public const string ProbeAllId = "cluster.probe-all";

    private readonly string _moduleId = Argument.IsNotNullOrWhiteSpace(moduleId);
    private readonly ClusterServices _services = Argument.IsNotNull(services);
    private readonly IDashboardEventPublisher _publisher = Argument.IsNotNull(publisher);

    public IReadOnlyList<DashboardCommand> Commands { get; } =
    [
        // A probe changes no state an operator owns, so it asks for no confirmation or reason; it still needs operate
        // permission and is logged, because it runs real calls against the host's dependencies.
        new()
        {
            Id = ProbeId,
            Label = "Probe now",
            ObjectKind = ClusterObjects.CheckKind,
            Permission = DashboardCommandPermission.Operate,
            Confirmation = DashboardConfirmation.None,
            RequiresReason = false,
        },
        // It acts on the serving host as one object, so one run is one row whatever the number of checks.
        new()
        {
            Id = ProbeAllId,
            Label = "Probe all",
            ObjectKind = ClusterObjects.HealthKind,
            Permission = DashboardCommandPermission.Operate,
            Confirmation = DashboardConfirmation.None,
            RequiresReason = false,
        },
    ];

    public IReadOnlyList<DashboardCommandAvailability> GetAllowed(IReadOnlyList<ObjectRef> targets)
    {
        var reason = _services.Monitor is null ? HealthEndpoints.MissingMonitor : null;
        var answers = new List<DashboardCommandAvailability>(targets.Count);

        foreach (var target in targets)
        {
            var commandId = target.Kind switch
            {
                ClusterObjects.CheckKind => ProbeId,
                ClusterObjects.HealthKind => ProbeAllId,
                _ => null,
            };

            if (commandId is not null)
            {
                answers.Add(
                    new DashboardCommandAvailability
                    {
                        Target = target,
                        CommandId = commandId,
                        Allowed = reason is null,
                        DisabledReason = reason,
                    }
                );
            }
        }

        return answers;
    }

    public async Task<DashboardCommandResult> ExecuteAsync(
        DashboardCommandRequest request,
        CancellationToken cancellationToken
    )
    {
        if (_services.Monitor is not { } monitor)
        {
            return new DashboardCommandResult { Rows = [], Refusal = HealthEndpoints.MissingMonitor };
        }

        if (string.Equals(request.CommandId, ProbeAllId, StringComparison.Ordinal))
        {
            var report = await monitor.ProbeAllAsync(cancellationToken).ConfigureAwait(false);
            _PublishProbed(report.Entries.Keys);
            var host = request.Ids.Count > 0 ? request.Ids[0] : _services.HostLabel;

            return new DashboardCommandResult
            {
                Rows =
                [
                    new DashboardRowResult
                    {
                        Id = host,
                        Outcome = DashboardRowOutcome.Done,
                        Reason = string.Create(
                            CultureInfo.InvariantCulture,
                            $"{report.Status} · {report.Entries.Count} checks"
                        ),
                    },
                ],
            };
        }

        var rows = new List<DashboardRowResult>(request.Ids.Count);

        foreach (var id in request.Ids)
        {
            var report = await monitor.ProbeAsync(id, cancellationToken).ConfigureAwait(false);

            if (report is null)
            {
                rows.Add(
                    new DashboardRowResult
                    {
                        Id = id,
                        Outcome = DashboardRowOutcome.Skipped,
                        Reason = $"No check named '{id}'",
                    }
                );

                continue;
            }

            _PublishProbed(report.Entries.Keys);
            rows.Add(
                new DashboardRowResult
                {
                    Id = id,
                    Outcome = DashboardRowOutcome.Done,
                    Reason = report.Status.ToString(),
                }
            );
        }

        return new DashboardCommandResult { Rows = rows };
    }

    /// <summary>
    /// Publishes one change per probed check, versioned by its recorded result's completion time so the shell, which
    /// drops a version it has already sent, passes every later probe.
    /// </summary>
    private void _PublishProbed(IEnumerable<string> names)
    {
        var snapshot = _services.Monitor!.GetSnapshot();

        foreach (var name in names)
        {
            if (HealthEndpoints.FindCheck(snapshot, name) is not { Results.Count: > 0 } check)
            {
                continue;
            }

            _publisher.Publish(
                new DashboardChange
                {
                    Target = ClusterObjects.Ref(_moduleId, ClusterObjects.CheckKind, check.Descriptor.Name),
                    Version = ClusterObjects.Version(check.Results[^1].CompletedAt),
                    AffectsAttention = true,
                }
            );
        }
    }
}
