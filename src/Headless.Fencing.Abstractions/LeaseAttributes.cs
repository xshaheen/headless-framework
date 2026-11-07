// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Fencing;

/// <summary>
/// What a grant fixes for its generation: opaque bytes plus the contract tag that says how to read them, such as the
/// scope the attempt is allowed to act on or metadata about its owner.
/// </summary>
/// <remarks>
/// The store never interprets the bytes. It writes them with every grant that acquires the lease and returns them
/// with <c>GetCurrentAsync</c>, so one read of the lease row answers both whether the attempt is live and what it is
/// scoped to. Settlement, release, abandonment, and revocation keep them: they still describe the generation on the
/// row. A grant checks the payload and contract against the limits in <c>FencingFieldLimits</c> before it writes
/// anything.
/// </remarks>
[PublicAPI]
public sealed class LeaseAttributes
{
    private readonly byte[] _payload;

    /// <summary>Creates an attributes record.</summary>
    /// <param name="payload">The attribute bytes; copied.</param>
    /// <param name="contract">The contract tag the bytes were written under.</param>
    /// <exception cref="ArgumentNullException"><paramref name="contract" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException"><paramref name="contract" /> is empty or whitespace.</exception>
    public LeaseAttributes(ReadOnlySpan<byte> payload, string contract)
    {
        Argument.IsNotNullOrWhiteSpace(contract);

        _payload = payload.ToArray();
        Contract = contract;
    }

    /// <summary>Gets the attribute bytes.</summary>
    public ReadOnlyMemory<byte> Payload => _payload;

    /// <summary>Gets the contract tag the bytes were written under.</summary>
    public string Contract { get; }
}
