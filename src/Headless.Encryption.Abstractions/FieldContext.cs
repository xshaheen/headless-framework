// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Encryption;

/// <summary>
/// What a field value is bound to: its purpose (the column), its scope (the tenant whose key encrypts it), and an
/// optional binding (usually the row's id). All three are authenticated with the ciphertext, so a value copied to
/// another column, another tenant, or, when a binding is set, another row fails to decrypt.
/// </summary>
/// <remarks>
/// Decrypt with the same context the value was encrypted with. The context is not stored in the frame: the caller
/// states it again, which is what lets the frame refuse to move.
/// </remarks>
/// <param name="purpose">
/// A stable name of what the value is, such as <c>Beneficiary.NationalId</c>. Renaming it makes every value encrypted
/// under the old name unreadable.
/// </param>
/// <exception cref="ArgumentException"><paramref name="purpose" /> is <see langword="null" /> or white space.</exception>
[PublicAPI]
public sealed class FieldContext(string purpose)
{
    /// <summary>The scope of host-level data, which belongs to no tenant: the empty string.</summary>
    public const string HostScope = "";

    /// <summary>Gets the stable name of what the value is. It selects the subkey that encrypts it.</summary>
    public string Purpose { get; } = Argument.IsNotNullOrWhiteSpace(purpose);

    /// <summary>
    /// Gets the scope whose data key encrypts the value: <see langword="null" /> (the default) for the current tenant,
    /// <see cref="HostScope" /> for host-level data, or an explicit tenant id or per-subject scope of at most
    /// <see cref="DataKeyFieldLimits.ScopeMaxLength" /> characters.
    /// </summary>
    /// <remarks>
    /// A <see langword="null" /> scope with no current tenant fails rather than falling back to the host scope: a value
    /// written under the host key would survive the tenant's crypto-shredding.
    /// </remarks>
    public string? Scope { get; init; }

    /// <summary>
    /// Gets bytes the value is additionally bound to, usually the row's primary key, so two rows of one tenant and
    /// column cannot swap values. Empty (the default) binds nothing. The same bytes are needed to decrypt.
    /// </summary>
    public ReadOnlyMemory<byte> Binding { get; init; }

    /// <summary>Creates a context for host-level data, which belongs to no tenant.</summary>
    /// <param name="purpose">A stable name of what the value is.</param>
    /// <returns>A context whose <see cref="Scope" /> is <see cref="HostScope" />.</returns>
    /// <exception cref="ArgumentException"><paramref name="purpose" /> is <see langword="null" /> or white space.</exception>
    public static FieldContext Host(string purpose) => new(purpose) { Scope = HostScope };
}
