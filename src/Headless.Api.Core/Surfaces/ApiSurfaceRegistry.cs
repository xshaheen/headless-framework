// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Frozen;
using Headless.Checks;
using Microsoft.Extensions.Options;

namespace Headless.Api.Surfaces;

/// <summary>One immutable surface snapshot shared by routing, telemetry, and document generation in a host.</summary>
[PublicAPI]
public sealed class ApiSurfaceRegistry
{
    private readonly FrozenDictionary<string, ApiSurfaceFeature> _features;

    public ApiSurfaceRegistry(IOptions<ApiSurfaceOptions> options)
    {
        var surfaces = Argument
            .IsNotNull(options)
            .Value.Surfaces.Select(x => x.Build())
            .OrderBy(x => x.SurfaceName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Surfaces = Array.AsReadOnly(surfaces);
        _features = surfaces.ToFrozenDictionary(
            x => x.SurfaceName,
            x => new ApiSurfaceFeature(x),
            StringComparer.OrdinalIgnoreCase
        );
    }

    public IReadOnlyList<ApiSurfaceDescriptor> Surfaces { get; }

    /// <summary>Returns the configured surface using a case-insensitive name lookup.</summary>
    /// <exception cref="InvalidOperationException">The surface was not registered.</exception>
    public ApiSurfaceDescriptor GetRequiredSurface(string surfaceName) => GetRequiredFeature(surfaceName).Surface;

    internal ApiSurfaceFeature GetRequiredFeature(string surfaceName) =>
        _features.TryGetValue(surfaceName, out var feature)
            ? feature
            : throw new InvalidOperationException(
                $"API surface '{surfaceName}' has not been configured. Register it with AddHeadlessApiSurfaces."
            );
}
