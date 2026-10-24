// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Reflection;

namespace Headless.Api;

/// <summary>
/// Checks a route, query, header, or form value the way the request delegate factory parses it, so a value it would
/// reject is answered with the parameter's name before the factory sees it.
/// </summary>
/// <remarks>
/// The parse mirrors the factory's choice of method and its arguments: <see cref="Uri"/> and the date and time types
/// with the styles the factory passes, then <see cref="IParsable{TSelf}"/> (which for the numeric types applies the same
/// number styles the factory passes), then a public static <c>TryParse(string, IFormatProvider, out T)</c>, then
/// <c>TryParse(string, out T)</c>, always with the invariant culture. A type it cannot match exactly gets no checker,
/// so the factory's own answer stands and no value the factory accepts is ever rejected here.
/// </remarks>
internal static class SimpleValueParser
{
    private static readonly ConcurrentDictionary<Type, Func<string, bool>?> _Cache = new();

    private static readonly MethodInfo _ParsableMethod = typeof(SimpleValueParser).GetMethod(
        nameof(_Parsable),
        BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly
    )!;

    private static readonly MethodInfo _ProviderMethod = typeof(SimpleValueParser).GetMethod(
        nameof(_WithProvider),
        BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly
    )!;

    private static readonly MethodInfo _PlainMethod = typeof(SimpleValueParser).GetMethod(
        nameof(_Plain),
        BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly
    )!;

    /// <summary>
    /// Gets the checker for <paramref name="type"/> (not nullable, not an array), or <see langword="null"/> when the
    /// type binds without parsing (<see cref="string"/>) or not by <c>TryParse</c>.
    /// </summary>
    public static Func<string, bool>? For(Type type) => _Cache.GetOrAdd(type, _Build);

    private static Func<string, bool>? _Build(Type type)
    {
        if (type == typeof(string) || type.IsEnum || type.ContainsGenericParameters)
        {
            return null;
        }

        // The factory prefers BindAsync, which reads the whole request rather than one value.
        if (
            type.GetMethod("BindAsync", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            is not null
        )
        {
            return null;
        }

        if (type == typeof(Uri))
        {
            return static value => Uri.TryCreate(value, UriKind.RelativeOrAbsolute, out _);
        }

        if (type == typeof(DateTime))
        {
            return static value =>
                DateTime.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AllowWhiteSpaces,
                    out _
                );
        }

        if (type == typeof(DateTimeOffset))
        {
            return static value =>
                DateTimeOffset.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces,
                    out _
                );
        }

        if (type == typeof(DateOnly))
        {
            return static value =>
                DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out _);
        }

        if (type == typeof(TimeOnly))
        {
            return static value =>
                TimeOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out _);
        }

        if (_IsSelfParsable(type))
        {
            return _ParsableMethod.MakeGenericMethod(type).CreateDelegate<Func<string, bool>>();
        }

        var withProvider = type.GetMethod(
            "TryParse",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy,
            [typeof(string), typeof(IFormatProvider), type.MakeByRefType()]
        );

        if (withProvider?.ReturnType == typeof(bool))
        {
            return (Func<string, bool>)_ProviderMethod.MakeGenericMethod(type).Invoke(null, [withProvider])!;
        }

        var plain = type.GetMethod(
            "TryParse",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy,
            [typeof(string), type.MakeByRefType()]
        );

        return plain?.ReturnType == typeof(bool)
            ? (Func<string, bool>)_PlainMethod.MakeGenericMethod(type).Invoke(null, [plain])!
            : null;
    }

    // Closing IParsable<> over a type that does not implement it throws, so look for the implementation instead.
    private static bool _IsSelfParsable(Type type) =>
        Array.Exists(
            type.GetInterfaces(),
            x =>
                x.IsGenericType
                && x.GetGenericTypeDefinition() == typeof(IParsable<>)
                && x.GenericTypeArguments[0] == type
        );

    private static bool _Parsable<T>(string value)
        where T : IParsable<T> => T.TryParse(value, CultureInfo.InvariantCulture, out _);

    private static Func<string, bool> _WithProvider<T>(MethodInfo method)
    {
        var tryParse = method.CreateDelegate<TryParseWithProvider<T>>();

        return value => tryParse(value, CultureInfo.InvariantCulture, out _);
    }

    private static Func<string, bool> _Plain<T>(MethodInfo method)
    {
        var tryParse = method.CreateDelegate<TryParsePlain<T>>();

        return value => tryParse(value, out _);
    }

    private delegate bool TryParseWithProvider<T>(string value, IFormatProvider? provider, out T result);

    private delegate bool TryParsePlain<T>(string value, out T result);
}
