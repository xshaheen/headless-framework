// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Jobs;

/// <summary>
/// Collects one module's Jobs registrations for <c>services.ConfigureJobs(...)</c>. Each registration is recorded as
/// an immutable descriptor in the service collection and applied when the host's job registry is built, so a module
/// never calls <c>AddHeadlessJobs</c> itself.
/// </summary>
/// <remarks>
/// Not generic over the job entity types, unlike <see cref="JobsOptionsBuilder{TTimeJob,TCronJob}"/>, so a module can
/// contribute without knowing which entity types its host chose. In a host that never calls <c>AddHeadlessJobs</c>,
/// contributions stay inert.
/// </remarks>
[PublicAPI]
public sealed class JobsContributionBuilder
{
    private readonly IServiceCollection _services;

    internal JobsContributionBuilder(IServiceCollection services)
    {
        _services = services;
    }

    /// <summary>
    /// Contributes one assembly's generated job functions and middleware, for example
    /// <c>AddModule&lt;Billing.JobsModule&gt;()</c>. Contributing a module more than once is harmless.
    /// </summary>
    /// <typeparam name="TModule">The generated <see cref="IJobsModule"/> of the assembly.</typeparam>
    /// <returns>This builder, for chaining.</returns>
    public JobsContributionBuilder AddModule<TModule>()
        where TModule : IJobsModule
    {
        _services.AddJobsModuleContribution<TModule>();
        return this;
    }

    /// <summary>
    /// Tunes the deployment settings of one declared job, for example
    /// <c>Tune("billing.close-day", job =&gt; job.Concurrency(2))</c>.
    /// </summary>
    /// <remarks>
    /// The identity is checked when the host's job registry is built: an identity no registered module declares fails
    /// startup. <paramref name="configure"/> runs once, synchronously, during this call.
    /// </remarks>
    /// <param name="identity">The job's <c>[Job]</c> identity.</param>
    /// <param name="configure">Changes the job's deployment settings.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="identity"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public JobsContributionBuilder Tune(string identity, [InstantHandle] Action<JobTuningBuilder> configure)
    {
        _services.AddJobTuning(identity, configure);
        return this;
    }
}

/// <summary>One generated module that a contribution or the host asked the host's catalog to register.</summary>
/// <param name="ModuleType">The generated module type, which identifies the module across contributions.</param>
/// <param name="Register">Runs the module's generated registration against one host's catalog.</param>
internal sealed record JobsModuleContribution(Type ModuleType, Action<JobsCatalogBuilder> Register);

/// <summary>One <c>Tune</c> call recorded in the service collection.</summary>
internal sealed record JobsTuningContribution(JobTuning Tuning);

internal static class JobsContributionRecording
{
    public static void AddJobsModuleContribution<TModule>(this IServiceCollection services)
        where TModule : IJobsModule
    {
        services.AddSingleton(new JobsModuleContribution(typeof(TModule), static catalog => TModule.Register(catalog)));
    }

    public static void AddJobTuning(
        this IServiceCollection services,
        string identity,
        Action<JobTuningBuilder> configure
    )
    {
        Argument.IsNotNullOrWhiteSpace(identity);
        Argument.IsNotNull(configure);

        var builder = new JobTuningBuilder(identity);
        configure(builder);
        services.AddSingleton(new JobsTuningContribution(builder.Build()));
    }
}
