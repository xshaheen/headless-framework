// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Internal;

internal readonly record struct MiddlewareDispatchKey(Type MiddlewareType, Type MessageType);
