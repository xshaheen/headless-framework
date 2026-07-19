// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Constants;
using Headless.FluentValidation;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace FluentValidation;

/// <summary>FluentValidation extension rules for common string format constraints.</summary>
[PublicAPI]
public static class StringsFluentValidationExtensions
{
#nullable disable // keep the builder nullability-agnostic: binds to nullable and non-nullable properties, preserving the caller's nullability
    /// <summary>
    /// Validates that the string consists entirely of digit characters (no sign, no decimal point).
    /// Passes <see langword="null"/> values through without failure.
    /// </summary>
    /// <returns>The rule builder options for chaining.</returns>
    public static IRuleBuilderOptions<T, string> OnlyIntegers<T>(this IRuleBuilder<T, string> builder)
#nullable restore
    {
        return builder
            .Matches(RegexPatterns.IntegerNumber)
            .When(x => x is not null)
            .WithErrorDescriptor(FluentValidatorErrorDescriber.Strings.OnlyNumberValidator());
    }

#nullable disable // keep the builder nullability-agnostic: binds to nullable and non-nullable properties, preserving the caller's nullability
    extension<T>(IRuleBuilder<T, string> builder)
    {
        /// <summary>
        /// Validates that the string represents a decimal number (optional leading minus sign and an
        /// optional fractional part separated by <c>.</c> or <c>,</c>).
        /// Passes <see langword="null"/> values through without failure.
        /// </summary>
        /// <returns>The rule builder options for chaining.</returns>
        public IRuleBuilderOptions<T, string> OnlyDecimals()
        {
            return builder
                .Matches(RegexPatterns.DecimalNumber)
                .When(x => x is not null)
                .WithErrorDescriptor(FluentValidatorErrorDescriber.Strings.OnlyDecimalsValidator());
        }

        /// <summary>
        /// Validates that the string is a URL slug: one or more lowercase alphanumeric segments
        /// separated by single hyphens (for example <c>hello-world</c>).
        /// Passes <see langword="null"/> values through without failure.
        /// </summary>
        /// <returns>The rule builder options for chaining.</returns>
        public IRuleBuilderOptions<T, string> Slug()
        {
            return builder
                .Matches(RegexPatterns.Slug)
                .When(x => x is not null)
                .WithErrorDescriptor(FluentValidatorErrorDescriber.Strings.InvalidSlug());
        }

        /// <summary>
        /// Validates that the string is a username: 3–30 characters using letters, digits, <c>-</c>,
        /// <c>_</c>, and <c>.</c>, where a special character may not appear consecutively or as the
        /// first or last character. Passes <see langword="null"/> values through without failure.
        /// </summary>
        /// <returns>The rule builder options for chaining.</returns>
        public IRuleBuilderOptions<T, string> Username()
        {
            return builder
                .Matches(RegexPatterns.Username)
                .When(x => x is not null)
                .WithErrorDescriptor(FluentValidatorErrorDescriber.Strings.InvalidUsername());
        }

        /// <summary>
        /// Validates that the string is a postal/ZIP code: 2–12 alphanumeric characters with optional
        /// inner hyphens or spaces. Passes <see langword="null"/> values through without failure.
        /// </summary>
        /// <returns>The rule builder options for chaining.</returns>
        public IRuleBuilderOptions<T, string> ZipCode()
        {
            return builder
                .Matches(RegexPatterns.ZipCode)
                .When(x => x is not null)
                .WithErrorDescriptor(FluentValidatorErrorDescriber.Strings.InvalidZipCode());
        }

        /// <summary>
        /// Validates that the string is a hex color: an optional leading <c>#</c> followed by exactly
        /// 3 or 6 hexadecimal digits (case-insensitive, so both <c>#1a2b3c</c> and <c>#1A2B3C</c> pass).
        /// Passes <see langword="null"/> values through without failure.
        /// </summary>
        /// <returns>The rule builder options for chaining.</returns>
        public IRuleBuilderOptions<T, string> HexColor()
        {
            return builder
                .Must(static value =>
                {
                    if (value is null)
                    {
                        return true;
                    }

                    var span = value.AsSpan();

                    if (!span.IsEmpty && span[0] == '#')
                    {
                        span = span[1..];
                    }

                    if (span.Length is not (3 or 6))
                    {
                        return false;
                    }

                    foreach (var c in span)
                    {
                        if (!char.IsAsciiHexDigit(c))
                        {
                            return false;
                        }
                    }

                    return true;
                })
                .WithErrorDescriptor(FluentValidatorErrorDescriber.Strings.InvalidHexColor());
        }

        /// <summary>
        /// Validates that the string is a valid Base64-encoded value (standard alphabet, surrounding
        /// whitespace tolerated). Passes <see langword="null"/> values through without failure; an empty
        /// string is considered valid Base64 — combine with <c>.NotEmpty()</c> if a value is required.
        /// </summary>
        /// <returns>The rule builder options for chaining.</returns>
        public IRuleBuilderOptions<T, string> Base64()
        {
            return builder
                .Must(static value => value is null || System.Buffers.Text.Base64.IsValid(value.AsSpan()))
                .WithErrorDescriptor(FluentValidatorErrorDescriber.Strings.InvalidBase64());
        }

        /// <summary>
        /// Validates that the string has no leading or trailing whitespace (the value equals its
        /// trimmed form). Passes <see langword="null"/> values through without failure.
        /// </summary>
        /// <returns>The rule builder options for chaining.</returns>
        public IRuleBuilderOptions<T, string> Trimmed()
        {
            return builder
                .Must(static value => value is null || value.AsSpan().Trim().Length == value.Length)
                .WithErrorDescriptor(FluentValidatorErrorDescriber.Strings.NotTrimmed());
        }

        /// <summary>
        /// Validates that the string is a culture/locale name accepted by
        /// <see cref="CultureInfo.GetCultureInfo(string)"/> (for example <c>en</c> or <c>en-US</c>).
        /// On ICU-backed runtimes any well-formed BCP-47 tag is accepted, not only installed cultures.
        /// Passes <see langword="null"/> values through without failure.
        /// </summary>
        /// <returns>The rule builder options for chaining.</returns>
        public IRuleBuilderOptions<T, string> Culture()
        {
            return builder
                .Must(static value =>
                {
                    if (value is null)
                    {
                        return true;
                    }

                    try
                    {
                        _ = CultureInfo.GetCultureInfo(value);

                        return true;
                    }
                    catch (CultureNotFoundException)
                    {
                        return false;
                    }
                })
                .WithErrorDescriptor(FluentValidatorErrorDescriber.Strings.InvalidCulture());
        }
    }
}
