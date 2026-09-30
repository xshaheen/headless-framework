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
    /// Contributes Jobs registrations from a module. The host applies every contribution, in the order it was added,
    /// when its job registry is built; a host that never calls <c>AddHeadlessJobs</c> ignores them.
    /// </summary>
    /// <remarks>
    /// <paramref name="configure"/> runs once, synchronously, during this call and only records descriptors. A module
    /// calls this from its own <c>Add{Module}</c> entry point instead of calling <c>AddHeadlessJobs</c>, which the host
    /// owns. A contribution counts whether it is added before or after the host's <c>AddHeadlessJobs</c> call, as long
    /// as it is added before the service provider is built.
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
}
