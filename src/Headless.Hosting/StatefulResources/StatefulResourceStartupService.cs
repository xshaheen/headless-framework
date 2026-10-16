// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Headless.Hosting;

/// <summary>
/// Sets up the stateful resources in <see cref="IHostedLifecycleService.StartingAsync" />. It is registered ahead of
/// every other hosted service, so the resources exist before an initializer or a consumer reaches them. A host that
/// starts its services concurrently runs every <c>StartingAsync</c> at once, so a startup check of a resource awaits
/// <see cref="HostedInitializer.WaitForInitializationAsync" /> instead of relying on that order. The base swaps in a
/// fresh promise on every start, so a restarted host never reuses an earlier start's outcome.
/// </summary>
internal sealed class StatefulResourceStartupService(
    IServiceProvider services,
    IOptions<StatefulResourceStartupOptions> options
) : HostedInitializer
{
    public override async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var resources = await StatefulResourceRunner
            .GetResourcesAsync(services, cancellationToken)
            .ConfigureAwait(false);

        var setup = await StatefulResourceRunner
            .RunAsync(resources, StatefulResourceOperation.Setup, timeout: null, cancellationToken)
            .ConfigureAwait(false);
        StatefulResourceRunner.ThrowIfFailed(setup, StatefulResourceOperation.Setup);

        if (options.Value.Action == StatefulResourceStartupAction.ResetState)
        {
            var clear = await StatefulResourceRunner
                .RunAsync(resources, StatefulResourceOperation.Clear, timeout: null, cancellationToken)
                .ConfigureAwait(false);
            StatefulResourceRunner.ThrowIfFailed(clear, StatefulResourceOperation.Clear);
        }
    }
}

internal sealed class StatefulResourceStartupOptions
{
    public StatefulResourceStartupAction Action { get; set; } = StatefulResourceStartupAction.SetupOnly;
}
