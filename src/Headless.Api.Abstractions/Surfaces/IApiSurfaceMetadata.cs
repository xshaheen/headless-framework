// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Api.Surfaces;

/// <summary>
/// Marks an endpoint or controller as belonging to a named API surface partition.
/// </summary>
public interface IApiSurfaceMetadata
{
    /// <summary>
    /// Gets the unique identifier of the API surface (e.g., "Portal", "Console", "Partner", "Public").
    /// </summary>
    string SurfaceName { get; }
}
