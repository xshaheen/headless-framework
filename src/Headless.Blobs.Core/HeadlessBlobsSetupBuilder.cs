// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Blobs;

/// <summary>
/// Root builder for <c>AddHeadlessBlobs</c>. Provider packages contribute deferred service registrations into
/// three slots — an optional default (at most one), named instances (unlimited, unique names), and cross-cutting
/// extensions. Nothing is registered into <see cref="Services"/> until the setup gates pass; contributions are
/// queued only.
/// </summary>
[PublicAPI]
public sealed class HeadlessBlobsSetupBuilder
{
    private readonly HashSet<string> _instanceNames = new(StringComparer.Ordinal);

    internal HeadlessBlobsSetupBuilder(IServiceCollection services)
    {
        Services = Argument.IsNotNull(services);
    }

    internal IServiceCollection Services { get; }

    internal List<Action<IServiceCollection>> DefaultExtensions { get; } = [];

    internal List<(string Name, Action<IServiceCollection> Action)> NamedExtensions { get; } = [];

    internal List<Action<IServiceCollection>> CrossCuttingExtensions { get; } = [];

    /// <summary>Queues the default (unkeyed) blob storage provider contribution.</summary>
    /// <param name="action">The provider's deferred service registration action.</param>
    public void RegisterDefaultProvider(Action<IServiceCollection> action)
    {
        Argument.IsNotNull(action);

        DefaultExtensions.Add(action);
    }

    /// <summary>Queues a cross-cutting contribution applied after the default and named providers.</summary>
    /// <param name="action">The deferred service registration action.</param>
    public void RegisterCrossCuttingExtension(Action<IServiceCollection> action)
    {
        Argument.IsNotNull(action);

        CrossCuttingExtensions.Add(action);
    }

    /// <summary>
    /// Adds an independently-configured named blob storage instance, resolvable as a keyed
    /// <see cref="IBlobStorage"/> service or through <see cref="IBlobStorageProvider"/>. Named instances never
    /// touch the default (unkeyed) <see cref="IBlobStorage"/>.
    /// </summary>
    /// <param name="name">The blob storage instance name. Must be non-empty and unique.</param>
    /// <param name="configure">Configuration action that selects exactly one provider for the instance.</param>
    /// <returns>The builder for chaining.</returns>
    public HeadlessBlobsSetupBuilder AddNamed(string name, Action<HeadlessBlobInstanceBuilder> configure)
    {
        Argument.IsNotNullOrWhiteSpace(name);
        Argument.IsNotNull(configure);

        if (!_instanceNames.Add(name))
        {
            throw new InvalidOperationException($"A named blob storage instance '{name}' is already configured.");
        }

        var instance = new HeadlessBlobInstanceBuilder(name);
        configure(instance);

        if (instance.RegistrationCount == 0)
        {
            throw new InvalidOperationException(
                $"Named blob storage instance '{name}' requires exactly one provider. "
                    + "Call one of `UseFileSystem`, `UseRedis`, `UseAws`, `UseCloudflareR2`, `UseAzure`, or `UseSsh`."
            );
        }

        // A second provider is rejected eagerly by RegisterProvider, so RegistrationCount is always 1 here.
        NamedExtensions.Add((name, instance.Action!));

        return this;
    }
}
