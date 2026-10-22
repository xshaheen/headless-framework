// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.DistributedLocks;
using Headless.Hosting;
using Headless.Jobs.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Headless.Jobs.BackgroundServices;

/// <summary>
/// Writes the code-declared cron jobs into the store, then runs the application's <c>UseJobsSeeder</c> delegates. It
/// runs only when the application opts in: the deployment's <c>seed</c> verb, or <c>AddDeployStepsOnStartup</c>, whose
/// startup seeding finishes before the scheduler's activation barrier opens.
/// </summary>
/// <remarks>
/// The seed is what applies a changed cron expression or time zone, retires a removed cron and its pending occurrences,
/// and creates a new cron's row. Rows carry a deterministic id per function, so concurrent seeds converge on one row.
/// </remarks>
internal sealed class JobsSeeder(
    IServiceProvider serviceProvider,
    JobFunctionRegistry functionRegistry,
    ILogger<JobsSeeder> logger,
    TimeSpan? seedLockWait = null
) : ISeeder
{
    // Longer than the lease, so a lease a crashed run left behind expires inside the wait instead of failing the seed.
    internal static readonly TimeSpan DefaultSeedLockWait =
        JobsKeys.GuardAcquireOptions.TimeUntilExpires!.Value + TimeSpan.FromSeconds(30);

    private readonly DistributedLockAcquireOptions _seedLockOptions = new()
    {
        AcquireTimeout = seedLockWait ?? DefaultSeedLockWait,
        TimeUntilExpires = JobsKeys.GuardAcquireOptions.TimeUntilExpires,
        Monitoring = LockMonitoringMode.None,
    };

    public string Name => "Jobs";

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var executionContext = serviceProvider.GetRequiredService<JobsExecutionContext>();
        var schedulerOptions = serviceProvider.GetRequiredService<SchedulerOptionsBuilder>();
        var options = executionContext.OptionsSeeding;

        if (options?.SeedDefinedCronJobs != false)
        {
            await SeedDefinedCronJobsAsync(schedulerOptions, cancellationToken).ConfigureAwait(false);
        }

        // Each seeder runs inside its own scope. A consumer-supplied seeder commonly resolves the scoped
        // ITimeJobManager<>/ICronJobManager<>/IJobScheduler facades.
        if (options?.TimeSeederAction is not null)
        {
            await using var scope = serviceProvider.CreateAsyncScope();
            await options.TimeSeederAction(scope.ServiceProvider).ConfigureAwait(false);
        }

        if (options?.CronSeederAction is not null)
        {
            await using var scope = serviceProvider.CreateAsyncScope();
            await options.CronSeederAction(scope.ServiceProvider).ConfigureAwait(false);
        }
    }

    // Internal (not private) is the codebase's standard InternalsVisibleTo test seam: the guard unit test constructs
    // the seeder and calls this directly.
    internal async Task SeedDefinedCronJobsAsync(
        SchedulerOptionsBuilder schedulerOptions,
        CancellationToken cancellationToken
    )
    {
        // Its own scope, like the TimeSeederAction/CronSeederAction seeders, against a scoped resolution creeping into
        // this path (IInternalJobManager itself is singleton today).
        await using var scope = serviceProvider.CreateAsyncScope();
        var scopedProvider = scope.ServiceProvider;
        var internalJobsManager = scopedProvider.GetRequiredService<IInternalJobManager>();

        // Resolve the per-definition knobs HERE rather than in the provider, so every node seeds the same values: if
        // each provider resolved them from local configuration, two nodes could disagree about whether the same
        // instant misfired.
        var functionsToSeed = functionRegistry
            .Functions.Where(x => !string.IsNullOrEmpty(x.Value.CronExpression))
            .Select(x =>
                _ToSeed(
                    x.Key,
                    x.Value,
                    schedulerOptions,
                    scopedProvider
                        .GetRequiredService<CronScheduleCache>()
                        .ComputeEvaluationFingerprint(x.Value.TimeZoneId),
                    functionRegistry.Descriptors[x.Key].ContractVersion,
                    functionRegistry.GetFlattenedFailurePolicy(x.Key)
                )
            )
            .ToArray();

        // No lock configured (default): run the seed directly. Seeded rows carry a DETERMINISTIC primary key derived
        // from the function, so simultaneous first-boot on N nodes converges on a single row (PK dedup) — no duplicate
        // schedules even without the lock. The optional lock below only removes the redundant N-node scan/write storm;
        // it is never the correctness boundary — per-row predicates, node@incarnation ownership, and per-job leases are.
        if (!schedulerOptions.UseStorageLock)
        {
            await internalJobsManager.MigrateDefinedCronJobs(functionsToSeed, cancellationToken).ConfigureAwait(false);
            return;
        }

        IDistributedLease? lease;
        try
        {
            // Resolve the keyed lock lazily INSIDE the try (not via constructor injection) so a consumer factory that
            // throws at resolution — e.g. UseDistributedLock(sp => sp.GetRequiredService<IDistributedLock>()) when no
            // provider is registered — is logged as an acquire fault like any other.
            var lockProvider = scopedProvider.GetRequiredKeyedService<IDistributedLock>(JobsKeys.LockProvider);
            lease = await lockProvider
                .TryAcquireAsync(JobsKeys.CronSeedMigrationResource, _seedLockOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Host-shutdown / caller cancellation (our own token tripped) must propagate — do not swallow it as a skip.
            throw;
        }
        catch (Exception ex)
        {
            // A lock-store fault, including a provider that surfaces an internal timeout as an
            // OperationCanceledException while our token is not cancelled, fails the seed. The seed runs once per
            // deployment (the seed verb) or per opted-in start, so nothing retries a skipped seed: reporting success
            // would leave the cron rows stale with no failed step to show for it.
            logger.CronSeedMigrationLockAcquireFailed(ex);
            throw;
        }

        if (lease is null)
        {
            // The seed runs once per deployment, so nothing retries a skipped one: a lock still held after the wait
            // (another seed that outlived its lease, or a stuck one) fails the step instead of reporting success over
            // stale cron rows.
            logger.CronSeedMigrationLockTimedOut(_seedLockOptions.AcquireTimeout!.Value);

            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The '{JobsKeys.CronSeedMigrationResource}' lock was still held after {_seedLockOptions.AcquireTimeout.Value.TotalSeconds:F0}s, so the cron seed did not run. Another seed is running or did not release it; run 'seed' again once it finishes."
                )
            );
        }

        await using (lease.ConfigureAwait(false))
        {
            await internalJobsManager.MigrateDefinedCronJobs(functionsToSeed, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Resolves one declared function's knobs — attribute value, else the scheduler-wide default — and rejects any
    /// that a hand-written registration could still carry out of range; the source generator already rejects them for
    /// attributes. The retries come from the job's resolved failure policy, flattened the same way a scheduling call
    /// stores them.
    /// </summary>
    private static CronSeedDefinition _ToSeed(
        string function,
        JobFunctionRegistration registration,
        SchedulerOptionsBuilder schedulerOptions,
        string evaluationFingerprint,
        string contractVersion,
        (int Retries, int[]? RetryIntervals) flattenedFailurePolicy
    )
    {
        var onMissedRun = registration.OnMissedRun ?? schedulerOptions.DefaultMissedRunPolicy;
        var graceSeconds = registration.MissedRunGraceSeconds ?? schedulerOptions.DefaultMissedRunGraceSeconds;
        var onOverlap = registration.OnOverlap ?? schedulerOptions.DefaultOverlapPolicy;

        if (!Enum.IsDefined(onMissedRun))
        {
            throw new JobValidatorException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Missed-run policy value '{(int)onMissedRun}' is not defined for function '{function}'."
                )
            );
        }

        if (graceSeconds <= 0)
        {
            throw new JobValidatorException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Missed-run grace must be greater than zero seconds for function '{function}' but was {graceSeconds}."
                )
            );
        }

        if (!Enum.IsDefined(onOverlap))
        {
            throw new JobValidatorException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Overlap policy value '{(int)onOverlap}' is not defined for function '{function}'."
                )
            );
        }

        return new CronSeedDefinition(
            function,
            registration.CronExpression,
            onMissedRun,
            graceSeconds,
            onOverlap,
            evaluationFingerprint,
            contractVersion,
            registration.TimeZoneId,
            flattenedFailurePolicy.Retries,
            flattenedFailurePolicy.RetryIntervals
        );
    }
}
