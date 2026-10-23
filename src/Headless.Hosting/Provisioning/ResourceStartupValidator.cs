// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.Hosting;

/// <summary>
/// Verifies, before any hosted service starts, every provisioned resource whose startup check is
/// <see cref="ResourceStartupCheck.FailStart" />, and fails the start naming each one that is missing or changed.
/// </summary>
/// <remarks>
/// The host never creates these resources: the deployment runs <c>resources provision</c>, and a development host
/// registers <c>AddDeployStepsOnStartup</c>, whose provisioning runs before this check. The <c>resources</c>,
/// <c>seed</c>, <c>deploy</c>, <c>check</c>, and <c>describe</c> commands skip it, because they run before or instead of
/// the deployment that creates the resources.
/// </remarks>
internal sealed class ResourceStartupValidator(
    IServiceProvider services,
    ResourceProvisioningStartupService? provisioning = null
) : IHeadlessStartupValidator
{
    public async Task ValidateAsync(CancellationToken cancellationToken)
    {
        if (provisioning is not null)
        {
            // A host that starts its services concurrently runs this beside the provisioning; verify what it made.
            await provisioning.WaitForInitializationAsync(cancellationToken).ConfigureAwait(false);
        }

        await ResourceStartupChecks
            .VerifyAsync(services, ResourceStartupCheck.FailStart, cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>
/// Verifies, once the host has started, every provisioned resource whose startup check is
/// <see cref="ResourceStartupCheck.GateReadiness" />. Until it passes, the <c>warmup</c> readiness check names each
/// resource that is missing, and the warm-up runner retries it with back-off.
/// </summary>
internal sealed class ResourceReadinessWarmup(IServiceProvider services) : IWarmup
{
    public string Name => "Resources";

    public Task WarmUpAsync(CancellationToken cancellationToken) =>
        ResourceStartupChecks.VerifyAsync(services, ResourceStartupCheck.GateReadiness, cancellationToken);
}

internal static class ResourceStartupChecks
{
    /// <summary>Verifies every registered resource whose effective startup check is <paramref name="check" />.</summary>
    /// <exception cref="ResourceProvisioningException">At least one resource is missing, changed, or unreachable.</exception>
    public static async Task VerifyAsync(
        IServiceProvider services,
        ResourceStartupCheck check,
        CancellationToken cancellationToken
    )
    {
        var options =
            services.GetService<IOptions<ResourceProvisioningOptions>>()?.Value ?? new ResourceProvisioningOptions();

        var provisioners = await ResourceProvisioningRunner
            .GetProvisionersAsync(services, cancellationToken)
            .ConfigureAwait(false);

        List<IResourceProvisioner> selected = [.. provisioners.Where(p => options.StartupCheckOf(p) == check)];

        if (selected.Count == 0)
        {
            return;
        }

        var outcomes = await ResourceProvisioningRunner
            .RunAsync(selected, ResourceOperation.Verify, timeout: null, cancellationToken)
            .ConfigureAwait(false);

        ResourceProvisioningRunner.ThrowIfFailed(outcomes, ResourceOperation.Verify);
    }
}
