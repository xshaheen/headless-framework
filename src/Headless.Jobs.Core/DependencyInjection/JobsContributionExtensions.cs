// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Jobs;

/// <summary>Lets a module contribute Jobs registrations without owning the host's Jobs setup.</summary>
[PublicAPI]
public static class JobsContributionExtensions
{
    /// <summary>
    /// Contributes Jobs registrations from a module. The host's <c>AddHeadlessJobs</c> call applies every
    /// contribution in the order it was added; a host that never calls it ignores them.
    /// </summary>
    /// <remarks>
    /// <paramref name="configure"/> runs once, synchronously, during this call and only records descriptors. A module
    /// calls this from its own <c>Add{Module}</c> entry point instead of calling <c>AddHeadlessJobs</c>, which the host
    /// owns. The process-wide job catalog closes when the first host's <c>AddHeadlessJobs</c> call completes, so a
    /// contribution must be added before that call; one added afterwards fails when the host's job registry is first
    /// resolved rather than being silently dropped.
    /// </remarks>
    /// <param name="services">The host's service collection.</param>
    /// <param name="configure">Adds this module's registrations.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
    public static IServiceCollection ConfigureJobs(
        this IServiceCollection services,
        [InstantHandle] Action<JobsContributionBuilder> configure
    )
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(configure);

        configure(new JobsContributionBuilder(services));

        return services;
    }

    /// <summary>Registers every contributed module recorded so far, in contribution order.</summary>
    internal static void DrainContributions(IServiceCollection services)
    {
        foreach (var descriptor in services)
        {
            if (descriptor.ImplementationInstance is JobsModuleContribution contribution)
            {
                JobFunctionProvider.RegisterModule(contribution.ModuleType, contribution.Register);
            }
        }
    }

    /// <summary>
    /// Fails for a contribution whose module never joined the catalog, which happens when it was recorded after the
    /// catalog closed. Without this the module's jobs would be missing at runtime with no error.
    /// </summary>
    internal static void EnsureContributionsRegistered(IEnumerable<JobsModuleContribution> contributions)
    {
        foreach (var contribution in contributions)
        {
            if (!JobFunctionProvider.IsModuleRegistered(contribution.ModuleType))
            {
                throw new InvalidOperationException(
                    $"Jobs module '{contribution.ModuleType.FullName}' was contributed with ConfigureJobs after the "
                        + "process-wide job catalog closed. Call ConfigureJobs before the first AddHeadlessJobs call "
                        + "in this process."
                );
            }
        }
    }
}
