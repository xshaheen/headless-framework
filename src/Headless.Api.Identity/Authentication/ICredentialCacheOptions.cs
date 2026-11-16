// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Api.Identity;

/// <summary>The opt-in credential cache settings both Identity-backed schemes share.</summary>
internal interface ICredentialCacheOptions
{
    TimeSpan? CredentialCacheDuration { get; }

    int CredentialCacheMaxEntries { get; }
}

internal sealed class CredentialCacheOptionsValidator : AbstractValidator<ICredentialCacheOptions>
{
    public CredentialCacheOptionsValidator()
    {
        RuleFor(x => x.CredentialCacheDuration)
            .GreaterThanOrEqualTo(TimeSpan.Zero)
            .LessThanOrEqualTo(CredentialAuthenticationCache.MaxDuration)
            .When(x => x.CredentialCacheDuration.HasValue);
        RuleFor(x => x.CredentialCacheMaxEntries).GreaterThan(0);
    }
}
