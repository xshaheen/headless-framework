// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Encryption;

/// <summary>What a blind index hashes for: the purpose, the scope whose lookup key hashes it, and the hash length.</summary>
/// <param name="purpose">
/// The column the hash indexes, such as <c>Beneficiary.NationalId</c>. Keep it stable: it selects the hash key, so
/// renaming it makes every stored hash unmatchable.
/// </param>
/// <remarks>
/// Use one context per column, kept in one place: a column whose writes and queries use different scopes or lengths
/// matches nothing.
/// </remarks>
[PublicAPI]
public sealed class BlindIndexContext(string purpose)
{
    /// <summary>The smallest hash length, in bytes.</summary>
    public const int MinLength = 8;

    /// <summary>The full HMAC-SHA256 length, in bytes, and the default.</summary>
    public const int MaxLength = 32;

    /// <summary>Gets the column the hash indexes.</summary>
    public string Purpose { get; } = Argument.IsNotNullOrWhiteSpace(purpose);

    /// <summary>
    /// Gets the scope whose lookup key hashes the value: a tenant id, <see cref="FieldContext.HostScope" /> for the
    /// shared host key, or <see langword="null" /> for the current tenant. A <see langword="null" /> scope with no
    /// current tenant throws; it never falls back to the host key.
    /// </summary>
    public string? Scope { get; init; }

    /// <summary>
    /// Gets the hash length in bytes, from <see cref="MinLength" /> to <see cref="MaxLength" /> (the default). A shorter
    /// hash leaks less about how often a value repeats and matches more unrelated rows; check each match by decrypting.
    /// </summary>
    public int Length
    {
        get;
        init => field = Argument.IsInclusiveBetween(value, MinLength, MaxLength);
    } = MaxLength;

    /// <summary>
    /// Creates a context hashed by the host's lookup key, so the same value hashes the same in every tenant: for
    /// deliberate cross-tenant matching, such as finding one person registered by two tenants.
    /// </summary>
    /// <param name="purpose">The column the hash indexes.</param>
    /// <returns>The context.</returns>
    public static BlindIndexContext Host(string purpose) => new(purpose) { Scope = FieldContext.HostScope };
}
