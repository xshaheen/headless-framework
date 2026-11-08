// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Compliance.Classification;

namespace Headless.Logging;

/// <summary>
/// The Headless data classifications: what a value is, so the host can decide once how each kind is redacted in every
/// log and telemetry channel.
/// </summary>
/// <remarks>
/// Mark members with <see cref="SecretDataAttribute"/>, <see cref="PrivateDataAttribute"/>, or
/// <see cref="IdentifierDataAttribute"/>, and register the redactors with <c>AddHeadlessRedaction</c>. The
/// classifications are ordinary <see cref="DataClassification"/> values, so an app can map them to its own redactors
/// with Microsoft's <c>AddRedaction</c> instead.
/// </remarks>
[PublicAPI]
public static class HeadlessDataClassifications
{
    /// <summary>The taxonomy name every Headless classification carries.</summary>
    public const string TaxonomyName = "Headless";

    /// <summary>
    /// A credential or key whose value must never appear: a password, an API key, a bearer or refresh token, a private
    /// key. Always erased.
    /// </summary>
    public static DataClassification Secret { get; } = new(TaxonomyName, nameof(Secret));

    /// <summary>
    /// Personal data about a person: a name, an email address, a phone number, a national id, a postal address.
    /// Pseudonymized when the host supplies an HMAC key, so operators can still tell that two events concern the same
    /// person; erased otherwise.
    /// </summary>
    public static DataClassification Private { get; } = new(TaxonomyName, nameof(Private));

    /// <summary>
    /// A stable identifier operators correlate on but that should not appear raw: a user or account id that links to a
    /// person, a device token, an external reference. Pseudonymized when the host supplies an HMAC key; erased
    /// otherwise.
    /// </summary>
    public static DataClassification Identifier { get; } = new(TaxonomyName, nameof(Identifier));
}
