// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.Hosting;

/// <summary>Orders the registered seeders and runs them, stopping at the first failure.</summary>
internal static class SeedRunner
{
    /// <summary>
    /// Resolves every seeder from <paramref name="scoped"/> once, ordered so each runs after the seeders it names in
    /// <see cref="ISeeder.DependsOn"/>, registration order otherwise.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Two seeders share a name, so the <c>seed</c> verb could not tell them apart, or two depend on each other.
    /// </exception>
    public static IReadOnlyList<ISeeder> Ordered(IServiceProvider scoped)
    {
        List<ISeeder> registered = [.. scoped.GetServices<ISeeder>()];

        var seeders = registered;
        var duplicate = seeders
            .GroupBy(static s => s.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static g => g.Skip(1).Any());

        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Seeders {string.Join(", ", duplicate.Select(static s => s.GetType().Name))} share the name "
                    + $"'{duplicate.Key}'. Give each seeder its own ISeeder.Name."
            );
        }

        return DependencyOrder.Sort<ISeeder>(
            seeders,
            static (s, all) => s.DependsOn(all),
            static s => s.Name,
            "Seeders"
        );
    }

    /// <summary>
    /// Runs <paramref name="seeders"/> one at a time. After a failure the rest are skipped, because a later seeder may
    /// need what an earlier one writes.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public static async Task<IReadOnlyList<SeedOutcome>> RunAsync(
        IReadOnlyList<ISeeder> seeders,
        TimeSpan? timeout,
        CancellationToken cancellationToken
    )
    {
        using var budget = BudgetedStep.CreateBudget(timeout, cancellationToken);
        var outcomes = new List<SeedOutcome>(seeders.Count);
        var stopped = false;

        foreach (var seeder in seeders)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (stopped)
            {
                outcomes.Add(new(seeder.Name, SeedOutcomeStatus.Skipped, TimeSpan.Zero, Error: null));

                continue;
            }

            var result = await BudgetedStep
                .RunAsync(seeder.SeedAsync, budget.Token, cancellationToken)
                .ConfigureAwait(false);

            // A seeder has no unsupported state: anything but success stops the run.
            var ok = result.Status == BudgetedStepStatus.Ok;
            outcomes.Add(
                new(seeder.Name, ok ? SeedOutcomeStatus.Ok : SeedOutcomeStatus.Failed, result.Duration, result.Error)
            );
            stopped = !ok;
        }

        return outcomes;
    }

    /// <summary>
    /// Asks every seeder in <paramref name="seeders"/> whether its store is current, one at a time. Every seeder is asked
    /// even after one fails, so the report names each.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public static async Task<IReadOnlyList<SeedVerification>> VerifyAsync(
        IReadOnlyList<ISeeder> seeders,
        TimeSpan? timeout,
        CancellationToken cancellationToken
    )
    {
        using var budget = BudgetedStep.CreateBudget(timeout, cancellationToken);
        var verifications = new List<SeedVerification>(seeders.Count);

        foreach (var seeder in seeders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = SeedState.Unknown;
            var result = await BudgetedStep
                .RunAsync(
                    async token => state = await seeder.VerifyAsync(token).ConfigureAwait(false),
                    budget.Token,
                    cancellationToken
                )
                .ConfigureAwait(false);

            verifications.Add(
                new(seeder.Name, state, result.Duration, result.Status == BudgetedStepStatus.Ok ? null : result.Error)
            );
        }

        return verifications;
    }

    /// <summary>Throws when a seeder failed.</summary>
    /// <exception cref="InvalidOperationException">A seeder failed; its error is the inner exception.</exception>
    public static void ThrowIfFailed(IReadOnlyList<SeedOutcome> outcomes)
    {
        var failed = outcomes.FirstOrDefault(static o => o.Status == SeedOutcomeStatus.Failed);

        if (failed is not null)
        {
            throw new InvalidOperationException(
                $"Seeder {failed.Name} failed: {failed.Error?.Message}. The seeders after it did not run.",
                failed.Error
            );
        }
    }
}

internal enum SeedOutcomeStatus
{
    Ok = 0,
    Failed = 1,
    Skipped = 2,
}

internal sealed record SeedOutcome(string Name, SeedOutcomeStatus Status, TimeSpan Duration, Exception? Error);

internal sealed record SeedVerification(string Name, SeedState State, TimeSpan Duration, Exception? Error);

/// <summary>
/// Records one seeder type's registration, so a second registration with another factory is refused and the startup
/// check can list the seeders without constructing them.
/// </summary>
internal sealed record SeederRegistration(Type SeederType, Delegate? Factory);
