// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Diagnostics.CodeAnalysis;
using Framework.Kernel.Primitives;

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Api.Contracts;

public sealed class PageMetadataView
{
    /// <summary>Lowercase, hyphenated identifier typically used in URLs.</summary>
    public required string? Slug { get; init; }

    /// <summary>Page title used for search engine optimization purposes.</summary>
    public required string? MetaTitle { get; init; }

    /// <summary>Page description used for search engine optimization purposes.</summary>
    public required string? MetaDescription { get; init; }

    /// <summary>Page keywords used for search engine optimization purposes.</summary>
    public required HashSet<string>? MetaKeywords { get; init; }

    /// <summary>List of arbitrary tags typically used as metadata to improve search results or associate a custom behavior.</summary>
    public required HashSet<string>? Tags { get; init; }

    [return: NotNullIfNotNull(nameof(operand))]
    public static PageMetadataView? FromPageMetadata(PageMetadata? operand) => operand;

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
