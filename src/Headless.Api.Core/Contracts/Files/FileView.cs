// Copyright (c) Mahmoud Shaheen. All rights reserved.

using File = Headless.Primitives.File;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Api.Contracts;

/// <summary>
/// API response view for a stored file resource. Maps the domain <see cref="File"/> primitive
/// to a wire-safe shape that exposes only fields consumers need (no internal storage paths).
/// </summary>
public class FileView
{
    /// <summary>The unique identifier of the file resource.</summary>
    public required string Id { get; init; }

    /// <summary>The display name of the file (typically the original upload filename).</summary>
    public required string FileName { get; init; }

    /// <summary>The public URL from which the file can be downloaded or accessed.</summary>
    public required string Url { get; init; }

    /// <summary>The file size in bytes.</summary>
    public required long Length { get; init; }

    /// <summary>The MIME content type of the file (e.g., <c>image/png</c>, <c>application/pdf</c>).</summary>
    public required string ContentType { get; init; }

    /// <summary>The UTC timestamp at which the file was uploaded.</summary>
    public required DateTimeOffset DateUploaded { get; init; }

    /// <summary>
    /// Maps a domain <see cref="File"/> to a <see cref="FileView"/>.
    /// Returns <see langword="null"/> when <paramref name="operand"/> is <see langword="null"/>.
    /// </summary>
    [return: NotNullIfNotNull(nameof(operand))]
    public static FileView? FromFile(File? operand)
    {
        return operand;
    }

    /// <summary>
    /// Implicitly converts a domain <see cref="File"/> to a <see cref="FileView"/>.
    /// Returns <see langword="null"/> when <paramref name="operand"/> is <see langword="null"/>.
    /// </summary>
    [return: NotNullIfNotNull(nameof(operand))]
    public static implicit operator FileView?(File? operand)
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
        };
    }
}
