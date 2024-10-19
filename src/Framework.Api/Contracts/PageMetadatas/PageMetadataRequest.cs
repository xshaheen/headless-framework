// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Diagnostics.CodeAnalysis;
using FluentValidation;
using Framework.FluentValidation;
using Framework.Kernel.BuildingBlocks.Models.Primitives;
using Framework.Kernel.Primitives;

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Api.Contracts;

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

    public PageMetadata ToPageMetadata() => this;

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

public sealed class PageMetadataRequestValidator : AbstractValidator<PageMetadataRequest>
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

public static class FluentValidatorPageMetadataExtensions
{
    public static IRuleBuilderOptions<T, PageMetadataRequest?> PageMetadata<T>(
        this IRuleBuilder<T, PageMetadataRequest?> builder
    )
    {
        return builder.SetValidator(new PageMetadataRequestValidator()!).When(x => x is not null);
    }
}
