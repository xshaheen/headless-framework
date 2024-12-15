// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Framework.FluentValidation.Resources;

namespace Framework.FluentValidation;

[PublicAPI]
public static class UrlValidators
{
    public static IRuleBuilder<T, string?> Url<T>(this IRuleBuilder<T, string?> rule)
    {
        return rule.Must(maybeUrl => maybeUrl is null || Uri.TryCreate(maybeUrl, UriKind.Absolute, out _))
            .WithErrorDescriptor(FluentValidatorErrorDescriber.Urls.InvalidUrl());
    }

    public static IRuleBuilder<T, string?> HttpUrl<T>(this IRuleBuilder<T, string?> rule)
    {
        return rule.Must(maybeUrl =>
            {
                if (maybeUrl is null)
                {
                    return true;
                }

                return Uri.TryCreate(maybeUrl, UriKind.Absolute, out var uriResult)
                    && (
                        string.Equals(uriResult.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
                        || string.Equals(uriResult.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
                    );
            })
            .WithErrorDescriptor(FluentValidatorErrorDescriber.Urls.InvalidUrl());
    }
}
