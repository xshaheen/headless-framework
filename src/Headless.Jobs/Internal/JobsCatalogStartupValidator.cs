// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Jobs.Internal;

/// <summary>
/// Builds the job catalog and validates the scheduling policies at startup on a host that skips Jobs startup
/// initialization, which otherwise does both. Without it, a duplicate job registration or an invalid policy surfaces at
/// the first enqueue instead of failing the host.
/// </summary>
/// <param name="services">
/// Provider the check resolves from inside <c>ValidateAsync</c>: the catalog is built on first resolution, after every
/// contribution has been recorded.
/// </param>
internal sealed class JobsCatalogStartupValidator(IServiceProvider services) : IHeadlessStartupValidator
{
    public Task ValidateAsync(CancellationToken cancellationToken)
    {
        _ = services.GetRequiredService<JobSchedulingPolicies>();

        return Task.CompletedTask;
    }
}
