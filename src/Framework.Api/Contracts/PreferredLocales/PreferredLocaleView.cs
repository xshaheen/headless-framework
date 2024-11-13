// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Framework.Kernel.Primitives;

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Api.Contracts;

public sealed record PreferredLocaleView(string Country, string Language)
{
    [return: NotNullIfNotNull(nameof(operand))]
    public static PreferredLocaleView? FromPreferredLocale(PreferredLocale? operand) => operand;

    [return: NotNullIfNotNull(nameof(operand))]
    public static implicit operator PreferredLocaleView?(PreferredLocale? operand)
    {
        return operand is null ? null : new(operand.Country, operand.Language);
    }
}
