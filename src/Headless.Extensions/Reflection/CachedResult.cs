// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Reflection;

/// <summary>
/// Wraps a nullable reference value so it can be stored in collections that reject
/// null entries (notably <see cref="System.Runtime.CompilerServices.ConditionalWeakTable{TKey, TValue}"/>).
/// Use for reflection caches whose computed result can legitimately be <c>null</c>
/// (for example, a <see cref="System.Reflection.MethodInfo"/> lookup that may not find a match).
/// </summary>
[PublicAPI]
public sealed class CachedResult<T>(T? value)
    where T : class
{
    public T? Value { get; } = value;
}
