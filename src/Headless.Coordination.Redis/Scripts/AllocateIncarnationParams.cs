// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using Headless.Redis;
using StackExchange.Redis;

namespace Headless.Coordination.Redis.Scripts;

#pragma warning disable IDE1006 // camelCase mirrors the Lua @param token names
/// <summary>Parameters for <see cref="RedisMembershipAllocateIncarnationScriptDefinition"/>.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct AllocateIncarnationParams(RedisKey genKey, RedisKey knownKey, string generationField);
