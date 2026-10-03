// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Configuration;

namespace Headless.Messaging.Registration;

/// <summary>One <c>WithMessageNameMapping&lt;T&gt;(name)</c> call: a raw name for the type on both lanes.</summary>
/// <param name="MessageType">The message type.</param>
/// <param name="Name">The raw logical message name.</param>
internal sealed record MessageNameMappingDeclaration(Type MessageType, string Name) : MessageDeclaration(MessageType);
