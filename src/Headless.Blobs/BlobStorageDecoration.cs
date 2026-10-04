// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Blobs;

/// <summary>
/// A wrapper applied to every blob store <c>AddHeadlessBlobs</c> registers, through
/// <c>services.DecorateHeadlessBlobs(decoration)</c>.
/// </summary>
/// <param name="decorate">
/// Builds the wrapper when a store is first resolved: receives the store, its name (<see langword="null"/> for the
/// default store), and the service provider, and returns the store to hand out. Return the store unchanged to leave
/// it undecorated.
/// </param>
[PublicAPI]
public sealed class BlobStorageDecoration(Func<IBlobStorage, string?, IServiceProvider, IBlobStorage> decorate)
{
    private readonly Func<IBlobStorage, string?, IServiceProvider, IBlobStorage> _decorate = Argument.IsNotNull(
        decorate
    );

    private bool _applied;

    /// <summary>
    /// Gets the number of store registrations (default and named) this decoration wrapped, so a feature can fail
    /// startup when it expected to wrap a store and found none.
    /// </summary>
    public int DecoratedRegistrations { get; private set; }

    internal void Apply(IServiceCollection services)
    {
        if (_applied)
        {
            return;
        }

        _applied = true;

        if (services.TryDecorate<IBlobStorage>((inner, provider) => _decorate(inner, null, provider)))
        {
            DecoratedRegistrations++;
        }

        var storeNames = services
            .Where(static d => d.IsKeyedService && d.ServiceType == typeof(IBlobStorage))
            .Select(static d => d.ServiceKey)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // Keyed IPresignedUrlBlobStorage forwards resolve the keyed IBlobStorage, so they reach the wrapper without
        // being decorated themselves.
        foreach (var name in storeNames)
        {
            if (services.TryDecorateKeyed<IBlobStorage>(name, (inner, provider) => _decorate(inner, name, provider)))
            {
                DecoratedRegistrations++;
            }
        }
    }
}
