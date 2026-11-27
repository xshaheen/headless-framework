// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Fencing;
using Headless.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Leases;

/// <summary>
/// Fails the host at start when it serves neither half the module shows, so a module with nothing to display is caught
/// at deploy time instead of as two empty views.
/// </summary>
/// <remarks>
/// It checks the built container, not the service collection at <c>AddLeases</c> time, so Fencing and the lock
/// provider may be registered in any order relative to the module. It resolves two services and does no I/O.
/// </remarks>
internal sealed class LeasesDashboardStartupValidator(IServiceProvider services) : IHeadlessStartupValidator
{
    public Task ValidateAsync(CancellationToken cancellationToken)
    {
        if (services.GetService<IFencedLeaseInspector>() is null && LeasesServices.ResolveLock(services) is null)
        {
            throw new InvalidOperationException(
                "The Leases dashboard module (AddLeases) shows fenced leases and distributed locks, and this host "
                    + "registers neither. Call services.AddHeadlessFencing(...) with a provider, or "
                    + "services.AddHeadlessDistributedLocks(...) with a lock provider."
            );
        }

        return Task.CompletedTask;
    }
}
