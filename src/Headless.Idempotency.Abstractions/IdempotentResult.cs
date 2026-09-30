// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Idempotency;

/// <summary>
/// The stored result of a completed operation: opaque bytes plus the contract tag that says how to read them.
/// </summary>
/// <remarks>
/// The store never interprets the bytes. The contract tag (for example <c>"orders.receipt/v2"</c>) lets a replay
/// refuse bytes written by a caller that encoded them differently, instead of misreading them.
/// </remarks>
[PublicAPI]
public sealed class IdempotentResult
{
    private readonly byte[] _payload;

    /// <summary>Creates a stored result.</summary>
    /// <param name="payload">The result bytes; copied.</param>
    /// <param name="contract">The contract tag the bytes were written under.</param>
    /// <exception cref="ArgumentException"><paramref name="contract" /> is empty or whitespace.</exception>
    public IdempotentResult(ReadOnlySpan<byte> payload, string contract)
    {
        Argument.IsNotNullOrWhiteSpace(contract);

        _payload = payload.ToArray();
        Contract = contract;
    }

    /// <summary>Gets the result bytes.</summary>
    public ReadOnlyMemory<byte> Payload => _payload;

    /// <summary>Gets the contract tag the bytes were written under.</summary>
    public string Contract { get; }
}
