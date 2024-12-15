// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Framework.Caching;

public sealed class InMemoryCacheOptions : CacheOptions
{
    /// <summary>Gets or sets the maximum number of items to store in the cache.</summary>
    public int? MaxItems { get; set; } = 10000;

    /// <summary>Gets or sets a value indicating whether values should be cloned during get and set to make sure that any cache entry changes are isolated.</summary>
    public bool CloneValues { get; set; }
}

public sealed class InMemoryCacheOptionsValidator : AbstractValidator<InMemoryCacheOptions>
{
    public InMemoryCacheOptionsValidator()
    {
        RuleFor(x => x.KeyPrefix).NotNull();
        RuleFor(x => x.MaxItems).GreaterThan(0);
    }
}
