// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Headless.Primitives;

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Headless.Api.Contracts;

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
