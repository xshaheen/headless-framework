// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation.Resources;
using FluentValidation.Results;

namespace FluentValidation;

[PublicAPI]
public static class UrlValidators
{
    extension<T>(IRuleBuilder<T, string?> rule)
    {
        public IRuleBuilderOptions<T, string?> Url()
        {
            return rule.Must(maybeUrl => maybeUrl is null || Uri.TryCreate(maybeUrl, UriKind.Absolute, out _))
                .WithErrorDescriptor(FluentValidatorErrorDescriber.Urls.InvalidUrl());
        }

        public IRuleBuilderOptions<T, string?> HttpUrl()
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

        public IRuleBuilderOptions<T, string?> HttpsOnlyUrl()
        {
            return rule.Must(maybeUrl =>
                {
                    if (maybeUrl is null)
                    {
                        return true;
                    }

                    return Uri.TryCreate(maybeUrl, UriKind.Absolute, out var uriResult)
                        && string.Equals(uriResult.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal);
                })
                .WithErrorDescriptor(FluentValidatorErrorDescriber.Urls.InvalidUrl());
        }

        public IRuleBuilderOptions<T, string?> FileUrl()
        {
            return rule.Must(maybeUrl =>
                {
                    if (maybeUrl is null)
                    {
                        return true;
                    }

                    return Uri.TryCreate(maybeUrl, UriKind.Absolute, out var uriResult)
                        && string.Equals(uriResult.Scheme, Uri.UriSchemeFile, StringComparison.Ordinal);
                })
                .WithErrorDescriptor(FluentValidatorErrorDescriber.Urls.InvalidUrl());
        }

        public IRuleBuilderOptions<T, string?> FtpUrl()
        {
            return rule.Must(maybeUrl =>
                {
                    if (maybeUrl is null)
                    {
                        return true;
                    }

                    return Uri.TryCreate(maybeUrl, UriKind.Absolute, out var uriResult)
                        && string.Equals(uriResult.Scheme, Uri.UriSchemeFtp, StringComparison.Ordinal);
                })
                .WithErrorDescriptor(FluentValidatorErrorDescriber.Urls.InvalidUrl());
        }

        public IRuleBuilderOptions<T, string?> MailtoUrl()
        {
            return rule.Must(maybeUrl =>
                {
                    if (maybeUrl is null)
                    {
                        return true;
                    }

                    return Uri.TryCreate(maybeUrl, UriKind.Absolute, out var uriResult)
                        && string.Equals(uriResult.Scheme, Uri.UriSchemeMailto, StringComparison.Ordinal);
                })
                .WithErrorDescriptor(FluentValidatorErrorDescriber.Urls.InvalidUrl());
        }

        public IRuleBuilderOptionsConditions<T, string?> CorsOrigin()
        {
            return rule.Custom(
                (maybeOrigin, context) =>
                {
                    if (maybeOrigin is null)
                    {
                        return;
                    }

                    if (string.Equals(maybeOrigin, "*", StringComparison.Ordinal))
                    {
                        return;
                    }

                    if (!Uri.TryCreate(maybeOrigin, UriKind.Absolute, out var uri))
                    {
                        _AddInvalidOriginFormatFailure(context, maybeOrigin);

                        return;
                    }

                    if (
                        !string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
                        && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
                    )
                    {
                        _AddInvalidOriginSchemeFailure(context, maybeOrigin, uri.Scheme);

                        return;
                    }

                    if (
                        uri.AbsolutePath is not "/" and not ""
                        || !string.IsNullOrEmpty(uri.Query)
                        || !string.IsNullOrEmpty(uri.Fragment)
                    )
                    {
                        _AddInvalidOriginPathFailure(context, maybeOrigin);

                        return;
                    }

                    if (maybeOrigin.EndsWith('/'))
                    {
                        _AddInvalidOriginTrailingSlashFailure(context, maybeOrigin);
                    }
                }
            );
        }
    }

    private static void _AddInvalidOriginFormatFailure<TObj>(ValidationContext<TObj> context, string origin)
    {
        var (code, description, severity) = FluentValidatorErrorDescriber.Urls.InvalidOriginFormat();

        var failure = new ValidationFailure(context.PropertyPath, description)
        {
            AttemptedValue = origin,
            ErrorCode = code,
            Severity = severity.ToSeverity(),
        };

        context.AddFailure(failure);
    }

    private static void _AddInvalidOriginSchemeFailure<TObj>(
        ValidationContext<TObj> context,
        string origin,
        string scheme
    )
    {
        var (code, description, severity) = FluentValidatorErrorDescriber.Urls.InvalidOriginScheme();

        var failure = new ValidationFailure(context.PropertyPath, description)
        {
            AttemptedValue = origin,
            ErrorCode = code,
            Severity = severity.ToSeverity(),
        };

        failure.FormattedMessagePlaceholderValues["Scheme"] = scheme;

        context.AddFailure(failure);
    }

    private static void _AddInvalidOriginPathFailure<TObj>(ValidationContext<TObj> context, string origin)
    {
        var (code, description, severity) = FluentValidatorErrorDescriber.Urls.InvalidOriginNotRootPath();

        var failure = new ValidationFailure(context.PropertyPath, description)
        {
            AttemptedValue = origin,
            ErrorCode = code,
            Severity = severity.ToSeverity(),
        };

        context.AddFailure(failure);
    }

    private static void _AddInvalidOriginTrailingSlashFailure<TObj>(ValidationContext<TObj> context, string origin)
    {
        var (code, description, severity) = FluentValidatorErrorDescriber.Urls.InvalidOriginTrailingSlash();

        var failure = new ValidationFailure(context.PropertyPath, description)
        {
            AttemptedValue = origin,
            ErrorCode = code,
            Severity = severity.ToSeverity(),
        };

        context.AddFailure(failure);
    }
}
