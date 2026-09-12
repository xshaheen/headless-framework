// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Collections.Frozen;
using Headless.Checks;

namespace Headless.Api.Surfaces;

/// <summary>
/// Options for configuring API surfaces across the application.
/// </summary>
public sealed class ApiSurfaceOptions
{
    private readonly ConcurrentDictionary<string, ApiSurfaceDescriptor> _surfaces = new(
        StringComparer.OrdinalIgnoreCase
    );

    /// <summary>
    /// Adds or configures an API surface partition.
    /// </summary>
    /// <param name="surfaceName">The unique name of the surface.</param>
    /// <param name="configure">Optional configuration callback.</param>
    /// <returns>This options instance for chaining.</returns>
    public ApiSurfaceOptions AddSurface(string surfaceName, Action<ApiSurfaceDescriptor>? configure = null)
    {
        Argument.IsNotNullOrWhiteSpace(surfaceName);

        var descriptor = _surfaces.GetOrAdd(surfaceName, name => new ApiSurfaceDescriptor(name));
        configure?.Invoke(descriptor);

        return this;
    }

    /// <summary>
    /// Gets all registered surface descriptors.
    /// </summary>
    public IReadOnlyCollection<ApiSurfaceDescriptor> Surfaces => _surfaces.Values.ToList();

    /// <summary>
    /// Attempts to retrieve a surface descriptor by name.
    /// </summary>
    public bool TryGetSurface(string surfaceName, out ApiSurfaceDescriptor? descriptor)
    {
        return _surfaces.TryGetValue(surfaceName, out descriptor);
    }

    /// <summary>
    /// Builds a frozen lookup map from surface name to <see cref="ApiSurfaceFeature"/> for zero-allocation request handling.
    /// </summary>
    public FrozenDictionary<string, ApiSurfaceFeature> BuildFeatureLookup()
    {
        var dictionary = new Dictionary<string, ApiSurfaceFeature>(StringComparer.OrdinalIgnoreCase);
        foreach (var descriptor in _surfaces.Values)
        {
            dictionary[descriptor.SurfaceName] = descriptor.ToFeature();
        }

        return dictionary.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }
}
