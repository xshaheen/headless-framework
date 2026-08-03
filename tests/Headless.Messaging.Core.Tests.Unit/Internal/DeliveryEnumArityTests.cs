// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Testing.Tests;

namespace Tests.Internal;

/// <summary>
/// <see cref="DeliveryDecisionResolver"/> validates its inputs against inline member lists instead of
/// <c>Enum.IsDefined</c>, because it runs on every publish. That trade buys speed at the price of a silent
/// coupling: a newly added member is accepted by the rest of the pipeline but throws here. These tests fail
/// as soon as one of the three enums grows, and name the guard that has to grow with it.
/// </summary>
public sealed class DeliveryEnumArityTests : TestBase
{
    [Fact]
    public void should_pin_message_lane_members_to_the_resolver_guard()
    {
        Enum.GetValues<MessageLane>()
            .Should()
            .Equal(
                [MessageLane.Bus, MessageLane.Queue],
                "DeliveryDecisionResolver.Resolve rejects anything outside `lane is (Bus or Queue)` — widen that guard before this list"
            );
    }

    [Fact]
    public void should_pin_delivery_mode_members_to_the_resolver_guard()
    {
        Enum.GetValues<DeliveryMode>()
            .Should()
            .Equal(
                [DeliveryMode.Auto, DeliveryMode.Durable, DeliveryMode.TransportDirect],
                "DeliveryDecisionResolver.Resolve rejects anything outside `requestedMode is (Auto or Durable or TransportDirect)`, and its resolvedMode/path switches would hit UnreachableException for a new member"
            );
    }

    [Fact]
    public void should_pin_delivery_coordination_status_members_to_the_resolver_guard()
    {
        Enum.GetValues<DeliveryCoordinationStatus>()
            .Should()
            .Equal(
                [
                    DeliveryCoordinationStatus.None,
                    DeliveryCoordinationStatus.Compatible,
                    DeliveryCoordinationStatus.Incompatible,
                ],
                "DeliveryDecisionResolver.Resolve rejects anything outside `coordination.Status is (None or Compatible or Incompatible)` — widen that guard before this list"
            );
    }
}
