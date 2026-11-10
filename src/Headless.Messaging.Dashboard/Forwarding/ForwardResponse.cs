// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Dashboard;

/// <summary>The answer of one forwarded call.</summary>
/// <param name="State">What the call found the peer in.</param>
/// <param name="Status">The peer's status code, or <c>0</c> when no answer came.</param>
/// <param name="Body">The peer's body, empty when no answer came.</param>
/// <param name="ContentType">The peer's content type, when it sent one.</param>
internal sealed record ForwardResponse(PeerState State, int Status, byte[] Body, string? ContentType)
{
    public bool Answered => State is PeerState.Answering;

    public bool IsSuccess => Answered && Status is >= 200 and < 300;

    public static ForwardResponse Failed(PeerState state) => new(state, Status: 0, [], ContentType: null);
}
