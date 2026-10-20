// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Headless.Hosting;

/// <summary>
/// Provisions the resources in <see cref="IHostedLifecycleService.StartingAsync" />. It is registered ahead of every
/// other hosted service, so the resources exist before an initializer or a consumer reaches them. A host that starts
/// its services concurrently runs every <c>StartingAsync</c> at once, so a startup check of a resource awaits
/// <see cref="HostedInitializer.WaitForInitializationAsync" /> instead of relying on that order. The base swaps in a
/// fresh promise on every start, so a restarted host never reuses an earlier start's outcome.
/// </summary>
internal sealed class ResourceProvisioningStartupService(
    IServiceProvider services,
    IOptions<DeployStepsOnStartupOptions> options
) : HostedInitializer
{
    protected override bool RunOnStartup => options.Value.Resources != ResourceStartupAction.None;

    public override async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var purge = options.Value.Resources == ResourceStartupAction.ProvisionAndPurge;

        // Checked when the host starts, not at registration: the environment is only known once the host is built.
        if (
            purge
            && DestructiveOperationGuard.Refusal(
                services,
                "ResourceStartupAction.ProvisionAndPurge",
                "Use ResourceStartupAction.Provision"
            )
                is { } refusal
        )
        {
            throw new InvalidOperationException(refusal);
        }

        var provisioners = await ResourceProvisioningRunner
            .GetProvisionersAsync(services, cancellationToken)
            .ConfigureAwait(false);

        var provision = await ResourceProvisioningRunner
            .RunAsync(provisioners, ResourceOperation.Provision, timeout: null, cancellationToken)
            .ConfigureAwait(false);
        ResourceProvisioningRunner.ThrowIfFailed(provision, ResourceOperation.Provision);

        if (purge)
        {
            var purged = await ResourceProvisioningRunner
                .RunAsync(provisioners, ResourceOperation.Purge, timeout: null, cancellationToken)
                .ConfigureAwait(false);
            ResourceProvisioningRunner.ThrowIfFailed(purged, ResourceOperation.Purge);
        }
    }
}
