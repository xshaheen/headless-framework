// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Context;
using Headless.Primitives;

namespace Headless.Api;

/// <summary>
/// Default <see cref="IEnumLocaleAccessor"/> implementation that delegates to the
/// <c>HeadlessEnumExtensions.GetLocale</c> extension methods using the locale and language
/// exposed by the injected <see cref="ICurrentLocale"/>.
/// </summary>
public sealed class DefaultEnumLocaleAccessor(ICurrentLocale currentLocale) : IEnumLocaleAccessor
{
    /// <inheritdoc/>
    public EnumLocale<T>[] GetLocale<T>()
        where T : struct, Enum
    {
        return Enum.GetValues<T>().ConvertAll(x => x.GetLocale(currentLocale.Locale, currentLocale.Language));
    }

    /// <inheritdoc/>
    public EnumLocale<T> GetLocale<T>(T value)
        where T : struct, Enum
    {
        return value.GetLocale(currentLocale.Locale, currentLocale.Language);
    }
}
