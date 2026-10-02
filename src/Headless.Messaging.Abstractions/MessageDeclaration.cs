// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// One message declaration recorded in a service collection. Messaging folds every declaration, in registration order,
/// when the host's consumer registry freezes, so a declaration counts wherever and whenever it was made.
/// </summary>
/// <param name="MessageType">The declared message type.</param>
internal abstract record MessageDeclaration(Type MessageType);
