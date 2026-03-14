// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Headless.Checks;
using Headless.Primitives;
using Headless.Reflection;
using Humanizer;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace System;

[PublicAPI]
public static class EnumExtensions
{
    private sealed record CacheKey(Type EnumType, int Value);

    private static readonly ConcurrentDictionary<CacheKey, object> _LocaleCache = new();

    [RequiresUnreferencedCode("Uses Type.GetMember which is not compatible with trimming.")]
    [SystemPure, JetBrainsPure, MustUseReturnValue]
    public static MemberInfo GetEnumMemberInfo(this Enum enumValue)
    {
        return Argument.IsNotNull(enumValue).GetType().GetMember(enumValue.ToString())[0];
    }

    extension(Enum? enumValue)
    {
        [RequiresUnreferencedCode("Uses reflection to get attributes which is not compatible with trimming.")]
        [SystemPure, JetBrainsPure, MustUseReturnValue]
        public T? GetFirstAttribute<T>()
            where T : Attribute
        {
            return enumValue?.GetEnumMemberInfo().GetFirstAttribute<T>(inherit: false);
        }

        [RequiresUnreferencedCode("Uses reflection to get attributes which is not compatible with trimming.")]
        [SystemPure, JetBrainsPure, MustUseReturnValue]
        public string GetDisplayName()
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

        [RequiresUnreferencedCode("Uses reflection to get attributes which is not compatible with trimming.")]
        [SystemPure, JetBrainsPure, MustUseReturnValue]
        public string? GetDescription()
        {
            if (enumValue is null)
            {
                return null;
            }

            var attribute = enumValue.GetFirstAttribute<DescriptionAttribute>();

            return string.IsNullOrWhiteSpace(attribute?.Description) ? null : attribute.Description;
        }
    }

    extension<T>(T enumValue)
        where T : struct, Enum
    {
        [RequiresUnreferencedCode("Uses reflection to get attributes which is not compatible with trimming.")]
        [SystemPure, JetBrainsPure, MustUseReturnValue]
        public string GetLocaleName(string locale, string? fallbackLocale = null)
        {
            return enumValue.GetLocale(locale, fallbackLocale).DisplayName;
        }

        [RequiresUnreferencedCode("Uses reflection to get attributes which is not compatible with trimming.")]
        [SystemPure, JetBrainsPure, MustUseReturnValue]
        public AllLocaleValue<T> GetAllLocales()
        {
            Argument.IsNotNull(enumValue);

            return (AllLocaleValue<T>)
                _LocaleCache.GetOrAdd(
                    key: new(enumValue.GetType(), Value: Convert.ToInt32(enumValue, CultureInfo.InvariantCulture)),
                    valueFactory: static (_, value) =>
                    {
                        var defaultValue = new EnumLocale<T>
                        {
                            DisplayName = value.GetDisplayName(),
                            Description = value.GetDescription(),
                            Value = value,
                        };

                        var locales = value
                            .GetEnumMemberInfo()
                            .GetCustomAttributes<LocaleAttribute>(inherit: false)
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
                            .ToArray();

                        return new AllLocaleValue<T>(defaultValue, locales);
                    },
                    factoryArgument: enumValue
                );
        }

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
