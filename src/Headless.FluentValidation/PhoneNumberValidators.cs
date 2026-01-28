// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation.Resources;
using FluentValidation.Results;
using PhoneNumbers;
using DataAnnotationsPhoneAttribute = System.ComponentModel.DataAnnotations.PhoneAttribute;

namespace FluentValidation;

[PublicAPI]
public static class PhoneNumberValidators
{
    private static readonly DataAnnotationsPhoneAttribute _PhoneAttribute = new();

    public static IRuleBuilderOptions<T, int?> PhoneCountryCode<T>(this IRuleBuilder<T, int?> builder)
    {
        return builder.GreaterThan(0);
    }

    public static IRuleBuilderOptions<T, int> PhoneCountryCode<T>(this IRuleBuilder<T, int> builder)
    {
        return builder.GreaterThan(0);
    }

    private static void _AddInvalidCountryCodeFailure<TObj>(ValidationContext<TObj> context, string number)
    {
        var (code, description, severity) =
            FluentValidatorErrorDescriber.PhoneNumbers.InvalidNumberCountryCodeValidator();

        var failure = new ValidationFailure(context.PropertyPath, description)
        {
            AttemptedValue = number,
            ErrorCode = code,
            Severity = severity.ToSeverity(),
        };

        context.AddFailure(failure);
    }

    private static void _AddInvalidNumberFailure<TObj>(ValidationContext<TObj> context, string number)
    {
        var (code, description, severity) = FluentValidatorErrorDescriber.PhoneNumbers.InvalidNumber();

        var failure = new ValidationFailure(context.PropertyPath, description)
        {
            AttemptedValue = number,
            ErrorCode = code,
            Severity = severity.ToSeverity(),
        };

        context.AddFailure(failure);
    }

    private static void _AddNotLocalValidatorFailure<TObj>(ValidationContext<TObj> context, string number)
    {
        var (code, description, severity) = FluentValidatorErrorDescriber.PhoneNumbers.NotLocalNumberValidator();

        var failure = new ValidationFailure(context.PropertyPath, description)
        {
            AttemptedValue = number,
            ErrorCode = code,
            Severity = severity.ToSeverity(),
        };

        context.AddFailure(failure);
    }

    extension<T>(IRuleBuilder<T, string?> builder)
    {
        public IRuleBuilderOptions<T, string?> BasicPhoneNumber()
        {
            return builder
                .Must(_PhoneAttribute.IsValid)
                .WithErrorDescriptor(FluentValidatorErrorDescriber.PhoneNumbers.InvalidNumber());
        }

        public IRuleBuilderOptionsConditions<T, string?> PhoneNumber(Func<T, int> countryCodeFunc)
        {
            return builder.Custom(
                (number, context) =>
                {
                    if (string.IsNullOrWhiteSpace(number))
                    {
                        return;
                    }

                    var util = PhoneNumberUtil.GetInstance();
                    var countryCode = countryCodeFunc(context.InstanceToValidate);

                    PhoneNumber maybePhoneNumber;

                    try
                    {
                        maybePhoneNumber = util.Parse(
                            "+" + countryCode.ToString(CultureInfo.InvariantCulture) + number,
                            defaultRegion: null
                        );
                    }
                    catch (NumberParseException e)
                    {
                        switch (e.ErrorType)
                        {
                            case ErrorType.INVALID_COUNTRY_CODE:
                                _AddInvalidCountryCodeFailure(context, number);

                                return;
                            case ErrorType.NOT_A_NUMBER:
                            case ErrorType.TOO_SHORT_AFTER_IDD:
                            case ErrorType.TOO_SHORT_NSN:
                            case ErrorType.TOO_LONG:
                                _AddInvalidNumberFailure(context, number);

                                return;
                            default:
                                throw new InvalidOperationException(
                                    $"Unexpected phone number parse ErrorType `{e.ErrorType}`"
                                );
                        }
                    }

                    var validationResult = util.IsPossibleNumberWithReason(maybePhoneNumber);

                    switch (validationResult)
                    {
                        case PhoneNumberUtil.ValidationResult.IS_POSSIBLE:
                            return;
                        case PhoneNumberUtil.ValidationResult.IS_POSSIBLE_LOCAL_ONLY:
                            _AddNotLocalValidatorFailure(context, number);

                            return;
                        case PhoneNumberUtil.ValidationResult.INVALID_COUNTRY_CODE:
                            _AddInvalidCountryCodeFailure(context, number);

                            return;
                        case PhoneNumberUtil.ValidationResult.TOO_SHORT:
                        case PhoneNumberUtil.ValidationResult.INVALID_LENGTH:
                        case PhoneNumberUtil.ValidationResult.TOO_LONG:
                            _AddInvalidNumberFailure(context, number);

                            return;
                        default:
                            throw new InvalidOperationException($"Unexpected validation result `{validationResult}`");
                    }
                }
            );
        }

        public IRuleBuilderOptionsConditions<T, string?> InternationalPhoneNumber()
        {
            return builder.Custom(
                (phoneNumber, context) =>
                {
                    if (string.IsNullOrWhiteSpace(phoneNumber))
                    {
                        return;
                    }

                    var util = PhoneNumberUtil.GetInstance();

                    PhoneNumber maybePhoneNumber;

                    try
                    {
                        maybePhoneNumber = util.Parse(phoneNumber, defaultRegion: null);
                    }
                    catch (NumberParseException e)
                    {
                        switch (e.ErrorType)
                        {
                            case ErrorType.INVALID_COUNTRY_CODE:
                                _AddInvalidCountryCodeFailure(context, phoneNumber);

                                return;
                            case ErrorType.NOT_A_NUMBER:
                            case ErrorType.TOO_SHORT_AFTER_IDD:
                            case ErrorType.TOO_SHORT_NSN:
                            case ErrorType.TOO_LONG:
                                _AddInvalidNumberFailure(context, phoneNumber);

                                return;
                            default:
                                throw new InvalidOperationException(
                                    $"Unexpected phone number parse ErrorType `{e.ErrorType}`"
                                );
                        }
                    }

                    var validationResult = util.IsPossibleNumberWithReason(maybePhoneNumber);

                    switch (validationResult)
                    {
                        case PhoneNumberUtil.ValidationResult.IS_POSSIBLE:
                            return;
                        case PhoneNumberUtil.ValidationResult.IS_POSSIBLE_LOCAL_ONLY:
                            _AddNotLocalValidatorFailure(context, phoneNumber);

                            return;
                        case PhoneNumberUtil.ValidationResult.INVALID_COUNTRY_CODE:
                            _AddInvalidCountryCodeFailure(context, phoneNumber);

                            return;
                        case PhoneNumberUtil.ValidationResult.TOO_SHORT:
                        case PhoneNumberUtil.ValidationResult.INVALID_LENGTH:
                        case PhoneNumberUtil.ValidationResult.TOO_LONG:
                            _AddInvalidNumberFailure(context, phoneNumber);

                            return;
                        default:
                            throw new InvalidOperationException($"Unexpected validation result `{validationResult}`");
                    }
                }
            );
        }
    }
}
