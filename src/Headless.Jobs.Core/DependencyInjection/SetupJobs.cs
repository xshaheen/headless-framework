// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Headless.CommitCoordination;
using Headless.Coordination;
using Headless.DistributedLocks;
using Headless.Jobs.BackgroundServices;
using Headless.Jobs.Coordination;
using Headless.Jobs.Dispatcher;
using Headless.Jobs.Entities;
using Headless.Jobs.Instrumentation;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Internal;
using Headless.Jobs.JobsThreadPool;
using Headless.Jobs.Managers;
using Headless.Jobs.MultiTenancy;
using Headless.Jobs.Provider;
using Headless.Jobs.Temps;
using Headless.Jobs.Transactions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Jobs;

/// <summary>
/// Registration entry point for the Jobs subsystem. <c>AddHeadlessJobs</c> composes the scheduler,
/// managers, background services, and the per-host <see cref="JobsRequestSerializationOptions"/> from a
/// single <see cref="JobsOptionsBuilder{TTimeJob,TCronJob}"/> callback.
/// </summary>
public static class SetupJobs
{
    private static readonly TimeSpan _MaximumPostCommitDrainTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Registers the Jobs subsystem using the default <see cref="TimeJobEntity"/> and
    /// <see cref="CronJobEntity"/> entity types. Equivalent to
    /// <c>AddHeadlessJobs&lt;TimeJobEntity, CronJobEntity&gt;(optionsBuilder)</c>.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="optionsBuilder">
    /// Optional callback that configures the Jobs subsystem (operational store, scheduler, retries,
    /// request serialization, dashboard, discovery). When omitted, in-memory defaults are used.
    /// </param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddHeadlessJobs(
        this IServiceCollection services,
        Action<JobsOptionsBuilder<TimeJobEntity, CronJobEntity>>? optionsBuilder = null
    )
    {
        return services.AddHeadlessJobs<TimeJobEntity, CronJobEntity>(optionsBuilder);
    }

    /// <summary>
    /// Registers the Jobs subsystem with application-specific time and cron job entity types: managers,
    /// the <c>IJobScheduler</c> facade, background services (unless disabled), the in-memory persistence
    /// default (replaced by durable providers such as <c>UseEntityFramework</c>), and the per-host
    /// <see cref="JobsRequestSerializationOptions"/> singleton. The <paramref name="optionsBuilder"/>
    /// callback also completes job-function discovery: every <c>AddJobsDiscovery</c> assembly must be
    /// registered inside it, after which the host's function registry is frozen.
    /// </summary>
    /// <typeparam name="TTimeJob">The application's concrete time job entity type.</typeparam>
    /// <typeparam name="TCronJob">The application's concrete cron job entity type.</typeparam>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="optionsBuilder">
    /// Optional callback that configures the Jobs subsystem (operational store, scheduler, retries,
    /// request serialization, dashboard, discovery). When omitted, in-memory defaults are used.
    /// </param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddHeadlessJobs<TTimeJob, TCronJob>(
        this IServiceCollection services,
        Action<JobsOptionsBuilder<TTimeJob, TCronJob>>? optionsBuilder = null
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        var tickerExecutionContext = new JobsExecutionContext();
        var schedulerOptionsBuilder = new SchedulerOptionsBuilder();
        var optionInstance = new JobsOptionsBuilder<TTimeJob, TCronJob>(
            tickerExecutionContext,
            schedulerOptionsBuilder
        );
        var discoveryParticipant = JobFunctionProvider.BeginDiscovery();
        try
        {
            optionsBuilder?.Invoke(optionInstance);
            _RegisterTenancyMiddleware(discoveryParticipant);
        }
        catch (Exception exception)
        {
            JobFunctionProvider.AbandonDiscovery(discoveryParticipant, exception);
            throw;
        }

        JobFunctionProvider.CompleteDiscovery(discoveryParticipant);

        // The pickup lease is stamped as LockedUntil = now + LeaseDuration; a non-positive duration would write a
        // lease that is already expired, defeating duplicate-suppression entirely (KTD2).
        Ensure.True(
            schedulerOptionsBuilder.LeaseDuration > TimeSpan.Zero,
            "SchedulerOptionsBuilder.LeaseDuration must be greater than TimeSpan.Zero."
        );
        _ = schedulerOptionsBuilder.ResolveCancellationObservationInterval();
        Ensure.True(
            schedulerOptionsBuilder.PostCommitDrainTimeout > TimeSpan.Zero,
            "SchedulerOptionsBuilder.PostCommitDrainTimeout must be greater than TimeSpan.Zero."
        );
        Ensure.True(
            schedulerOptionsBuilder.PostCommitDrainTimeout <= _MaximumPostCommitDrainTimeout,
            "SchedulerOptionsBuilder.PostCommitDrainTimeout must not exceed 5 minutes."
        );
        Ensure.True(
            schedulerOptionsBuilder.MaxConcurrency > 0,
            "SchedulerOptionsBuilder.MaxConcurrency must be greater than zero."
        );
        Ensure.True(
            schedulerOptionsBuilder.MaxLongRunningConcurrency > 0,
            "SchedulerOptionsBuilder.MaxLongRunningConcurrency must be greater than zero."
        );
        var retryOptions = optionInstance.RetryOptions;
        services.Configure<JobsRetryOptions, JobsRetryOptionsValidator>(configured =>
        {
            configured.RetryStrategy = retryOptions.RetryStrategy;
            configured.OnExhausted = retryOptions.OnExhausted;
            configured.OnExhaustedTimeout = retryOptions.OnExhaustedTimeout;
        });

        // The soft lease/fallback ordering warning (LeaseDuration < FallbackIntervalChecker) is emitted at startup by
        // JobsInitializationHostedService, where an ILogger is available.

        // Per-host request serialization settings (JSON options, GZip, decompression cap). Registered as a
        // singleton so components resolve THIS host's settings — never process-global state shared across hosts.
        var requestSerializationOptions = new JobsRequestSerializationOptions
        {
            SerializerOptions = optionInstance.RequestJsonSerializerOptions ?? JsonSerializerOptions.Default,
            UseGZipCompression = optionInstance.RequestGZipCompressionEnabled,
            MaxDecompressedRequestBytes = optionInstance.RequestGZipMaxDecompressedBytes,
        };
        services.AddSingleton(requestSerializationOptions);

        // Persisted job/cron primary keys are stamped via IGuidGenerator (Version7 default) instead of random
        // Guid.NewGuid() so they stay index-friendly. Idempotent: TryAdd-based, so a host that already registered it wins.
        services.AddHeadlessGuidGenerator();

        services.AddSingleton<ITimeJobManager<TTimeJob>, JobsManager<TTimeJob, TCronJob>>();
        services.AddSingleton<ICronJobManager<TCronJob>, JobsManager<TTimeJob, TCronJob>>();
        services.AddSingleton<IJobScheduler, JobScheduler<TTimeJob, TCronJob>>();
        services.AddSingleton<IInternalJobManager, InternalJobsManager<TTimeJob, TCronJob>>();
        // Default owner identity for the in-memory path + always-on instrumentation; the durable path overrides it.
        services.TryAddSingleton<IJobsOwnerIdentity, DefaultJobsOwnerIdentity>();
        services.AddSingleton(new CronScheduleCache(schedulerOptionsBuilder.SchedulerTimeZone));
        services.AddSingleton<
            IJobPersistenceProvider<TTimeJob, TCronJob>,
            JobsInMemoryPersistenceProvider<TTimeJob, TCronJob>
        >();
        services.AddSingleton<IJobsNotificationHubSender, NoOpJobsNotificationHubSender>();
        // Null-coordinator fallback so the JobsManager resolves and takes the direct path in standalone Jobs hosts
        // (no CommitCoordination, no Messaging). AddCommitCoordination's unconditional registration wins when present.
        services.TryAddSingleton<ICurrentCommitCoordinator, JobsNullCommitCoordinator>();
        services.TryAddSingleton(TimeProvider.System);

        // Per-host tenancy DI (see _AddTenancyServices): runs on EVERY call so a second host built after the process-
        // global middleware registry froze still resolves and dispatches the tenancy middleware.
        _AddTenancyServices(services);

        // Jobs-scoped distributed lock. The Core-layer UseDistributedLock extension stashed a single deferred keyed
        // registration on the builder (last-wins), replayed here; the NullDistributedLock fallback is always present
        // so the guard sites can resolve a keyed IDistributedLock even when UseStorageLock is off. The lock is
        // best-effort duplicate-suppression — never the correctness boundary for job-row ownership.
        optionInstance.LockRegistrationAction?.Invoke(services);
        services.TryAddKeyedSingleton<IDistributedLock, NullDistributedLock>(JobsKeys.LockProvider);

        // Core initialization — registered before background services to guarantee startup order
        services.AddHostedService<JobsInitializationHostedService>();

        // Only register background services if enabled (default is true)
        if (optionInstance.RegisterBackgroundServices)
        {
            services.AddSingleton<JobsSchedulerBackgroundService>();
            services.AddSingleton<IJobsHostScheduler>(provider =>
                provider.GetRequiredService<JobsSchedulerBackgroundService>()
            );
            services.AddHostedService(provider => provider.GetRequiredService<JobsSchedulerBackgroundService>());
            services.AddHostedService(provider => provider.GetRequiredService<JobsFallbackBackgroundService>());
            services.AddSingleton<JobsFallbackBackgroundService>();
            services.AddSingleton<JobsExecutionTaskHandler>();
            services.AddSingleton<JobsExecutionCancellationRegistry>();
            services.AddSingleton<IJobsDispatcher, JobsDispatcher>();
            services.AddSingleton(sp =>
            {
                var notification = sp.GetRequiredService<IJobsNotificationHubSender>();
                var notifyDebounce = new SoftSchedulerNotifyDebounce(notification.UpdateActiveThreads);

                return new JobsTaskScheduler(
                    schedulerOptionsBuilder.MaxConcurrency,
                    sp.GetRequiredService<TimeProvider>(),
                    schedulerOptionsBuilder.MaxLongRunningConcurrency,
                    schedulerOptionsBuilder.IdleWorkerTimeOut,
                    notifyDebounce,
                    sp.GetRequiredService<ILogger<JobsTaskScheduler>>()
                );
            });
        }
        else
        {
            // Register NoOp implementations when background services are disabled
            services.AddSingleton<IJobsHostScheduler, NoOpJobsHostScheduler>();
            services.AddSingleton<IJobsDispatcher, NoOpJobsDispatcher>();
        }

        services.AddSingleton<IJobFunctionConcurrencyGate, JobFunctionConcurrencyGate>();
        services.AddSingleton<IJobsInstrumentation, LoggerInstrumentation>();
        services.TryAddSingleton<JobFunctionRegistry>(provider =>
            JobFunctionProvider.CreateHostRegistry(provider.GetService<IConfiguration>())
        );

        optionInstance.ExternalProviderConfigServiceAction?.Invoke(services);
        optionInstance.DashboardServiceAction?.Invoke(services, requestSerializationOptions);

        // The durable operational store opts into coordinated membership; wire it after the provider's own
        // services are registered so the require-provider check sees the coordination registration.
        if (optionInstance.RequiresCoordinatedMembership)
        {
            _AddCoordinatedDurablePath(services);
        }

        if (optionInstance.JobExceptionHandlerType != null)
        {
            services.AddSingleton(typeof(IJobExceptionHandler), optionInstance.JobExceptionHandlerType);
        }

        services.AddSingleton(_ => optionInstance);
        services.AddSingleton(_ => tickerExecutionContext);
        services.AddSingleton(_ => schedulerOptionsBuilder);
        services.AddSingleton(_ => retryOptions);
        return services;
    }

    // Middleware identity strings mirror the source generator's `{assembly}:{fully-qualified-type}` shape so the frozen
    // registry orders the hand-registered tenancy middleware deterministically alongside generated declarations.
    private const string _TenancyScheduleMiddlewareIdentity =
        "Headless.Jobs.Core:Headless.Jobs.MultiTenancy.TenantPropagationScheduleMiddleware";
    private const string _TenancyExecuteMiddlewareIdentity =
        "Headless.Jobs.Core:Headless.Jobs.MultiTenancy.TenantRestoreExecuteMiddleware";

    // Hand-written dispatch: resolve the middleware from the bounded scope and no-op (call next) when it is absent, so
    // JobsManager's EmptyServiceProvider unit path and any host that never registered the middleware type stay a no-op.
    private static readonly JobScheduleMiddlewareDispatch _TenancyScheduleDispatch = static async (
        context,
        next,
        cancellationToken
    ) =>
    {
        var middleware = context.Services.GetService<TenantPropagationScheduleMiddleware>();
        if (middleware is null)
        {
            await next(cancellationToken).ConfigureAwait(false);

            return;
        }

        await middleware.InvokeAsync(context, next, cancellationToken).ConfigureAwait(false);
    };

    private static readonly JobExecuteMiddlewareDispatch _TenancyExecuteDispatch = static async (
        context,
        next,
        cancellationToken
    ) =>
    {
        var middleware = context.Services.GetService<TenantRestoreExecuteMiddleware>();
        if (middleware is null)
        {
            await next(cancellationToken).ConfigureAwait(false);

            return;
        }

        await middleware.InvokeAsync(context, next, cancellationToken).ConfigureAwait(false);
    };

    // KTD1 process-global one-shot: only the fresh-discovery participant that wins the reservation inserts the tenancy
    // middleware pair into the frozen-once registry, so overlapping host configuration and post-freeze ExistingCatalog
    // hosts never double-insert (which would dispatch tenancy twice). A post-freeze call skips silently — never throws.
    private static void _RegisterTenancyMiddleware(JobFunctionProvider.DiscoveryParticipation participation)
    {
        if (
            participation != JobFunctionProvider.DiscoveryParticipation.Participant
            || !JobMiddlewareRegistry.TryReserveTenancyRegistration()
        )
        {
            return;
        }

        JobMiddlewareRegistry.RegisterSchedule(
            _TenancyScheduleMiddlewareIdentity,
            function: null,
            JobMiddlewarePriority.Tenancy,
            _TenancyScheduleDispatch
        );
        JobMiddlewareRegistry.RegisterExecute(
            _TenancyExecuteMiddlewareIdentity,
            function: null,
            JobMiddlewarePriority.Tenancy,
            _TenancyExecuteDispatch
        );
    }

    // Per-host DI (KTD1): the middleware types plus the tenant-context primitives, mirroring Headless.Messaging.Core's
    // Setup. NullCurrentTenant remains the fallback that a real Headless.Api / EF / consumer registration strips;
    // CurrentTenant.Id stays null until a caller or seam populates the AsyncLocal, so a strict-mode enqueue with no
    // tenant still fails fast. Runs on every AddHeadlessJobs so a second host still resolves and dispatches the middleware.
    private static void _AddTenancyServices(IServiceCollection services)
    {
        services.TryAddSingleton<TenantPropagationScheduleMiddleware>();
        services.TryAddSingleton<TenantRestoreExecuteMiddleware>();
        services.TryAddSingleton<ICurrentTenantAccessor>(AsyncLocalCurrentTenantAccessor.Instance);
        services.AddOrReplaceFallbackSingleton<ICurrentTenant, NullCurrentTenant, CurrentTenant>();
    }

    private static void _AddCoordinatedDurablePath(IServiceCollection services)
    {
        // Fail-fast (R5): the durable path requires a real coordination provider. INodeMembership resolves by
        // last-wins registration (AddHeadlessCoordination uses AddSingleton, not TryAdd), so a consumer package may
        // register a NullNodeMembership fallback first and have coordination replace it. Inspect the LAST descriptor
        // — the one DI will actually resolve — not the first; FirstOrDefault would see the null fallback and reject a
        // valid config. A factory-registered NullNodeMembership cannot be detected pre-build, so AddHeadlessCoordination
        // must be registered before AddHeadlessJobs(... UseEntityFramework(...)).
        var membershipDescriptor = services.LastOrDefault(descriptor =>
            descriptor.ServiceType == typeof(INodeMembership)
        );

        var isNullProvider =
            membershipDescriptor?.ImplementationType == typeof(NullNodeMembership)
            || membershipDescriptor?.ImplementationInstance is NullNodeMembership;

        if (membershipDescriptor is null || isNullProvider)
        {
            throw new InvalidOperationException(
                "The durable Jobs operational store requires a coordination provider. Register one with "
                    + "AddHeadlessCoordination(...) before AddHeadlessJobs(... UseEntityFramework(...))."
            );
        }

        // Override the default owner identity (U1) with the node@incarnation adapter over INodeMembership.
        services.AddSingleton<IJobsOwnerIdentity, JobsOwnerIdentityAdapter>();

        // Event-driven dead-node recovery (U2, shared bridge) and the registration startup gate (R6).
        services.AddSingleton<JobsDeadOwnerReclaimer>();
        services.AddHostedService<DeadOwnerRecoveryBridge<JobsDeadOwnerReclaimer>>();
        services.AddHostedService<JobsCoordinationStartupGate>();
    }
}
