// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Diagnostics.CodeAnalysis;
using FluentValidation;
using Framework.FluentValidation;
using Framework.Kernel.Primitives;

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Api.Contracts;

public sealed record PhoneNumberRequest(int Code, string Number)
{
    public override string ToString() => $"+{Code.ToString(CultureInfo.InvariantCulture)}{Number}";

    public PhoneNumber ToPhoneNumber() => this;

    [return: NotNullIfNotNull(nameof(operand))]
    public static implicit operator PhoneNumber?(PhoneNumberRequest? operand) =>
        operand is null ? null : new(operand.Code, operand.Number);
}

public sealed class PhoneNumberRequestValidator : AbstractValidator<PhoneNumberRequest>
{
    public PhoneNumberRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().PhoneCountryCode();
        RuleFor(x => x.Number).NotEmpty().PhoneNumber(x => x.Code);
    }
}

public static class FluentValidatorPhoneNumberExtensions
{
    public static IRuleBuilderOptions<T, PhoneNumberRequest?> PhoneNumber<T>(
        this IRuleBuilder<T, PhoneNumberRequest?> builder
    )
    {
        return builder.SetValidator(new PhoneNumberRequestValidator()!).When(x => x is not null);
    }
}
