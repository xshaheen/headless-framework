// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Fencing;

/// <summary>
/// How far an attempt got: opaque bytes plus the contract tag that says how to read them, recorded with a renewal so
/// an attempt that takes the lease over can resume instead of starting again.
/// </summary>
/// <remarks>
/// The store never interprets the bytes. The contract tag (for example <c>"exports.cursor/v2"</c>) lets the resuming
/// attempt refuse bytes an executor encoded differently, instead of misreading them. A renewal checks the payload and
/// contract against the limits in <c>FencingFieldLimits</c> before it writes anything.
/// </remarks>
[PublicAPI]
public sealed class LeaseProgress
{
    private readonly byte[] _payload;

    /// <summary>Creates a progress record.</summary>
    /// <param name="payload">The progress bytes; copied.</param>
    /// <param name="contract">The contract tag the bytes were written under.</param>
    /// <exception cref="ArgumentNullException"><paramref name="contract" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException"><paramref name="contract" /> is empty or whitespace.</exception>
    public LeaseProgress(ReadOnlySpan<byte> payload, string contract)
    {
        Argument.IsNotNullOrWhiteSpace(contract);

        _payload = payload.ToArray();
        Contract = contract;
    }

    /// <summary>Gets the progress bytes.</summary>
    public ReadOnlyMemory<byte> Payload => _payload;

    /// <summary>Gets the contract tag the bytes were written under.</summary>
    public string Contract { get; }
}
