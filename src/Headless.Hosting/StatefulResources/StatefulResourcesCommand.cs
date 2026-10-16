// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Hosting;

/// <summary>The <c>resources</c> verb of <c>RunHeadlessAsync</c>: parses its arguments, runs one operation, prints a report.</summary>
internal static class StatefulResourcesCommand
{
    public const string Name = "resources";

    private const string _Usage =
        "Usage: resources [setup|check|clear|teardown|statistics|list] [-t|--type <type>]... [-n|--name <name>]... "
        + "[--timeout <seconds>]";

    private static readonly TimeSpan _DefaultTimeout = TimeSpan.FromSeconds(60);

    private static readonly Dictionary<string, StatefulResourceOperation?> _Subcommands = new(StringComparer.Ordinal)
    {
        ["setup"] = StatefulResourceOperation.Setup,
        ["check"] = StatefulResourceOperation.Check,
        ["clear"] = StatefulResourceOperation.Clear,
        ["teardown"] = StatefulResourceOperation.Teardown,
        ["statistics"] = StatefulResourceOperation.Statistics,

        // Lists the resources without running anything.
        ["list"] = null,
    };

    private static readonly Dictionary<string, string> _Options = new(StringComparer.Ordinal)
    {
        ["-t"] = "type",
        ["--type"] = "type",
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
        if (!HostCommandLine.TryParse(Name, args, _Subcommands.Keys, _Options, out var parsed, out var error))
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

        var operation = _Subcommands[parsed.Subcommand ?? "setup"];
        var types = parsed.ValuesOf("type");
        var names = parsed.ValuesOf("name");
        IReadOnlyList<IStatefulResource> selected;
        IReadOnlyList<StatefulResourceOutcome>? outcomes = null;

        try
        {
            await validateStartup(services, cancellationToken).ConfigureAwait(false);

            // Exact names, case-insensitive: any listed value of one flag matches, and both flags must match.
            var resources = await StatefulResourceRunner
                .GetResourcesAsync(services, cancellationToken)
                .ConfigureAwait(false);
            selected =
            [
                .. resources.Where(r =>
                    (types.Count == 0 || types.Contains(r.Type, StringComparer.OrdinalIgnoreCase))
                    && (names.Count == 0 || names.Contains(r.Name, StringComparer.OrdinalIgnoreCase))
                ),
            ];

            if (selected.Count == 0)
            {
                // Nothing registered is a valid host; filters that match nothing are a mistyped deploy step.
                var noFilters = types.Count == 0 && names.Count == 0;
                await HostCommandLine
                    .WriteAsync(
                        stderr,
                        noFilters
                            ? "No stateful resources are registered.\n"
                            : "No stateful resource matches the --type and --name filters.\n",
                        cancellationToken
                    )
                    .ConfigureAwait(false);

                return noFilters ? HostCommandLine.ExitSuccess : HostCommandLine.ExitUsage;
            }

            if (operation is { } run)
            {
                outcomes = await StatefulResourceRunner
                    .RunAsync(selected, run, parsed.Timeout ?? _DefaultTimeout, cancellationToken)
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
                .. selected.Select(static r => new HostCommandRow([r.Type, r.Name], "", [])),
            ])
            : HostCommandLine.FormatTable([.. outcomes.Select(_Row)], rightAlignedColumn: 3);
        await HostCommandLine.WriteAsync(stdout, report, cancellationToken).ConfigureAwait(false);

        return outcomes?.Any(static outcome => outcome.IsFailure) == true
            ? HostCommandLine.ExitFailure
            : HostCommandLine.ExitSuccess;
    }

    private static HostCommandRow _Row(StatefulResourceOutcome outcome) =>
        new(
            [
                outcome.Resource.Type,
                outcome.Resource.Name,
                outcome.Status.ToString(),
                HostCommandLine.Duration(outcome.Duration),
            ],
            outcome.Status switch
            {
                StatefulResourceOutcomeStatus.Ok => "",
                StatefulResourceOutcomeStatus.Unsupported => "Not supported by this resource.",
                _ => outcome.Error?.Message ?? "",
            },
            [.. outcome.Properties.Select(static p => $"{p.Key}: {p.Value}")]
        );
}
