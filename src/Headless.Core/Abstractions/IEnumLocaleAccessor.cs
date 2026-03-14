// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.Abstractions;

public interface IEnumLocaleAccessor
{
    [SystemPure, JetBrainsPure, MustUseReturnValue]
    EnumLocale<T>[] GetLocale<T>()
        where T : struct, Enum;

    [SystemPure, JetBrainsPure, MustUseReturnValue]
    EnumLocale<T> GetLocale<T>(T value)
        where T : struct, Enum;
}

public sealed class DefaultEnumLocaleAccessor(ICurrentLocale currentLocale) : IEnumLocaleAccessor
{
    public EnumLocale<T>[] GetLocale<T>()
        where T : struct, Enum
    {
        return Enum.GetValues<T>().ConvertAll(x => x.GetLocale(currentLocale.Locale, currentLocale.Language));
    }

    public EnumLocale<T> GetLocale<T>(T value)
        where T : struct, Enum
    {
        return value.GetLocale(currentLocale.Locale, currentLocale.Language);
    }
}
