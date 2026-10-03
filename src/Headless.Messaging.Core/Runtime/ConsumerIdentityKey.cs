// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Messaging.Messages;

namespace Headless.Messaging.Runtime;

internal readonly record struct ConsumerIdentityKey(string ConsumerIdentity, MessageLane Lane);
