// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Headless.Api;

/// <summary>
/// Reads named properties from a <see cref="System.Diagnostics.DiagnosticListener"/> event payload. Producers such as
/// the middleware analysis pipeline write anonymous objects, so the only contract is the property name.
/// </summary>
internal static class DiagnosticPayload
{
    // Payload types are a small closed set of producer types, so the cache stays bounded and spares a reflection
    // lookup on every middleware hop of every request.
    private static readonly ConcurrentDictionary<(Type Type, string Name), PropertyInfo?> _Properties = new();

    public static bool TryGet<T>(object payload, string name, [MaybeNullWhen(false)] out T value)
    {
        var property = _Properties.GetOrAdd(
            (payload.GetType(), name),
            static key => key.Type.GetProperty(key.Name, BindingFlags.Instance | BindingFlags.Public)
        );

        if (property?.GetValue(payload) is T typed)
        {
            value = typed;

            return true;
        }

        value = default;

        return false;
    }
}
