// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.ComponentModel.DataAnnotations.Schema;

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Kernel.Primitives;

[PublicAPI]
[ComplexType]
public class File
{
    /// <summary>Unique identifier for the file.</summary>
    public required string Id { get; init; }

    /// <summary>Actual file name.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Saved file name.</summary>
    public required string SavedName { get; init; }

    /// <summary>A public URL to reference the file. Updated automatically if file content changes.</summary>
    public required string Url { get; init; }

    /// <summary>Container name where the file is stored.</summary>
    public required string[] Container { get; init; }

    /// <summary>MIME content type of the file.</summary>
    public required string ContentType { get; init; }

    /// <summary>Date the file was uploaded.</summary>
    public required DateTimeOffset DateUploaded { get; init; }

    /// <summary>Size of the file in bytes.</summary>
    public required long Length { get; init; }

    /// <summary>Indicates whether the file is private.</summary>
    public required bool Private { get; init; }

    /// <summary>An MD5 hash of the file contents. This can be used to uniquely identify the file for caching purposes.</summary>
    public string? Md5 { get; init; }

    /// <summary>A set of arbitrary data that is typically used to store custom values.</summary>
    public Dictionary<string, object?>? Metadata { get; init; }
}
