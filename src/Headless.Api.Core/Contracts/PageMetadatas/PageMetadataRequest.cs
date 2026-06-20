// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Primitives;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Api.Contracts;

/// <summary>
/// API request contract for page SEO metadata. All properties are optional; validate with
/// <see cref="FluentValidatorPageMetadataExtensions.PageMetadata{T}"/> to enforce field length limits.
/// Maps to the domain <see cref="PageMetadata"/> primitive via <see cref="ToPageMetadata"/> or the
/// implicit conversion.
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

internal sealed class PageMetadataRequestValidator : AbstractValidator<PageMetadataRequest>
{
    public PageMetadataRequestValidator()
    {
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(PageMetadataConstants.Slugs.MaxLength);
        RuleFor(x => x.MetaTitle).NotEmpty().MaximumLength(PageMetadataConstants.MetaTitles.MaxLength);
        RuleFor(x => x.MetaDescription).NotEmpty().MaximumLength(PageMetadataConstants.MetaDescriptions.MaxLength);
        RuleFor(x => x.MetaKeywords).MaximumElements(PageMetadataConstants.MetaKeywords.MaxElements);
        RuleFor(x => x.Tags).MaximumElements(PageMetadataConstants.Tags.MaxElements);
    }
}

[PublicAPI]
public static class FluentValidatorPageMetadataExtensions
{
    /// <summary>
    /// Applies <see cref="PageMetadataRequestValidator"/> to validate length constraints on all
    /// populated fields. The validator is skipped when the <paramref name="builder"/> value is
    /// <see langword="null"/> (the property is treated as not provided).
    /// </summary>
    public static IRuleBuilderOptions<T, PageMetadataRequest?> PageMetadata<T>(
        this IRuleBuilder<T, PageMetadataRequest?> builder
    )
    {
        return builder.SetValidator(new PageMetadataRequestValidator()!).When(x => x is not null);
    }
}
