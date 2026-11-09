// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;

namespace Headless.Fencing;

/// <summary>
/// What one <see cref="RowLeaseRules" /> decision did: its status, the lease to store afterwards, and whether there is
/// anything to store. It carries no token, so a store builds its own from <see cref="RowLeaseSnapshot.Generation" />.
/// </summary>
/// <typeparam name="TStatus">The result enum of the verb that decided.</typeparam>
/// <param name="Status">What the verb did.</param>
/// <param name="Next">The lease after the decision; equal to the input when <paramref name="Wrote" /> is false.</param>
/// <param name="Wrote">Whether the decision changed the lease, so the store must persist <paramref name="Next" />.</param>
[PublicAPI]
[StructLayout(LayoutKind.Auto)]
public readonly record struct RowLeaseTransition<TStatus>(TStatus Status, RowLeaseSnapshot Next, bool Wrote)
    where TStatus : struct, Enum;
