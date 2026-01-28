// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Headless.Primitives;

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Headless.Api.Contracts;

public sealed record PreferredLocaleRequest(string Country, string Language)
{
    public override string ToString() => $"{Language}-{Country}";

    public PreferredLocale ToPreferredLocale() => this;

    [return: NotNullIfNotNull(nameof(operand))]
    public static implicit operator PreferredLocale?(PreferredLocaleRequest? operand)
    {
        return operand is null ? null : new(operand.Country, operand.Language);
    }
}
