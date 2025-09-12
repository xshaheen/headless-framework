// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Framework.Checks;
using Framework.Primitives;
using Framework.Reflection;
using Humanizer;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace System;

[PublicAPI]
public static class EnumExtensions
{
    private sealed record CacheKey(Type EnumType, int Value);

    private static readonly ConcurrentDictionary<CacheKey, AllLocaleValue> _LocaleCache = new();

    [SystemPure, JetBrainsPure, MustUseReturnValue]
    public static MemberInfo GetEnumMemberInfo(this Enum enumValue)
    {
        return Argument.IsNotNull(enumValue).GetType().GetMember(enumValue.ToString())[0];
    }

    [SystemPure, JetBrainsPure, MustUseReturnValue]
    public static T? GetFirstAttribute<T>(this Enum? enumValue)
        where T : Attribute
    {
        return enumValue?.GetEnumMemberInfo().GetFirstAttribute<T>(inherit: false);
    }

    [SystemPure, JetBrainsPure, MustUseReturnValue]
    public static string GetDisplayName(this Enum? enumValue)
    {
        if (enumValue is null)
        {
            return string.Empty;
        }

        var displayName = enumValue.GetFirstAttribute<DisplayNameAttribute>()?.DisplayName;

        if (!string.IsNullOrWhiteSpace(displayName))
        {
            return displayName;
        }

        displayName = enumValue.GetFirstAttribute<DisplayAttribute>()?.Name;

        return !string.IsNullOrWhiteSpace(displayName) ? displayName : enumValue.ToString().Humanize();
    }

    [SystemPure, JetBrainsPure, MustUseReturnValue]
    public static string? GetDescription(this Enum? enumValue)
    {
        if (enumValue is null)
        {
            return null;
        }

        var attribute = enumValue.GetFirstAttribute<DescriptionAttribute>();

        return string.IsNullOrWhiteSpace(attribute?.Description) ? null : attribute.Description;
    }

    [SystemPure, JetBrainsPure, MustUseReturnValue]
    public static AllLocaleValue GetAllLocales(this Enum enumValue)
    {
        Argument.IsNotNull(enumValue);

        return _LocaleCache.GetOrAdd(
            key: new(enumValue.GetType(), Value: Convert.ToInt32(enumValue, CultureInfo.InvariantCulture)),
            valueFactory: static (key, enumValue1) =>
            {
                var defaultValue = new EnumLocale
                {
                    DisplayName = enumValue1.GetDisplayName(),
                    Description = enumValue1.GetDescription(),
                    Value = key.Value,
                };

                var locales = enumValue1
                    .GetEnumMemberInfo()
                    .GetCustomAttributes<LocaleAttribute>(inherit: false)
                    .Select(attr => new KeyEnumLocale
                    {
                        Key = attr.Locale,
                        Locale = new()
                        {
                            DisplayName = attr.DisplayName,
                            Description = attr.Description,
                            Value = key.Value,
                        },
                    })
                    .ToArray();

                return new AllLocaleValue(defaultValue, locales);
            },
            factoryArgument: enumValue
        );
    }

    [SystemPure, JetBrainsPure, MustUseReturnValue]
    public static EnumLocale GetLocale(this Enum value, string locale, string? fallbackLocale = null)
    {
        var (defaultValue, locales) = value.GetAllLocales();

        var main = locales.FirstOrDefault(x => string.Equals(x.Key, locale, StringComparison.Ordinal));

        if (main is not null)
        {
            return main.Locale;
        }

        if (!string.IsNullOrWhiteSpace(fallbackLocale))
        {
            var fallback = locales.FirstOrDefault(x => string.Equals(x.Key, fallbackLocale, StringComparison.Ordinal));

            if (!string.IsNullOrWhiteSpace(fallback?.Locale.DisplayName))
            {
                return fallback.Locale;
            }
        }

        return defaultValue;
    }

    [SystemPure, JetBrainsPure, MustUseReturnValue]
    public static string GetLocaleName(this Enum value, string locale, string? fallbackLocale = null)
    {
        return value.GetLocale(locale, fallbackLocale).DisplayName;
    }
}
