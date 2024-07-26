using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using Framework.BuildingBlocks.Constants;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Framework.BuildingBlocks.Primitives;

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

public static class FileConstants
{
    public const int IdNameMaxLength = 100;
    public const int DisplayNameMaxLength = 250;
    public const int SavedNameMaxLength = 250;
    public const int UrlMaxLength = 2000;
    public const int ContentTypeMaxLength = 150;
    public const int Md5MaxLength = 32;
}

#region Entity Framework

public sealed class FileConverter()
    : ValueConverter<File?, string?>(
        v => JsonSerializer.Serialize(v, PlatformJsonConstants.DefaultInternalJsonOptions),
        v => v == null ? null : JsonSerializer.Deserialize<File>(v, PlatformJsonConstants.DefaultInternalJsonOptions)
    );

#endregion
