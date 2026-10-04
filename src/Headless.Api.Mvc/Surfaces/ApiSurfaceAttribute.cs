// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Api;

/// <summary>
/// Associates a controller with a specific API surface partition.
/// </summary>
[PublicAPI]
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class ApiSurfaceAttribute(string surfaceName) : Attribute, IApiSurfaceMetadata
{
    /// <inheritdoc />
    public string SurfaceName { get; } = Argument.IsNotNullOrWhiteSpace(surfaceName);
}
