// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Frozen;
using Headless.Checks;
using Microsoft.Extensions.Options;

namespace Headless.Api.Surfaces;

/// <summary>One immutable surface snapshot shared by routing, telemetry, and document generation in a host.</summary>
[PublicAPI]
public sealed class ApiSurfaceRegistry
{
    private readonly FrozenDictionary<string, ApiSurfaceDescriptor> _surfaces;

    public ApiSurfaceRegistry(IOptions<ApiSurfaceOptions> options)
    {
        var surfaces = Argument
            .IsNotNull(options)
            .Value.Surfaces.Select(x => x.Build())
            .OrderBy(x => x.SurfaceName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Surfaces = Array.AsReadOnly(surfaces);
        _surfaces = surfaces.ToFrozenDictionary(x => x.SurfaceName, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ApiSurfaceDescriptor> Surfaces { get; }

    /// <summary>Returns the configured surface using a case-insensitive name lookup.</summary>
    /// <exception cref="InvalidOperationException">The surface was not registered.</exception>
    public ApiSurfaceDescriptor GetRequiredSurface(string surfaceName) =>
        _surfaces.TryGetValue(surfaceName, out var surface)
            ? surface
            : throw new InvalidOperationException(
                $"API surface '{surfaceName}' has not been configured. Register it with AddHeadlessApiSurfaces."
            );
}
