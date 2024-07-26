using System.Text.Json;
using Framework.BuildingBlocks.Constants;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Framework.BuildingBlocks.Primitives;

public sealed class Image : File
{
    /// <summary>A brief description of the image, intended for display as a caption or alt text.</summary>
    public string? Caption { get; init; }

    /// <summary>Image width in pixels.</summary>
    public int? Width { get; init; }

    /// <summary>Image height in pixels.</summary>
    public int? Height { get; init; }
}

public static class ImageConstants
{
    public const int IdNameMaxLength = FileConstants.IdNameMaxLength;
    public const int DisplayNameMaxLength = FileConstants.DisplayNameMaxLength;
    public const int SavedNameMaxLength = FileConstants.SavedNameMaxLength;
    public const int UrlMaxLength = FileConstants.UrlMaxLength;
    public const int ContentTypeMaxLength = FileConstants.ContentTypeMaxLength;
    public const int Md5MaxLength = FileConstants.Md5MaxLength;
    public const int CaptionMaxLength = 1000;
}

#region Entity Framework

public sealed class ImageConverter()
    : ValueConverter<Image?, string?>(
        v => JsonSerializer.Serialize(v, PlatformJsonConstants.DefaultInternalJsonOptions),
        v => v == null ? null : JsonSerializer.Deserialize<Image>(v, PlatformJsonConstants.DefaultInternalJsonOptions)
    );

#endregion
