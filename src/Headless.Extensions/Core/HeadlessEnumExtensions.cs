// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Runtime.CompilerServices;
using Headless.Checks;
using Headless.Primitives;
using Headless.Reflection;
using Humanizer;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace System;

/// <summary>Extension methods for reading metadata (display names, descriptions, and locales) from enum values.</summary>
[PublicAPI]
public static class HeadlessEnumExtensions
{
    private static readonly ConditionalWeakTable<Type, ConcurrentDictionary<ulong, object>> _LocaleCache = [];

    private static readonly ConditionalWeakTable<
        Type,
        ConcurrentDictionary<ulong, object>
    >.CreateValueCallback _CreateLocaleInner = static _ => new ConcurrentDictionary<ulong, object>();

    // Display-name and description lookups are pure reflection; cache them per (Type, value) so repeated
    // calls do not re-walk the member's attributes. The inner dictionary is keyed by the boxed enum value,
    // whose equality already incorporates both the enum type and its underlying value.
    private static readonly ConditionalWeakTable<Type, ConcurrentDictionary<Enum, string>> _DisplayNameCache = [];

    private static readonly ConditionalWeakTable<
        Type,
        ConcurrentDictionary<Enum, string>
    >.CreateValueCallback _CreateDisplayNameInner = static _ => new ConcurrentDictionary<Enum, string>();

    private static readonly ConditionalWeakTable<Type, ConcurrentDictionary<Enum, string?>> _DescriptionCache = [];

    private static readonly ConditionalWeakTable<
        Type,
        ConcurrentDictionary<Enum, string?>
    >.CreateValueCallback _CreateDescriptionInner = static _ => new ConcurrentDictionary<Enum, string?>();

    /// <summary>
    /// Reads the raw underlying bits of an enum value into a <see cref="ulong"/>, zero-extending
    /// smaller underlying types. Safe for every CLR enum underlying type (<see cref="byte"/>,
    /// <see cref="sbyte"/>, <see cref="short"/>, <see cref="ushort"/>, <see cref="int"/>,
    /// <see cref="uint"/>, <see cref="long"/>, <see cref="ulong"/>) and avoids the
    /// <see cref="OverflowException"/> that <see cref="Convert.ToInt32(object?)"/> would throw
    /// on large 64-bit-backed enums.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong _GetEnumRawValue<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        return Unsafe.SizeOf<TEnum>() switch
        {
            1 => Unsafe.As<TEnum, byte>(ref value),
            2 => Unsafe.As<TEnum, ushort>(ref value),
            4 => Unsafe.As<TEnum, uint>(ref value),
            8 => Unsafe.As<TEnum, ulong>(ref value),
            _ => throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Unsupported enum underlying size: {Unsafe.SizeOf<TEnum>()} bytes."
                )
            ),
        };
    }

    /// <summary>Gets the <see cref="MemberInfo"/> for the field that declares the given enum value.</summary>
    /// <param name="enumValue">The enum value whose declaring member is resolved.</param>
    /// <returns>
    /// The <see cref="MemberInfo"/> of the field declaring <paramref name="enumValue"/>, or <see langword="null"/>
    /// when the value is not a declared member (for example an undefined cast or a combination of <c>[Flags]</c> values).
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="enumValue"/> is <see langword="null"/>.</exception>
    [RequiresUnreferencedCode("Uses Type.GetMember which is not compatible with trimming.")]
    [SystemPure, JetBrainsPure, MustUseReturnValue]
    public static MemberInfo? GetEnumMemberInfo(this Enum enumValue)
    {
        // GetMember returns an empty array for values without a declared field (undefined casts, flag
        // combinations); indexing [0] would throw IndexOutOfRangeException, so return null instead.
        var members = Argument.IsNotNull(enumValue).GetType().GetMember(enumValue.ToString());

        return members.Length > 0 ? members[0] : null;
    }

    extension(Enum? enumValue)
    {
        /// <summary>Gets the first attribute of type <typeparamref name="T"/> declared on the enum value's member.</summary>
        /// <typeparam name="T">The attribute type to look for.</typeparam>
        /// <returns>
        /// The first matching attribute, or <see langword="null"/> if none is declared or the enum value is
        /// <see langword="null"/>.
        /// </returns>
        [RequiresUnreferencedCode("Uses reflection to get attributes which is not compatible with trimming.")]
        [SystemPure, JetBrainsPure, MustUseReturnValue]
        public T? GetFirstAttribute<T>()
            where T : Attribute
        {
            return enumValue?.GetEnumMemberInfo()?.GetFirstOrDefaultAttribute<T>(inherit: false);
        }

        /// <summary>
        /// Gets a human-readable display name for the enum value, preferring a <see cref="DisplayNameAttribute"/>,
        /// then a <see cref="DisplayAttribute"/>, and finally a humanized form of the member name.
        /// </summary>
        /// <returns>
        /// The resolved display name, or <see cref="string.Empty"/> if the enum value is <see langword="null"/>.
        /// </returns>
        [RequiresUnreferencedCode("Uses reflection to get attributes which is not compatible with trimming.")]
        [SystemPure, JetBrainsPure, MustUseReturnValue]
        public string GetDisplayName()
        {
            if (enumValue is null)
            {
                return string.Empty;
            }

            var inner = _DisplayNameCache.GetValue(enumValue.GetType(), _CreateDisplayNameInner);

            return inner.GetOrAdd(
                enumValue,
                static value =>
                {
                    var displayName = value.GetFirstAttribute<DisplayNameAttribute>()?.DisplayName;

                    if (!string.IsNullOrWhiteSpace(displayName))
                    {
                        return displayName;
                    }

                    displayName = value.GetFirstAttribute<DisplayAttribute>()?.Name;

                    return !string.IsNullOrWhiteSpace(displayName) ? displayName : value.ToString().Humanize();
                }
            );
        }

        /// <summary>Gets the description declared on the enum value via <see cref="DescriptionAttribute"/>.</summary>
        /// <returns>
        /// The description text, or <see langword="null"/> if none is declared or the enum value is
        /// <see langword="null"/>.
        /// </returns>
        [RequiresUnreferencedCode("Uses reflection to get attributes which is not compatible with trimming.")]
        [SystemPure, JetBrainsPure, MustUseReturnValue]
        public string? GetDescription()
        {
            if (enumValue is null)
            {
                return null;
            }

            var inner = _DescriptionCache.GetValue(enumValue.GetType(), _CreateDescriptionInner);

            return inner.GetOrAdd(
                enumValue,
                static value =>
                {
                    var attribute = value.GetFirstAttribute<DescriptionAttribute>();

                    return string.IsNullOrWhiteSpace(attribute?.Description) ? null : attribute.Description;
                }
            );
        }
    }

    extension<T>(T enumValue)
        where T : struct, Enum
    {
        /// <summary>Gets the localized display name of the enum value for the requested locale.</summary>
        /// <param name="locale">The locale key (e.g. <c>"ar"</c>) to look up.</param>
        /// <param name="fallbackLocale">(Optional) A locale key to fall back to when <paramref name="locale"/> has no entry.</param>
        /// <returns>
        /// The display name for <paramref name="locale"/>, the display name for <paramref name="fallbackLocale"/>,
        /// or the default display name when neither locale is declared.
        /// </returns>
        [RequiresUnreferencedCode("Uses reflection to get attributes which is not compatible with trimming.")]
        [SystemPure, JetBrainsPure, MustUseReturnValue]
        public string GetLocaleName(string locale, string? fallbackLocale = null)
        {
            return enumValue.GetLocale(locale, fallbackLocale).DisplayName;
        }

        /// <summary>
        /// Gets the default and all locale-specific values declared on the enum value via
        /// <see cref="LocaleAttribute"/>. Results are cached per enum type and value.
        /// </summary>
        /// <returns>An <see cref="AllLocaleValue{T}"/> holding the default value and every declared locale entry.</returns>
        [RequiresUnreferencedCode("Uses reflection to get attributes which is not compatible with trimming.")]
        [SystemPure, JetBrainsPure, MustUseReturnValue]
        public AllLocaleValue<T> GetAllLocales()
        {
            Argument.IsNotNull(enumValue);

            var enumType = enumValue.GetType();
            var rawValue = _GetEnumRawValue(enumValue);
            var inner = _LocaleCache.GetValue(enumType, _CreateLocaleInner);

            return (AllLocaleValue<T>)
                inner.GetOrAdd(
                    rawValue,
                    static (_, value) =>
                    {
                        var defaultValue = new EnumLocale<T>
                        {
                            DisplayName = value.GetDisplayName(),
                            Description = value.GetDescription(),
                            Value = value,
                        };

                        var locales =
                            value
                                .GetEnumMemberInfo()
                                ?.GetCustomAttributes<LocaleAttribute>(inherit: false)
                                .Select(attr => new KeyEnumLocale<T>
                                {
                                    Key = attr.Locale,
                                    Locale = new()
                                    {
                                        DisplayName = attr.DisplayName,
                                        Description = attr.Description,
                                        Value = value,
                                    },
                                })
                                .ToArray()
                            ?? [];

                        return new AllLocaleValue<T>(defaultValue, locales);
                    },
                    enumValue
                );
        }

        /// <summary>Gets the localized value of the enum value for the requested locale.</summary>
        /// <param name="locale">The locale key (e.g. <c>"ar"</c>) to look up.</param>
        /// <param name="fallbackLocale">(Optional) A locale key to fall back to when <paramref name="locale"/> has no entry.</param>
        /// <returns>
        /// The <see cref="EnumLocale{T}"/> for <paramref name="locale"/>, for <paramref name="fallbackLocale"/>
        /// when it has a non-empty display name, or the default value when neither locale is declared.
        /// </returns>
        [RequiresUnreferencedCode("Uses reflection to get attributes which is not compatible with trimming.")]
        [SystemPure, JetBrainsPure, MustUseReturnValue]
        public EnumLocale<T> GetLocale(string locale, string? fallbackLocale = null)
        {
            var (defaultValue, locales) = enumValue.GetAllLocales();

            var main = locales.FirstOrDefault(x => string.Equals(x.Key, locale, StringComparison.Ordinal));

            if (main is not null)
            {
                return main.Locale;
            }

            if (!string.IsNullOrWhiteSpace(fallbackLocale))
            {
                var fallback = locales.FirstOrDefault(x =>
                    string.Equals(x.Key, fallbackLocale, StringComparison.Ordinal)
                );

                if (!string.IsNullOrWhiteSpace(fallback?.Locale.DisplayName))
                {
                    return fallback.Locale;
                }
            }

            return defaultValue;
        }
    }
}
