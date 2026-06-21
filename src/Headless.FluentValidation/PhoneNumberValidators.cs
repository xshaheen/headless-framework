// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation.Resources;
using FluentValidation.Results;
using Headless.Primitives;
using PhoneNumbers;
using DataAnnotationsPhoneAttribute = System.ComponentModel.DataAnnotations.PhoneAttribute;

namespace FluentValidation;

/// <summary>FluentValidation extension rules for phone number and country code properties.</summary>
[PublicAPI]
public static class PhoneNumberValidators
{
    private static readonly DataAnnotationsPhoneAttribute _PhoneAttribute = new();
    private static readonly PhoneNumberUtil _PhoneNumberUtil = PhoneNumberUtil.GetInstance();

    /// <summary>
    /// Validates that the nullable phone country code, when present, is greater than zero.
    /// </summary>
    /// <returns>The rule builder options for chaining.</returns>
    public static IRuleBuilderOptions<T, int?> PhoneCountryCode<T>(this IRuleBuilder<T, int?> builder)
    {
        return builder
            .GreaterThan(0)
            .WithErrorDescriptor(FluentValidatorErrorDescriber.PhoneNumbers.InvalidNumberCountryCodeValidator());
    }

    /// <summary>Validates that the phone country code is greater than zero.</summary>
    /// <returns>The rule builder options for chaining.</returns>
    public static IRuleBuilderOptions<T, int> PhoneCountryCode<T>(this IRuleBuilder<T, int> builder)
    {
        return builder
            .GreaterThan(0)
            .WithErrorDescriptor(FluentValidatorErrorDescriber.PhoneNumbers.InvalidNumberCountryCodeValidator());
    }

    private static void _AddPhoneFailure<TObj>(
        ValidationContext<TObj> context,
        string number,
        ErrorDescriptor descriptor
    )
    {
        var (code, description, severity) = descriptor;

        // Custom failures bypass FluentValidation's MessageFormatter, so substitute the placeholder here
        // rather than leaving a literal "{PropertyValue}" token in the rendered message.
        var message = description.Replace("{PropertyValue}", number, StringComparison.Ordinal);

        context.AddFailure(
            new ValidationFailure(context.PropertyPath, message)
            {
                AttemptedValue = number,
                ErrorCode = code,
                Severity = severity.ToSeverity(),
            }
        );
    }

    private static void _ValidateParsedNumber<TObj>(
        ValidationContext<TObj> context,
        string rawInput,
        string attemptedValue
    )
    {
        PhoneNumbers.PhoneNumber maybePhoneNumber;

        try
        {
            maybePhoneNumber = _PhoneNumberUtil.Parse(rawInput, defaultRegion: null);
        }
        catch (NumberParseException e)
        {
            // INVALID_COUNTRY_CODE has its own code; every other parse error (incl. any future/unknown
            // ErrorType) degrades to "invalid number" rather than throwing and turning input into a 500.
            var descriptor =
                e.ErrorType == ErrorType.INVALID_COUNTRY_CODE
                    ? FluentValidatorErrorDescriber.PhoneNumbers.InvalidNumberCountryCodeValidator()
                    : FluentValidatorErrorDescriber.PhoneNumbers.InvalidNumber();

            _AddPhoneFailure(context, attemptedValue, descriptor);

            return;
        }

        switch (_PhoneNumberUtil.IsPossibleNumberWithReason(maybePhoneNumber))
        {
            case PhoneNumberUtil.ValidationResult.IS_POSSIBLE:
                return;
            case PhoneNumberUtil.ValidationResult.IS_POSSIBLE_LOCAL_ONLY:
                _AddPhoneFailure(
                    context,
                    attemptedValue,
                    FluentValidatorErrorDescriber.PhoneNumbers.NotLocalNumberValidator()
                );

                return;
            case PhoneNumberUtil.ValidationResult.INVALID_COUNTRY_CODE:
                _AddPhoneFailure(
                    context,
                    attemptedValue,
                    FluentValidatorErrorDescriber.PhoneNumbers.InvalidNumberCountryCodeValidator()
                );

                return;
            case PhoneNumberUtil.ValidationResult.TOO_SHORT:
            case PhoneNumberUtil.ValidationResult.INVALID_LENGTH:
            case PhoneNumberUtil.ValidationResult.TOO_LONG:
            default:
                // Known short/long/length failures and any future/unknown result degrade to "invalid number".
                _AddPhoneFailure(context, attemptedValue, FluentValidatorErrorDescriber.PhoneNumbers.InvalidNumber());

                return;
        }
    }

#nullable disable // keep the builder nullability-agnostic: binds to nullable and non-nullable properties, preserving the caller's nullability
    extension<T>(IRuleBuilder<T, string> builder)
    {
        /// <summary>
        /// Validates the phone number using the .NET <c>PhoneAttribute</c> heuristic (basic format
        /// check only; does not verify the number against a country code).
        /// Passes <see langword="null"/> and whitespace-only values through without failure.
        /// </summary>
        /// <returns>The rule builder options for chaining.</returns>
        public IRuleBuilderOptions<T, string> BasicPhoneNumber()
        {
            return builder
                .Must(_PhoneAttribute.IsValid)
                .WithErrorDescriptor(FluentValidatorErrorDescriber.PhoneNumbers.InvalidNumber());
        }

        /// <summary>
        /// Validates a local phone number combined with a country code resolved at validation time
        /// via <paramref name="countryCodeFunc"/>. The number is prepended with <c>+{countryCode}</c>
        /// and parsed by libphonenumber. Passes <see langword="null"/> and whitespace-only values through without failure.
        /// </summary>
        /// <param name="countryCodeFunc">A function that extracts the numeric country code (e.g. 20 for Egypt) from the root object.</param>
        /// <returns>The rule builder options for chaining.</returns>
        public IRuleBuilderOptionsConditions<T, string> PhoneNumber(Func<T, int> countryCodeFunc)
        {
            return builder.Custom(
                (number, context) =>
                {
                    if (string.IsNullOrWhiteSpace(number))
                    {
                        return;
                    }

                    var countryCode = countryCodeFunc(context.InstanceToValidate);

                    _ValidateParsedNumber(
                        context,
                        "+" + countryCode.ToString(CultureInfo.InvariantCulture) + number,
                        number
                    );
                }
            );
        }

        /// <summary>
        /// Validates an E.164-style international phone number (must include country code prefix,
        /// e.g. <c>+201234567890</c>). Parsed by libphonenumber.
        /// Passes <see langword="null"/> and whitespace-only values through without failure.
        /// </summary>
        /// <returns>The rule builder options for chaining.</returns>
        public IRuleBuilderOptionsConditions<T, string> InternationalPhoneNumber()
        {
            return builder.Custom(
                (phoneNumber, context) =>
                {
                    if (string.IsNullOrWhiteSpace(phoneNumber))
                    {
                        return;
                    }

                    _ValidateParsedNumber(context, phoneNumber, phoneNumber);
                }
            );
        }
    }
#nullable restore
}
