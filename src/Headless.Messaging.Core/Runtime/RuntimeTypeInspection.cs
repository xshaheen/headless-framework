// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;

namespace Headless.Messaging.Runtime;

/// <summary>
/// Reflection helpers used by the messaging runtime, storage providers, and dashboards to reason about
/// consumer and options types.
/// </summary>
internal static class RuntimeTypeInspection
{
    private static readonly ConditionalWeakTable<Type, TypeConverter> _ConverterCache = [];

    private static readonly ConditionalWeakTable<Type, TypeConverter>.CreateValueCallback _ConverterFactory =
        TypeDescriptor.GetConverter;

    /// <summary>
    /// Determines whether <paramref name="type"/> is a complex type — one that cannot be produced from a string
    /// via a primitive conversion or a <see cref="TypeConverter"/>. Dashboards use this to decide how to render
    /// consumer parameter inputs.
    /// </summary>
    /// <param name="type">The type to inspect.</param>
    public static bool IsComplexType(Type type)
    {
        return !_CanConvertFromString(type);
    }

    /// <summary>
    /// Determines whether <paramref name="type"/> declares an instance or static field of type <typeparamref name="T"/>.
    /// Storage providers use this to detect whether a <c>DbContext</c> wires an outbox bus/queue.
    /// </summary>
    /// <typeparam name="T">The field type to look for.</typeparam>
    /// <param name="type">The type whose declared fields are inspected.</param>
    public static bool DeclaresFieldOfType<T>(Type type)
    {
        const BindingFlags flags =
            BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Static
            | BindingFlags.Instance
            | BindingFlags.DeclaredOnly;

        return type.GetFields(flags).Any(x => x.FieldType == typeof(T));
    }

    private static bool _CanConvertFromString(Type destinationType)
    {
        destinationType = Nullable.GetUnderlyingType(destinationType) ?? destinationType;
        if (_IsSimpleType(destinationType))
        {
            return true;
        }

        var converter = _ConverterCache.GetValue(destinationType, _ConverterFactory);
        return converter.CanConvertFrom(typeof(string));
    }

    private static bool _IsSimpleType(Type type)
    {
        return type.GetTypeInfo().IsPrimitive
            || type == typeof(decimal)
            || type == typeof(string)
            || type == typeof(DateTime)
            || type == typeof(Guid)
            || type == typeof(DateTimeOffset)
            || type == typeof(TimeSpan)
            || type == typeof(Uri);
    }
}
