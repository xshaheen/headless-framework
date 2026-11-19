// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Hosting.Schema;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Scenarios;

/// <summary>
/// Several lock nodes, each its own service provider as a separate process would have, all on one backend and one key
/// prefix, so they contend for the same locks and draw tokens from the same fence. The first node runs the schema
/// step a deploy would run, once.
/// </summary>
public sealed class LockNodes : IAsyncDisposable
{
    private readonly List<ServiceProvider> _providers = [];

    private LockNodes() { }

    public IReadOnlyList<IDistributedLock> Locks { get; private set; } = [];

    public static async Task<LockNodes> StartAsync(
        int count,
        Action<HeadlessDistributedLocksSetupBuilder> configure,
        Action<IServiceCollection>? configureServices,
        CancellationToken cancellationToken
    )
    {
        var nodes = new LockNodes();

        try
        {
            for (var i = 0; i < count; i++)
            {
                var services = new ServiceCollection();
                services.AddLogging();
                configureServices?.Invoke(services);
                services.AddHeadlessDistributedLocks(configure);
                nodes._providers.Add(services.BuildServiceProvider());
            }

            if (nodes._providers[0].GetService<SchemaRunner>() is { } schema)
            {
                await schema.ApplyAsync(cancellationToken).ConfigureAwait(false);
            }

            nodes.Locks = nodes._providers.ConvertAll(provider => provider.GetRequiredService<IDistributedLock>());

            return nodes;
        }
        catch
        {
            await nodes.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var provider in _providers)
        {
            await provider.DisposeAsync().ConfigureAwait(false);
        }
    }
}
