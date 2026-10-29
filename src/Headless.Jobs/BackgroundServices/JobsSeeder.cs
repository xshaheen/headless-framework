// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.DistributedLocks;
using Headless.Hosting;
using Headless.Jobs.Internal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Headless.Jobs.BackgroundServices;

/// <summary>
/// Writes the code-declared cron jobs into the store, then runs the application's <c>UseJobsSeeder</c> delegates. It
/// runs only when the application opts in: the deployment's <c>seed</c> verb, or <c>AddDeployStepsOnStartup</c>, whose
/// startup seeding finishes before the scheduler's activation barrier opens.
/// </summary>
/// <remarks>
/// The code-declared crons are the <c>[Job(Cron)]</c> crons and the <c>SeedRecurringJob</c> declarations, reconciled as
/// one set. The seed is what applies a changed cron expression or time zone, retires a removed cron and its pending
/// occurrences, and creates a new cron's row. It reconciles only the rows of this host's seed owner, and each row
/// carries a deterministic id derived from the owner and the function or declaration key, so concurrent seeds of one
/// owner converge on one row.
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

        await SeedDefinedCronJobsAsync(schedulerOptions, options, cancellationToken).ConfigureAwait(false);

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
        IJobsOptionsSeeding? options,
        CancellationToken cancellationToken
    )
    {
        var seedOwner = options?.SeedOwner ?? JobsSeedId.DefaultOwner;

        // Its own scope, like the TimeSeederAction/CronSeederAction seeders, against a scoped resolution creeping into
        // this path (IInternalJobManager itself is singleton today).
        await using var scope = serviceProvider.CreateAsyncScope();
        var scopedProvider = scope.ServiceProvider;
        var internalJobsManager = scopedProvider.GetRequiredService<IInternalJobManager>();

        // Resolve the per-definition knobs HERE rather than in the provider, so every node seeds the same values: if
        // each provider resolved them from local configuration, two nodes could disagree about whether the same
        // instant misfired.
        IEnumerable<CronSeedDefinition> attributeSeeds =
            options?.SeedDefinedCronJobs == false
                ? []
                : functionRegistry
                    .Functions.Where(x => !string.IsNullOrEmpty(x.Value.CronExpression))
                    .Select(x =>
                        _ToSeed(
                            seedOwner,
                            x.Key,
                            x.Value,
                            schedulerOptions,
                            scopedProvider
                                .GetRequiredService<CronScheduleCache>()
                                .ComputeEvaluationFingerprint(x.Value.TimeZoneId),
                            functionRegistry.Descriptors[x.Key].ContractVersion,
                            functionRegistry.GetFlattenedFailurePolicy(x.Key)
                        )
                    );

        // Every entry is built before the store is touched, so a declaration that cannot be built fails the seed with
        // nothing written. The reconcile then runs even when the list is empty: that is what deletes this owner's rows
        // for crons and declarations the app no longer makes.
        CronSeedDefinition[] functionsToSeed =
        [
            .. attributeSeeds,
            .. _ToDeclarationSeeds(
                seedOwner,
                options?.RecurringJobDeclarations ?? [],
                schedulerOptions,
                scopedProvider
            ),
        ];

        // No lock configured (default): run the seed directly. Seeded rows carry a DETERMINISTIC primary key derived
        // from the function, so simultaneous first-boot on N nodes converges on a single row (PK dedup) — no duplicate
        // schedules even without the lock. The optional lock below only removes the redundant N-node scan/write storm;
        // it is never the correctness boundary — per-row predicates, node@incarnation ownership, and per-job leases are.
        if (!schedulerOptions.UseStorageLock)
        {
            await internalJobsManager
                .MigrateDefinedCronJobs(seedOwner, functionsToSeed, cancellationToken)
                .ConfigureAwait(false);
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
            await internalJobsManager
                .MigrateDefinedCronJobs(seedOwner, functionsToSeed, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Resolves one declared function's knobs — attribute value, else the scheduler-wide default — and rejects any
    /// that a hand-written registration could still carry out of range; the source generator already rejects them for
    /// attributes. The retries come from the job's resolved failure policy, flattened the same way a scheduling call
    /// stores them.
    /// </summary>
    private static CronSeedDefinition _ToSeed(
        string seedOwner,
        string function,
        JobFunctionRegistration registration,
        SchedulerOptionsBuilder schedulerOptions,
        string evaluationFingerprint,
        string contractVersion,
        (int Retries, int[]? RetryIntervals) flattenedFailurePolicy
    )
    {
        var (onMissedRun, graceSeconds, onOverlap) = _ResolveCreationPolicies(function, registration, schedulerOptions);

        return CronSeedDefinition.ForCronFunction(
            seedOwner,
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

    /// <summary>
    /// Builds one entry per declaration. The declaration's own recurring options win, as they do for a
    /// <c>ScheduleRecurringAsync</c> call; the missed-run and overlap policies come from the job, as for its attribute
    /// cron.
    /// </summary>
    /// <exception cref="InvalidOperationException">A declaration names a job this host does not register.</exception>
    private CronSeedDefinition[] _ToDeclarationSeeds(
        string seedOwner,
        IReadOnlyList<RecurringJobDeclaration> declarations,
        SchedulerOptionsBuilder schedulerOptions,
        IServiceProvider scopedProvider
    )
    {
        if (declarations.Count == 0)
        {
            return [];
        }

        var policies = scopedProvider.GetRequiredService<JobSchedulingPolicies>();
        var serializationOptions = scopedProvider.GetRequiredService<JobsRequestSerializationOptions>();
        var scheduleCache = scopedProvider.GetRequiredService<CronScheduleCache>();
        var configuration = scopedProvider.GetService<IConfiguration>();
        var seeds = new CronSeedDefinition[declarations.Count];

        for (var i = 0; i < declarations.Count; i++)
        {
            var declaration = declarations[i];
            var descriptor =
                declaration.TryResolve(functionRegistry, out var error) ?? throw new InvalidOperationException(error);
            var function = descriptor.FunctionName;
            var (onMissedRun, graceSeconds, onOverlap) = _ResolveCreationPolicies(
                function,
                functionRegistry.Functions.GetValueOrDefault(function),
                schedulerOptions
            );
            var resolved = policies.ResolveRecurring(descriptor, declaration.Options);
            var options = declaration.Options;
            var request = descriptor.RequestType is null ? null : declaration.SerializeRequest(serializationOptions);
            // The seed compares this hash with the stored payload's to decide whether the arguments drifted. Hashing
            // the canonical form, not the bytes, keeps hosts that serialize the same arguments differently, or compress
            // them, from rewriting each other's row on every seed.
            var requestHash =
                JobsHelper.TryComputeCanonicalRequestHash(request ?? [])
                ?? throw new InvalidOperationException(
                    $"The arguments of the recurring job declaration '{declaration.Key}' did not serialize to valid JSON."
                );

            seeds[i] = CronSeedDefinition.ForDeclaration(
                seedOwner,
                declaration.Key,
                function,
                JobFunctionRegistryBuilder.ResolveCronExpression(declaration.CronExpression, configuration),
                onMissedRun,
                graceSeconds,
                onOverlap,
                requestHash,
                scheduleCache.ComputeEvaluationFingerprint(options?.TimeZoneId),
                descriptor.ContractVersion,
                options?.TimeZoneId,
                resolved.Retries ?? 0,
                resolved.RetryIntervals,
                request,
                options?.Description,
                resolved.OnNodeDeath ?? NodeDeathPolicy.Retry,
                options?.CorrelationId,
                options?.CausationId
            );
        }

        return seeds;
    }

    private static (
        MissedRunPolicy OnMissedRun,
        int GraceSeconds,
        CronOverlapPolicy OnOverlap
    ) _ResolveCreationPolicies(
        string function,
        JobFunctionRegistration? registration,
        SchedulerOptionsBuilder schedulerOptions
    )
    {
        var onMissedRun = registration?.OnMissedRun ?? schedulerOptions.DefaultMissedRunPolicy;
        var graceSeconds = registration?.MissedRunGraceSeconds ?? schedulerOptions.DefaultMissedRunGraceSeconds;
        var onOverlap = registration?.OnOverlap ?? schedulerOptions.DefaultOverlapPolicy;

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

        return (onMissedRun, graceSeconds, onOverlap);
    }
}
