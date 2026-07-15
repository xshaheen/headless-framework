// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Primitives;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Api.Contracts;

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
    /// <returns>The rule builder so that additional calls can be chained.</returns>
    public static IRuleBuilderOptions<T, PageMetadataRequest?> PageMetadata<T>(
        this IRuleBuilder<T, PageMetadataRequest?> builder
    )
    {
        return builder.SetValidator(new PageMetadataRequestValidator()!).When(x => x is not null);
    }
}
