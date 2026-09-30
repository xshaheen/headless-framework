// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.Jobs;

/// <summary>
/// Collects one module's Jobs registrations for <c>services.ConfigureJobs(...)</c>. Each registration is recorded as
/// an immutable descriptor in the service collection and applied by the host's Jobs setup, so a module never calls
/// <c>AddHeadlessJobs</c> itself.
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
        _services.AddSingleton(new JobsModuleContribution(typeof(TModule), static () => TModule.Register()));
        return this;
    }
}

/// <summary>One generated module that a <c>ConfigureJobs</c> contribution asked the host to register.</summary>
/// <param name="ModuleType">The generated module type, which identifies the module across contributions.</param>
/// <param name="Register">Runs the module's generated registration.</param>
internal sealed record JobsModuleContribution(Type ModuleType, Action Register);
