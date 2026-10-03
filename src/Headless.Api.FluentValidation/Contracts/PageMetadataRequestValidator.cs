// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Primitives;

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
