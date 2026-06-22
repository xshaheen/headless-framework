// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.Abstractions;

/// <summary>
/// Resolves locale-aware display metadata for enum values using the current locale from
/// <see cref="ICurrentLocale"/>. Display names and descriptions are sourced from
/// <c>[LocaleAttribute]</c> annotations on enum members. Inject this service into components
/// that need to present enum values to users in the correct language.
/// </summary>
public interface IEnumLocaleAccessor
{
    /// <summary>
    /// Returns locale-aware display metadata for every member of the enum type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The enum type whose members should be localized.</typeparam>
    /// <returns>
    /// An array of <see cref="EnumLocale{T}"/> — one entry per enum member — each containing a
    /// localized <c>DisplayName</c>, optional <c>Description</c>, and the enum <c>Value</c>.
    /// </returns>
    [SystemPure, JetBrainsPure, MustUseReturnValue]
    EnumLocale<T>[] GetLocale<T>()
        where T : struct, Enum;

    /// <summary>
    /// Returns locale-aware display metadata for a single enum member.
    /// </summary>
    /// <typeparam name="T">The enum type that contains <paramref name="value"/>.</typeparam>
    /// <param name="value">The specific enum member to localize.</param>
    /// <returns>
    /// An <see cref="EnumLocale{T}"/> containing the localized <c>DisplayName</c>, optional
    /// <c>Description</c>, and the enum <c>Value</c>.
    /// </returns>
    [SystemPure, JetBrainsPure, MustUseReturnValue]
    EnumLocale<T> GetLocale<T>(T value)
        where T : struct, Enum;
}

/// <summary>
/// Default <see cref="IEnumLocaleAccessor"/> implementation that delegates to the
/// <c>EnumExtensions.GetLocale</c> extension methods using the locale and language
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
