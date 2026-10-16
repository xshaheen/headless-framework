// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Hosting;

/// <summary>Orders the registered seeders and runs them, stopping at the first failure.</summary>
internal static class SeedRunner
{
    /// <summary>
    /// Resolves every seeder from <paramref name="scoped"/> once, ordered by <see cref="SeederPriorityAttribute"/>
    /// (lower first, registration order among equals).
    /// </summary>
    /// <exception cref="InvalidOperationException">Two seeders share a name, so the <c>seed</c> verb could not tell them apart.</exception>
    public static IReadOnlyList<ISeeder> Ordered(IServiceProvider scoped)
    {
        var seeders = scoped
            .GetServices<ISeeder>()
            .OrderBy(static s => s.GetType().GetCustomAttribute<SeederPriorityAttribute>()?.Priority ?? 0)
            .ToList();

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

        return seeders;
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
                .RunAsync(token => seeder.SeedAsync(token).AsTask(), budget.Token, cancellationToken)
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

/// <summary>
/// Records one seeder type's registration, so a second registration with another factory is refused and the startup
/// check can list the seeders without constructing them.
/// </summary>
internal sealed record SeederRegistration(Type SeederType, Delegate? Factory);
