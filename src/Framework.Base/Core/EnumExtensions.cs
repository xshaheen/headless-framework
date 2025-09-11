// Copyright (c) Mahmoud Shaheen. All rights reserved.

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

        if (!string.IsNullOrWhiteSpace(displayName))
        {
            return displayName;
        }

        return enumValue.ToString().Humanize();
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
    public static IEnumerable<LocaleAttribute> GetLocaleAttributes(this Enum enumValue)
    {
        return Argument.IsNotNull(enumValue).GetEnumMemberInfo().GetCustomAttributes<LocaleAttribute>(inherit: false);
    }

    [SystemPure, JetBrainsPure, MustUseReturnValue]
    public static IEnumerable<ValueLocale> GetLocale(this Enum enumValue)
    {
        var attributes = Argument.IsNotNull(enumValue).GetLocaleAttributes();

        return attributes.Select(attr => new ValueLocale
        {
            Locale = attr.Locale,
            DisplayName = attr.DisplayName,
            Description = attr.Description,
            Value = Convert.ToInt32(enumValue, CultureInfo.InvariantCulture),
        });
    }

    public static string GetLocaleName(this Enum value, string locale, string? fallbackLocale = null)
    {
        var allLocale = value.GetLocale().ToArray();
        var curr = allLocale.FirstOrDefault(x => string.Equals(x.Locale, locale, StringComparison.Ordinal));

        if (!string.IsNullOrWhiteSpace(curr?.DisplayName))
        {
            return curr.DisplayName;
        }

        if (!string.IsNullOrWhiteSpace(fallbackLocale))
        {
            curr = allLocale.FirstOrDefault(x => string.Equals(x.Locale, fallbackLocale, StringComparison.Ordinal));

            if (!string.IsNullOrWhiteSpace(curr?.DisplayName))
            {
                return curr.DisplayName;
            }
        }

        return value.GetDisplayName();
    }
}
