// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Api.Contracts;

/// <summary>
/// API response view for a stored image resource. Extends <see cref="FileView"/> with
/// image-specific metadata (dimensions, caption).
/// </summary>
public sealed class ImageView : FileView
{
    /// <summary>Optional caption or alt-text for the image.</summary>
    public string? Caption { get; init; }

    /// <summary>Image width in pixels, or <see langword="null"/> when not available.</summary>
    public int? Width { get; init; }

    /// <summary>Image height in pixels, or <see langword="null"/> when not available.</summary>
    public int? Height { get; init; }

    /// <summary>
    /// Maps a domain <see cref="Image"/> to an <see cref="ImageView"/>.
    /// Returns <see langword="null"/> when <paramref name="operand"/> is <see langword="null"/>.
    /// </summary>
    [return: NotNullIfNotNull(nameof(operand))]
    public static ImageView? FromImage(Image? operand) => operand;

    [return: NotNullIfNotNull(nameof(operand))]
    public static implicit operator ImageView?(Image? operand)
    {
        if (operand is null)
        {
            return null;
        }

        return new()
        {
            Id = operand.Id,
            FileName = operand.DisplayName,
            Url = operand.Url,
            Length = operand.Length,
            ContentType = operand.ContentType,
            DateUploaded = operand.DateUploaded,
            Caption = operand.Caption,
            Width = operand.Width,
            Height = operand.Height,
        };
    }
}
