// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Api.Surfaces;

/// <summary>Identifies the request's surface and its configured defaults, not its effective authorization.</summary>
[PublicAPI]
public sealed class ApiSurfaceFeature(ApiSurfaceDescriptor surface)
{
    public ApiSurfaceDescriptor Surface { get; } = Argument.IsNotNull(surface);
    public string SurfaceName => Surface.SurfaceName;
}
