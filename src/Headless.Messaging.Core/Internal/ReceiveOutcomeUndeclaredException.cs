// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Internal;

/// <summary>
/// Thrown when a receive middleware returns without invoking the <c>next</c> delegate and without
/// declaring a <see cref="ReceiveContext.Skip(string)"/> or
/// <see cref="ReceiveContext.Reject(string, Exception?)"/> outcome — an incomplete short-circuit
/// the receive pipeline treats as a middleware fault.
/// </summary>
#pragma warning disable CA1064 // Deliberately internal: framework fault taxonomy is not public contract; callers catch the base Exception via the outcome table.
internal sealed class ReceiveOutcomeUndeclaredException(Type middlewareType)
    : Exception(
        $"Receive middleware `{middlewareType.FullName ?? middlewareType.Name}` returned without invoking next() "
            + "and without declaring a Skip or Reject outcome."
    )
{
    /// <summary>The middleware type that returned without a declared outcome or continuation.</summary>
    public Type MiddlewareType { get; } = middlewareType;
}
#pragma warning restore CA1064
