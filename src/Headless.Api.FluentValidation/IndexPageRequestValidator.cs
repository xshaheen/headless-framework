// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Primitives;

namespace Headless.Api;

internal sealed class IndexPageRequestValidator : AbstractValidator<IndexPageRequest>
{
    public IndexPageRequestValidator()
    {
        RuleFor(x => x.Index).PageIndex();
        // No upper bound: the largest page an endpoint serves is the consumer's policy, not the framework's.
        RuleFor(x => x.Size).GreaterThan(0);
    }
}

[PublicAPI]
public static class FluentValidatorIndexPageExtensions
{
#nullable disable // keep the builder nullability-agnostic: binds to nullable and non-nullable page properties, preserving the caller's nullability
    /// <summary>
    /// Applies <c>IndexPageRequestValidator</c>: <see cref="IndexPageRequest.Index"/> must be zero or greater and
    /// <see cref="IndexPageRequest.Size"/> must be greater than zero. A <see langword="null"/> page is treated as not
    /// provided and passes. Cap the size with your own rule, such as <c>RuleFor(x =&gt; x.Page!.Size).PageSize(50).When(x =&gt; x.Page is not null)</c>.
    /// </summary>
    /// <returns>The rule builder so that additional calls can be chained.</returns>
    public static IRuleBuilderOptions<T, IndexPageRequest> IndexPage<T>(this IRuleBuilder<T, IndexPageRequest> builder)
#nullable restore
    {
        // FluentValidation skips a child validator when the property is null, so a missing page passes.
        return builder.SetValidator(new IndexPageRequestValidator());
    }
}
