// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Messaging;

/// <summary>
/// Identifies one running messaging host. Every-instance Bus subscriptions derive their per-process broker name from it.
/// </summary>
/// <remarks>
/// <c>AddHeadlessMessaging</c> registers one singleton with a new random value, so every start of a process gets a new
/// id. The id is deliberately not the host name: a host name repeats across rollouts, and a restarted process must not
/// reclaim the subscription its predecessor left for the broker to remove.
/// </remarks>
/// <param name="value">The instance id.</param>
/// <exception cref="ArgumentException"><paramref name="value"/> is <see cref="Guid.Empty"/>.</exception>
[PublicAPI]
public sealed class MessagingInstanceId(Guid value)
{
    /// <summary>Initializes a new instance with a new random value.</summary>
    public MessagingInstanceId()
        : this(Guid.NewGuid()) { }

    /// <summary>The instance id.</summary>
    public Guid Value { get; } = Argument.IsNotEmpty(value);

    /// <inheritdoc />
    public override string ToString() => Value.ToString("N");
}
