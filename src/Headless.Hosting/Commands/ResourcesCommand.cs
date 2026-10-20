// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Headless.Hosting;

/// <summary>The <c>resources</c> verb of <c>RunHeadlessAsync</c>: parses its arguments, runs one operation, prints a report.</summary>
internal static class ResourcesCommand
{
    public const string Name = "resources";

    private const string _Usage =
        "Usage: resources <provision|verify|purge|delete|status|list> [-k|--kind <kind>]... [-n|--name <name>]... "
        + "[--timeout <seconds>] [--force]";

    private const string _Force = "--force";

    private static readonly Dictionary<string, ResourceOperation?> _Subcommands = new(StringComparer.Ordinal)
    {
        ["provision"] = ResourceOperation.Provision,
        ["verify"] = ResourceOperation.Verify,
        ["purge"] = ResourceOperation.Purge,
        ["delete"] = ResourceOperation.Delete,
        ["status"] = ResourceOperation.Status,

        // Lists the resources without running anything.
        ["list"] = null,
    };

    private static readonly Dictionary<string, string> _Options = new(StringComparer.Ordinal)
    {
        ["-k"] = "kind",
        ["--kind"] = "kind",
        ["-n"] = "name",
        ["--name"] = "name",
    };

    public static async Task<int> RunAsync(
        IReadOnlyList<string> args,
        IServiceProvider services,
        TextWriter stdout,
        TextWriter stderr,
        Func<IServiceProvider, CancellationToken, Task> validateStartup,
        CancellationToken cancellationToken
    )
    {
        if (
            !HostCommandLine.TryParse(
                Name,
                args,
                _Subcommands.Keys,
                _Options,
                out var parsed,
                out var error,
                switches: [_Force]
            )
        )
        {
            return await HostCommandLine
                .UsageErrorAsync(stderr, error, _Usage, cancellationToken)
                .ConfigureAwait(false);
        }

        if (parsed.Help)
        {
            await HostCommandLine.WriteAsync(stdout, _Usage + "\n", cancellationToken).ConfigureAwait(false);

            return HostCommandLine.ExitSuccess;
        }

        // No default subcommand: a bare 'resources' that changed state would surprise an operator reading a deploy script.
        if (parsed.Subcommand is null)
        {
            return await HostCommandLine
                .UsageErrorAsync(stderr, "A subcommand is required.", _Usage, cancellationToken)
                .ConfigureAwait(false);
        }

        var operation = _Subcommands[parsed.Subcommand];

        // Purge and delete remove data, so outside the allowed environments they run only when the operator says so
        // explicitly: a production-like environment refuses them whatever it is named.
        if (
            operation is ResourceOperation.Purge or ResourceOperation.Delete
            && !parsed.Switches.Contains(_Force)
            && DestructiveOperationGuard.Refusal(
                services,
                $"'resources {parsed.Subcommand}'",
                "Pass --force to run it anyway"
            )
                is { } refusal
        )
        {
            await HostCommandLine.WriteAsync(stderr, refusal + "\n", cancellationToken).ConfigureAwait(false);

            return HostCommandLine.ExitUsage;
        }

        var kinds = parsed.ValuesOf("kind");
        var names = parsed.ValuesOf("name");
        IReadOnlyList<IResourceProvisioner> selected;
        IReadOnlyList<ResourceOutcome>? outcomes = null;

        try
        {
            await validateStartup(services, cancellationToken).ConfigureAwait(false);

            // Exact names, case-insensitive: any listed value of one flag matches, and both flags must match.
            var resources = await ResourceProvisioningRunner
                .GetProvisionersAsync(services, cancellationToken)
                .ConfigureAwait(false);
            selected =
            [
                .. resources.Where(r =>
                    (kinds.Count == 0 || kinds.Contains(r.Kind, StringComparer.OrdinalIgnoreCase))
                    && (names.Count == 0 || names.Contains(r.Name, StringComparer.OrdinalIgnoreCase))
                ),
            ];

            if (selected.Count == 0)
            {
                // Nothing registered is a valid host; filters that match nothing are a mistyped deploy step.
                var noFilters = kinds.Count == 0 && names.Count == 0;
                await HostCommandLine
                    .WriteAsync(
                        stderr,
                        noFilters
                            ? "No resource provisioners are registered.\n"
                            : "No resource provisioner matches the --kind and --name filters.\n",
                        cancellationToken
                    )
                    .ConfigureAwait(false);

                return noFilters ? HostCommandLine.ExitSuccess : HostCommandLine.ExitUsage;
            }

            // No default budget: a resource's own timeouts (the schema runner's lock wait and DDL timeout) govern
            // unless the operator bounds the whole run.
            if (operation is { } run)
            {
                outcomes = await ResourceProvisioningRunner
                    .RunAsync(selected, run, parsed.Timeout, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031, EPC12 // False positive: the command's process boundary reports any failure as its message and an exit code, by design.
        catch (Exception exception)
        {
            // The message only: validators and resources write it for the operator, and a stack trace adds noise.
            await HostCommandLine
                .WriteAsync(stderr, $"Resources failed: {exception.Message}\n", cancellationToken)
                .ConfigureAwait(false);

            return HostCommandLine.ExitFailure;
        }
#pragma warning restore CA1031, EPC12

        var report = outcomes is null
            ? HostCommandLine.FormatTable([
                .. selected.Select(static r => new HostCommandRow([r.Kind, r.Name], _Limits(r), [])),
            ])
            : HostCommandLine.FormatTable([.. outcomes.Select(_Row)], rightAlignedColumn: 3);
        await HostCommandLine.WriteAsync(stdout, report, cancellationToken).ConfigureAwait(false);

        return outcomes?.Any(static outcome => outcome.IsFailure) == true
            ? HostCommandLine.ExitFailure
            : HostCommandLine.ExitSuccess;
    }

    private static string _Limits(IResourceProvisioner provisioner) =>
        (provisioner.CanPurge, provisioner.CanDelete) switch
        {
            (true, true) => "",
            (false, true) => "Cannot purge.",
            (true, false) => "Cannot delete.",
            _ => "Cannot purge or delete.",
        };

    private static HostCommandRow _Row(ResourceOutcome outcome) =>
        new(
            [
                outcome.Resource.Kind,
                outcome.Resource.Name,
                outcome.Status.ToString(),
                HostCommandLine.Duration(outcome.Duration),
            ],
            outcome.Status switch
            {
                ResourceOutcomeStatus.Ok => "",
                ResourceOutcomeStatus.Unsupported => "Not supported by this resource.",
                _ => outcome.Error?.Message ?? "",
            },
            [.. outcome.Properties.Select(static p => $"{p.Key}: {p.Value}")]
        );
}
