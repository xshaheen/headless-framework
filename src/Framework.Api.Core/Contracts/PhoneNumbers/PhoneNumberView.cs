// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Diagnostics.CodeAnalysis;
using Framework.Kernel.Primitives;

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Api.Core.Contracts;

public sealed record PhoneNumberView(int Code, string Number)
{
    [return: NotNullIfNotNull(nameof(operand))]
    public static PhoneNumberView? FromPhoneNumber(PhoneNumber? operand) => operand;

    [return: NotNullIfNotNull(nameof(operand))]
    public static implicit operator PhoneNumberView?(PhoneNumber? operand)
    {
        return operand is null ? null : new(operand.CountryCode, operand.Number);
    }
}
