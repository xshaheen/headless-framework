// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Idempotency;

/// <summary>
/// The last step an admitted attempt recorded as done, with the state it needs to resume from there: a short name,
/// opaque bytes, and the contract tag that says how to read them.
/// </summary>
/// <remarks>
/// An attempt records one with <c>SetRecoveryPointAsync</c> after each step whose effects must not repeat. The next
/// admission of the key reads it from <see cref="IdempotentAdmission.RecoveryPoint" /> and resumes after that step
/// instead of running it again. The store never interprets the name or the bytes.
/// </remarks>
[PublicAPI]
public sealed class IdempotentRecoveryPoint
{
    private readonly byte[] _state;

    /// <summary>Creates a recovery point.</summary>
    /// <param name="name">The step's name, for example <c>"payment-captured"</c>.</param>
    /// <param name="state">The state the step left behind; copied.</param>
    /// <param name="contract">The contract tag the state was written under.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="name" /> or <paramref name="contract" /> is empty or whitespace.
    /// </exception>
    public IdempotentRecoveryPoint(string name, ReadOnlySpan<byte> state, string contract)
    {
        Argument.IsNotNullOrWhiteSpace(name);
        Argument.IsNotNullOrWhiteSpace(contract);

        Name = name;
        _state = state.ToArray();
        Contract = contract;
    }

    /// <summary>Gets the step's name.</summary>
    public string Name { get; }

    /// <summary>Gets the state the step left behind.</summary>
    public ReadOnlyMemory<byte> State => _state;

    /// <summary>Gets the contract tag the state was written under.</summary>
    public string Contract { get; }
}
