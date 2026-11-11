// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Dashboard;

/// <summary>The answer of one forwarded call.</summary>
/// <param name="State">What the call found the peer in.</param>
/// <param name="Status">The peer's status code, or <c>0</c> when no answer came.</param>
/// <param name="Body">The peer's body, empty when no answer came.</param>
internal sealed record ForwardResponse(PeerState State, int Status, byte[] Body)
{
    public bool Answered => State is PeerState.Answering;

    public bool IsSuccess => Answered && Status is >= 200 and < 300;

    /// <summary>Gets a value indicating whether the last attempt got no answer because it ran out of time.</summary>
    public bool TimedOut { get; init; }

    public static ForwardResponse Failed(PeerState state) => new(state, Status: 0, []);
}
