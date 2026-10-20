// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.Hosting;

/// <summary>The <c>seed</c> verb of <c>RunHeadlessAsync</c>: runs, verifies, or lists the registered seeders and prints a report.</summary>
internal static class SeedCommand
{
    public const string Name = "seed";

    private const string _Usage = "Usage: seed [verify|list] [-n|--name <seeder>]... [--timeout <seconds>]";

    private static readonly TimeSpan _DefaultTimeout = TimeSpan.FromMinutes(5);

    private static readonly string[] _Subcommands = ["verify", "list"];

    private static readonly Dictionary<string, string> _Options = new(StringComparer.Ordinal)
    {
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
        if (!HostCommandLine.TryParse(Name, args, _Subcommands, _Options, out var parsed, out var error))
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

        var names = parsed.ValuesOf("name");
        IReadOnlyList<ISeeder> seeders;
        IReadOnlyList<SeedOutcome>? outcomes = null;
        IReadOnlyList<SeedVerification>? verifications = null;

        try
        {
            await validateStartup(services, cancellationToken).ConfigureAwait(false);

            // One scope and one resolution for the whole command: the listing and the run see the same seeders.
            await using var scope = services.CreateAsyncScope();
            var registered = SeedRunner.Ordered(scope.ServiceProvider);
            var unknown = names
                .Where(n => !registered.Any(s => string.Equals(s.Name, n, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (unknown.Count > 0)
            {
                await HostCommandLine
                    .WriteAsync(stderr, $"No seeder is named {string.Join(", ", unknown)}.\n", cancellationToken)
                    .ConfigureAwait(false);

                return HostCommandLine.ExitUsage;
            }

            if (registered.Count == 0)
            {
                await HostCommandLine
                    .WriteAsync(stderr, "No seeders are registered.\n", cancellationToken)
                    .ConfigureAwait(false);

                return HostCommandLine.ExitSuccess;
            }

            seeders =
            [
                .. registered.Where(s => names.Count == 0 || names.Contains(s.Name, StringComparer.OrdinalIgnoreCase)),
            ];

            if (string.Equals(parsed.Subcommand, "verify", StringComparison.Ordinal))
            {
                verifications = await SeedRunner
                    .VerifyAsync(seeders, parsed.Timeout ?? _DefaultTimeout, cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (parsed.Subcommand is null)
            {
                outcomes = await SeedRunner
                    .RunAsync(seeders, parsed.Timeout ?? _DefaultTimeout, cancellationToken)
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
            await HostCommandLine
                .WriteAsync(stderr, $"Seed failed: {exception.Message}\n", cancellationToken)
                .ConfigureAwait(false);

            return HostCommandLine.ExitFailure;
        }
#pragma warning restore CA1031, EPC12

        if (verifications is not null)
        {
            await HostCommandLine
                .WriteAsync(
                    stdout,
                    HostCommandLine.FormatTable([.. verifications.Select(_Row)], rightAlignedColumn: 2),
                    cancellationToken
                )
                .ConfigureAwait(false);

            // Unknown is not a failure: a seeder that cannot tell must not fail a deployment that seeded correctly.
            return verifications.Any(static v => v.Error is not null || v.State == SeedState.Stale)
                ? HostCommandLine.ExitFailure
                : HostCommandLine.ExitSuccess;
        }

        var report = outcomes is null
            ? HostCommandLine.FormatTable([.. seeders.Select(static s => new HostCommandRow([s.Name], "", []))])
            : HostCommandLine.FormatTable([.. outcomes.Select(_Row)], rightAlignedColumn: 2);
        await HostCommandLine.WriteAsync(stdout, report, cancellationToken).ConfigureAwait(false);

        return outcomes?.Any(static o => o.Status == SeedOutcomeStatus.Failed) == true
            ? HostCommandLine.ExitFailure
            : HostCommandLine.ExitSuccess;
    }

    private static HostCommandRow _Row(SeedVerification verification) =>
        new(
            [
                verification.Name,
                verification.Error is null ? verification.State.ToString() : "Failed",
                HostCommandLine.Duration(verification.Duration),
            ],
            verification switch
            {
                { Error: { } error } => error.Message,
                { State: SeedState.Stale } => "The store does not hold what the code declares. Run 'seed'.",
                { State: SeedState.Unknown } => "This seeder cannot tell.",
                _ => "",
            },
            []
        );

    private static HostCommandRow _Row(SeedOutcome outcome) =>
        new(
            [outcome.Name, outcome.Status.ToString(), HostCommandLine.Duration(outcome.Duration)],
            outcome.Status switch
            {
                SeedOutcomeStatus.Failed => outcome.Error?.Message ?? "",
                SeedOutcomeStatus.Skipped => "Not run: an earlier seeder failed.",
                _ => "",
            },
            []
        );
}
