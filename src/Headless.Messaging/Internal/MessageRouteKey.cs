// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Internal;

/// <summary>
/// Lane-qualified identity for behavior that routes a declared contract by its logical message name.
/// </summary>
internal readonly record struct MessageRouteKey(Type ContractType, string MessageName, MessageLane Lane);
