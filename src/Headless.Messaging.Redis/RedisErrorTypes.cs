// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Redis;

internal enum RedisErrorTypes : byte
{
    Unknown = 0,
    GroupAlreadyExists = 1,
    NoGroupInfoExists = 2,
}
