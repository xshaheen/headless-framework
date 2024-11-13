// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace System;

[PublicAPI]
public static class EnumExtensions
{
    [SystemPure]
    [JetBrainsPure]
    public static string GetDisplayName(this Enum? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        var attribute = _GetFirstAttributeOrDefault<DisplayAttribute>(value);

        return attribute is null ? value.ToString() : attribute.Name ?? value.ToString();
    }

    [SystemPure]
    [JetBrainsPure]
    public static string GetDescription(this Enum? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        var attribute = _GetFirstAttributeOrDefault<DescriptionAttribute>(value);

        return attribute is null ? value.ToString() : attribute.Description;
    }

    [SystemPure]
    [JetBrainsPure]
    private static T? _GetFirstAttributeOrDefault<T>(Enum? value)
        where T : Attribute
    {
        if (value is null)
        {
            return null;
        }

        var member = value.GetType().GetMember(value.ToString());

        var attributes = member[0].GetCustomAttributes(typeof(T), inherit: false);

        return (T)attributes[0];
    }
}
