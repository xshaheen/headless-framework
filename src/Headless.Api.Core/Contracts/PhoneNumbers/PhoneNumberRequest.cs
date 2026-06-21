// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Primitives;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Api.Contracts;

/// <summary>
/// API request contract for a phone number composed of an ITU country dialling code and a local
/// subscriber number. Validate with <see cref="FluentValidatorPhoneNumberExtensions.PhoneNumber{T}"/>
/// to enforce format rules before mapping to the domain <see cref="PhoneNumber"/> primitive.
/// </summary>
/// <param name="Code">The ITU-T country calling code (e.g., <c>1</c> for US, <c>44</c> for UK).</param>
/// <param name="Number">The local subscriber number, excluding the country code or leading zeros.</param>
public sealed record PhoneNumberRequest(int Code, string Number)
{
    /// <summary>
    /// Returns the phone number in E.164-like format (e.g., <c>+1555123456</c>).
    /// </summary>
    public override string ToString() => $"+{Code.ToString(CultureInfo.InvariantCulture)}{Number}";

    /// <summary>Maps this request to the domain <see cref="PhoneNumber"/> primitive.</summary>
    public PhoneNumber ToPhoneNumber() => this;

    /// <summary>
    /// Implicitly converts to the domain <see cref="PhoneNumber"/> primitive.
    /// Returns <see langword="null"/> when <paramref name="operand"/> is <see langword="null"/>.
    /// </summary>
    [return: NotNullIfNotNull(nameof(operand))]
    public static implicit operator PhoneNumber?(PhoneNumberRequest? operand) =>
        operand is null ? null : new(operand.Code, operand.Number);
}

internal sealed class PhoneNumberRequestValidator : AbstractValidator<PhoneNumberRequest>
{
    public PhoneNumberRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().PhoneCountryCode();
        RuleFor(x => x.Number).NotEmpty().PhoneNumber(x => x.Code);
    }
}

[PublicAPI]
public static class FluentValidatorPhoneNumberExtensions
{
    /// <summary>
    /// Applies <c>PhoneNumberRequestValidator</c> to validate the country code and subscriber
    /// number formats. The validator is skipped when the <paramref name="builder"/> value is
    /// <see langword="null"/> (the property is treated as not provided).
    /// </summary>
    /// <returns>The rule builder so that additional calls can be chained.</returns>
    public static IRuleBuilderOptions<T, PhoneNumberRequest?> PhoneNumber<T>(
        this IRuleBuilder<T, PhoneNumberRequest?> builder
    )
    {
        return builder.SetValidator(new PhoneNumberRequestValidator()!).When(x => x is not null);
    }
}
