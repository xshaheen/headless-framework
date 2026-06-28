// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Checks;

namespace Headless.Validators;

/// <summary>
/// Validates that a string is the name of a defined member of an enum type. Member-name sets are
/// resolved once per (enum type, case sensitivity) and cached as a <see cref="FrozenSet{T}"/>, so
/// repeated checks are O(1) lookups with no per-call reflection.
/// </summary>
[PublicAPI]
public static class EnumNameValidator
{
    private static readonly ConcurrentDictionary<Type, FrozenSet<string>> _OrdinalNames = new();
    private static readonly ConcurrentDictionary<Type, FrozenSet<string>> _IgnoreCaseNames = new();

    /// <summary>
    /// Determines whether <paramref name="name"/> is the name of a defined member of
    /// <paramref name="enumType"/>. Numeric strings are rejected even when they correspond to a
    /// defined value. <see langword="null"/> returns <see langword="false"/>.
    /// </summary>
    /// <param name="enumType">The enum type whose member names are accepted.</param>
    /// <param name="name">The candidate member name.</param>
    /// <param name="ignoreCase">When <see langword="true"/>, matching is case-insensitive. Defaults to <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="name"/> is a defined member name; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="enumType"/> is not an enum type.</exception>
    public static bool IsDefinedName(Type enumType, string? name, bool ignoreCase = false)
    {
        return name is not null && GetNames(enumType, ignoreCase).Contains(name);
    }

    /// <summary>Generic overload of <see cref="IsDefinedName(Type, string, bool)"/>.</summary>
    /// <typeparam name="TEnum">The enum type whose member names are accepted.</typeparam>
    /// <param name="name">The candidate member name.</param>
    /// <param name="ignoreCase">When <see langword="true"/>, matching is case-insensitive. Defaults to <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="name"/> is a defined member name; otherwise <see langword="false"/>.</returns>
    public static bool IsDefinedName<TEnum>(string? name, bool ignoreCase = false)
        where TEnum : struct, Enum
    {
        return IsDefinedName(typeof(TEnum), name, ignoreCase);
    }

    /// <summary>
    /// Gets the cached set of member names for <paramref name="enumType"/> using the requested case
    /// sensitivity. The set uses <see cref="StringComparer.Ordinal"/> or
    /// <see cref="StringComparer.OrdinalIgnoreCase"/> for its containment checks.
    /// </summary>
    /// <param name="enumType">The enum type whose member names are returned.</param>
    /// <param name="ignoreCase">When <see langword="true"/>, the set matches names case-insensitively. Defaults to <see langword="false"/>.</param>
    /// <returns>A cached, immutable set of the enum's member names.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="enumType"/> is not an enum type.</exception>
    public static FrozenSet<string> GetNames(Type enumType, bool ignoreCase = false)
    {
        Argument.IsNotNull(enumType);

        if (!enumType.IsEnum)
        {
            throw new ArgumentException($"'{enumType}' is not an enum type.", nameof(enumType));
        }

        return ignoreCase
            ? _IgnoreCaseNames.GetOrAdd(
                enumType,
                static type => Enum.GetNames(type).ToFrozenSet(StringComparer.OrdinalIgnoreCase)
            )
            : _OrdinalNames.GetOrAdd(enumType, static type => Enum.GetNames(type).ToFrozenSet(StringComparer.Ordinal));
    }
}
