// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Api.Contracts;

/// <summary>
/// API request contract for page SEO metadata. All properties are optional; validate with
/// the <c>PageMetadata</c> extension from <c>Headless.Api.FluentValidation</c> to enforce field length
/// limits. Maps to the domain <see cref="PageMetadata"/> primitive via <see cref="ToPageMetadata"/> or
/// the implicit conversion.
/// </summary>
public sealed class PageMetadataRequest
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

    /// <summary>Maps this request to the domain <see cref="PageMetadata"/> primitive.</summary>
    public PageMetadata ToPageMetadata() => this;

    /// <summary>
    /// Implicitly converts to the domain <see cref="PageMetadata"/> primitive.
    /// Returns <see langword="null"/> when <paramref name="operand"/> is <see langword="null"/>.
    /// </summary>
    [return: NotNullIfNotNull(nameof(operand))]
    public static implicit operator PageMetadata?(PageMetadataRequest? operand)
    {
        return operand is null
            ? null
            : new()
            {
                Slug = operand.Slug,
                MetaTitle = operand.MetaTitle,
                MetaDescription = operand.MetaDescription,
                MetaKeywords = operand.MetaKeywords,
                Tags = operand.Tags,
            };
    }
}
