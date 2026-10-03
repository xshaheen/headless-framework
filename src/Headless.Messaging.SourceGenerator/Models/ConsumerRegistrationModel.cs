// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.SourceGenerators;

namespace Headless.Messaging.SourceGenerator.Models;

/// <summary>
/// One consumer class as the emitter writes it: its model plus the member names chosen for the assembly. The factory,
/// the dispatcher, and the subscription hook share one suffix, so each class's members stay distinct.
/// </summary>
internal sealed record ConsumerRegistrationModel(ConsumerModel Consumer, string MemberSuffix)
{
    /// <summary>Builds the class for one call, preferring the container's registration.</summary>
    public string FactoryName => "Create_" + MemberSuffix;

    /// <summary>Runs one delivery.</summary>
    public string DispatcherName => "Dispatch_" + MemberSuffix;

    /// <summary>Runs the class's <c>IOnSubscriptionEstablished</c> hook.</summary>
    public string SubscriptionHookName => "OnSubscriptionEstablished_" + MemberSuffix;
}
