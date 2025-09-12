// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Framework.Checks;
using Framework.Primitives;

namespace Framework.Abstractions;

public interface IEnumLocaleAccessor
{
    [SystemPure, JetBrainsPure, MustUseReturnValue]
    EnumLocale[] GetLocale<T>()
        where T : struct, Enum;

    [SystemPure, JetBrainsPure, MustUseReturnValue]
    EnumLocale GetLocale(Enum value);
}

public sealed class DefaultEnumLocaleAccessor(ICurrentLocale currentLocale) : IEnumLocaleAccessor
{
    public EnumLocale[] GetLocale<T>()
        where T : struct, Enum
    {
        return Enum.GetValues<T>().ConvertAll(x => x.GetLocale(currentLocale.Locale, currentLocale.Language));
    }

    public EnumLocale GetLocale(Enum value)
    {
        return value.GetLocale(currentLocale.Locale, currentLocale.Language);
    }
}
