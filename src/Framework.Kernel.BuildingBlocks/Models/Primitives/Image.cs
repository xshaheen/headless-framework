// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.ComponentModel.DataAnnotations.Schema;

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Kernel.Primitives;

[PublicAPI]
[ComplexType]
public sealed class Image : File
{
    /// <summary>A brief description of the image, intended for display as a caption or alt text.</summary>
    public string? Caption { get; init; }

    /// <summary>Image width in pixels.</summary>
    public int? Width { get; init; }

    /// <summary>Image height in pixels.</summary>
    public int? Height { get; init; }
}
