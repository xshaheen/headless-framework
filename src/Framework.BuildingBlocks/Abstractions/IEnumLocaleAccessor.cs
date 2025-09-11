// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Framework.Primitives;

namespace Framework.Abstractions;

public interface IEnumLocaleAccessor
{
    [SystemPure, JetBrainsPure, MustUseReturnValue]
    List<ValueLocale> GetLocale<T>(string locale, string? fallbackLocale = null)
        where T : struct, Enum;
}

public sealed class DefaultEnumLocaleAccessor : IEnumLocaleAccessor
{
    private readonly ConcurrentDictionary<Type, ValueLocaleCache[]> _cache = new();

    [SystemPure, JetBrainsPure, MustUseReturnValue]
    public List<ValueLocale> GetLocale<T>(string locale, string? fallbackLocale = null)
        where T : struct, Enum
    {
        var valueLocales = _cache.GetOrAdd(typeof(T), _ => getValues());

        var result = new List<ValueLocale>();

        foreach (var (defaultValue, localeAttributes) in valueLocales)
        {
            var valueLocale = localeAttributes.FirstOrDefault(x =>
                x.Locale.Equals(locale, StringComparison.OrdinalIgnoreCase)
            );

            if (valueLocale is null && !string.IsNullOrWhiteSpace(fallbackLocale))
            {
                valueLocale = localeAttributes.FirstOrDefault(x =>
                    x.Locale.Equals(fallbackLocale, StringComparison.OrdinalIgnoreCase)
                );
            }

            result.Add(valueLocale ?? defaultValue);
        }

        return result;

        static ValueLocaleCache[] getValues()
        {
            var values = Enum.GetValues<T>();

            return values.ConvertAll(x =>
            {
                var valueLocale = new ValueLocale
                {
                    Locale = "default",
                    DisplayName = x.GetDisplayName(),
                    Description = x.GetDescription(),
                    Value = Convert.ToInt32(x, CultureInfo.InvariantCulture),
                };

                return new ValueLocaleCache(valueLocale, [.. x.GetLocale()]);
            });
        }
    }

    private sealed record ValueLocaleCache(ValueLocale Default, ValueLocale[] LocaleValues);
}
