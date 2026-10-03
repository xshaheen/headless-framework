// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Primitives;

/// <summary>The complete set of localized values for an enum: the default locale plus per-locale entries.</summary>
/// <typeparam name="T">The enum value type being localized.</typeparam>
/// <param name="Default">The localization for the default locale.</param>
/// <param name="Locales">The localizations for each additional locale, keyed by locale.</param>
[PublicAPI]
public sealed record AllLocaleValue<T>(EnumLocale<T> Default, KeyEnumLocale<T>[] Locales);
