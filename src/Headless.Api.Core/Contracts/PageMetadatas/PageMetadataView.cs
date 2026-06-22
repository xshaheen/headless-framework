// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Api.Contracts;

/// <summary>
/// API response view for page SEO metadata. Maps the domain <see cref="PageMetadata"/> primitive
/// to a serializable shape.
/// </summary>
public sealed class PageMetadataView
{
    /// <summary>Lowercase, hyphenated identifier typically used in URLs.</summary>
    public string? Slug { get; init; }

    /// <summary>Page title used for search engine optimization purposes.</summary>
    public string? MetaTitle { get; init; }

    /// <summary>Page description used for search engine optimization purposes.</summary>
    public string? MetaDescription { get; init; }

    /// <summary>Page keywords used for search engine optimization purposes.</summary>
    public HashSet<string>? MetaKeywords { get; init; }

    /// <summary>List of arbitrary tags typically used as metadata to improve search results or associate a custom behavior.</summary>
    public HashSet<string>? Tags { get; init; }

    /// <summary>
    /// Maps a domain <see cref="PageMetadata"/> to a <see cref="PageMetadataView"/>.
    /// Returns <see langword="null"/> when <paramref name="operand"/> is <see langword="null"/>.
    /// </summary>
    [return: NotNullIfNotNull(nameof(operand))]
    public static PageMetadataView? FromPageMetadata(PageMetadata? operand) => operand;

    /// <summary>
    /// Implicitly converts a domain <see cref="PageMetadata"/> to a <see cref="PageMetadataView"/>.
    /// Returns <see langword="null"/> when <paramref name="operand"/> is <see langword="null"/>.
    /// </summary>
    [return: NotNullIfNotNull(nameof(operand))]
    public static implicit operator PageMetadataView?(PageMetadata? operand)
    {
        if (operand is null)
        {
            return null;
        }

        return new()
        {
            Slug = operand.Slug,
            MetaTitle = operand.MetaTitle,
            MetaDescription = operand.MetaDescription,
            MetaKeywords = operand.MetaKeywords,
            Tags = operand.Tags,
        };
    }
}
