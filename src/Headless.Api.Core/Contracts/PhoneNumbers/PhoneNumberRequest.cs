// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Api.Contracts;

/// <summary>
/// API request contract for a phone number composed of an ITU country dialling code and a local
/// subscriber number. Install <c>Headless.Api.FluentValidation</c> and use its <c>PhoneNumber</c>
/// validator extension to enforce format rules before mapping to the domain <see cref="PhoneNumber"/>
/// primitive.
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
