// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation.Results;
using Headless.FluentValidation;
using Headless.Primitives;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace

namespace FluentValidation;

/// <summary>FluentValidation extension rules for URL and CORS origin string properties.</summary>
[PublicAPI]
public static class UrlValidators
{
#nullable disable // keep the builder nullability-agnostic: binds to nullable and non-nullable properties, preserving the caller's nullability
    extension<T>(IRuleBuilder<T, string> rule)
    {
        /// <summary>
        /// Validates that the value is any absolute URI. Accepts every scheme, including
        /// <c>javascript:</c> and <c>data:</c>; use <c>HttpUrl</c> or <c>HttpsOnlyUrl</c>
        /// for any URL that will be rendered in markup or dereferenced.
        /// </summary>
        public IRuleBuilderOptions<T, string> Url()
        {
            return rule.Must(maybeUrl => maybeUrl is null || Uri.TryCreate(maybeUrl, UriKind.Absolute, out _))
                .WithErrorDescriptor(FluentValidatorErrorDescriber.Urls.InvalidUrl());
        }

        /// <summary>
        /// Validates that the value is an absolute <c>http://</c> or <c>https://</c> URL.
        /// Passes <see langword="null"/> through without failure.
        /// </summary>
        /// <returns>The rule builder options for chaining.</returns>
        public IRuleBuilderOptions<T, string> HttpUrl()
        {
            return _SchemeUrl(
                rule,
                scheme =>
                    string.Equals(scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
                    || string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
            );
        }

        /// <summary>
        /// Validates that the value is an absolute <c>https://</c> URL.
        /// Passes <see langword="null"/> through without failure.
        /// </summary>
        /// <returns>The rule builder options for chaining.</returns>
        public IRuleBuilderOptions<T, string> HttpsOnlyUrl()
        {
            return _SchemeUrl(rule, scheme => string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.Ordinal));
        }

        /// <summary>
        /// Validates that the value is an absolute <c>file://</c> URL.
        /// Passes <see langword="null"/> through without failure.
        /// </summary>
        /// <returns>The rule builder options for chaining.</returns>
        public IRuleBuilderOptions<T, string> FileUrl()
        {
            return _SchemeUrl(rule, scheme => string.Equals(scheme, Uri.UriSchemeFile, StringComparison.Ordinal));
        }

        /// <summary>
        /// Validates that the value is an absolute <c>ftp://</c> URL.
        /// Passes <see langword="null"/> through without failure.
        /// </summary>
        /// <returns>The rule builder options for chaining.</returns>
        public IRuleBuilderOptions<T, string> FtpUrl()
        {
            return _SchemeUrl(rule, scheme => string.Equals(scheme, Uri.UriSchemeFtp, StringComparison.Ordinal));
        }

        /// <summary>
        /// Validates that the value is an absolute <c>mailto:</c> URL.
        /// Passes <see langword="null"/> through without failure.
        /// </summary>
        /// <returns>The rule builder options for chaining.</returns>
        public IRuleBuilderOptions<T, string> MailtoUrl()
        {
            return _SchemeUrl(rule, scheme => string.Equals(scheme, Uri.UriSchemeMailto, StringComparison.Ordinal));
        }

        /// <summary>
        /// Validates that the value is a well-formed CORS origin: an absolute
        /// <c>http://</c> or <c>https://</c> URI with no path, query, fragment, or trailing slash
        /// (for example <c>https://example.com</c> or <c>https://example.com:8080</c>), or the
        /// wildcard <c>*</c>. Passes <see langword="null"/> through without failure.
        /// </summary>
        /// <remarks>
        /// Surrounding whitespace causes a format failure — trimmed values are never valid Origin
        /// headers and the raw string must match exactly.
        /// </remarks>
        /// <returns>The rule builder options for chaining.</returns>
        public IRuleBuilderOptionsConditions<T, string> CorsOrigin()
        {
            return rule.Custom(
                (maybeOrigin, context) =>
                {
                    if (maybeOrigin is null)
                    {
                        return;
                    }

                    // Surrounding whitespace parses (Uri.TryCreate trims) but never matches a real Origin header.
                    if (maybeOrigin.AsSpan().Trim().Length != maybeOrigin.Length)
                    {
                        _BuildOriginFailure(
                            context,
                            maybeOrigin,
                            FluentValidatorErrorDescriber.Urls.InvalidOriginFormat()
                        );

                        return;
                    }

                    if (string.Equals(maybeOrigin, "*", StringComparison.Ordinal))
                    {
                        return;
                    }

                    if (!Uri.TryCreate(maybeOrigin, UriKind.Absolute, out var uri))
                    {
                        _BuildOriginFailure(
                            context,
                            maybeOrigin,
                            FluentValidatorErrorDescriber.Urls.InvalidOriginFormat()
                        );

                        return;
                    }

                    if (
                        !string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
                        && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
                    )
                    {
                        _BuildOriginFailure(
                            context,
                            maybeOrigin,
                            FluentValidatorErrorDescriber.Urls.InvalidOriginScheme(),
                            uri.Scheme
                        );

                        return;
                    }

                    // A serialized origin (RFC 6454) is scheme://host[:port] only; userinfo is never part of it.
                    if (!string.IsNullOrEmpty(uri.UserInfo))
                    {
                        _BuildOriginFailure(
                            context,
                            maybeOrigin,
                            FluentValidatorErrorDescriber.Urls.InvalidOriginFormat()
                        );

                        return;
                    }

                    if (
                        uri.AbsolutePath is not "/" and not ""
                        || !string.IsNullOrEmpty(uri.Query)
                        || !string.IsNullOrEmpty(uri.Fragment)
                    )
                    {
                        _BuildOriginFailure(
                            context,
                            maybeOrigin,
                            FluentValidatorErrorDescriber.Urls.InvalidOriginNotRootPath()
                        );

                        return;
                    }

                    if (maybeOrigin.EndsWith('/'))
                    {
                        _BuildOriginFailure(
                            context,
                            maybeOrigin,
                            FluentValidatorErrorDescriber.Urls.InvalidOriginTrailingSlash()
                        );
                    }
                }
            );
        }
    }

#nullable restore

    private static IRuleBuilderOptions<T, string?> _SchemeUrl<T>(
        IRuleBuilder<T, string?> rule,
        Func<string, bool> schemeMatches
    )
    {
        return rule.Must(maybeUrl =>
                maybeUrl is null
                || (Uri.TryCreate(maybeUrl, UriKind.Absolute, out var uri) && schemeMatches(uri.Scheme))
            )
            .WithErrorDescriptor(FluentValidatorErrorDescriber.Urls.InvalidUrl());
    }

    private static void _BuildOriginFailure<TObj>(
        ValidationContext<TObj> context,
        string origin,
        ErrorDescriptor descriptor,
        string? scheme = null
    )
    {
        var (code, description, severity) = descriptor;

        // Custom failures bypass FluentValidation's MessageFormatter, so substitute the placeholders here
        // rather than leaving literal "{PropertyValue}"/"{Scheme}" tokens in the rendered message.
        var message = description.Replace("{PropertyValue}", origin, StringComparison.Ordinal);

        if (scheme is not null)
        {
            message = message.Replace("{Scheme}", scheme, StringComparison.Ordinal);
        }

        context.AddFailure(
            new ValidationFailure(context.PropertyPath, message)
            {
                AttemptedValue = origin,
                ErrorCode = code,
                Severity = severity.ToSeverity(),
            }
        );
    }
}
