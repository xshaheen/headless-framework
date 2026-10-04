// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Context;
using Headless.Primitives;
using JetBrainsPureAttribute = JetBrains.Annotations.PureAttribute;
using SystemPureAttribute = System.Diagnostics.Contracts.PureAttribute;

namespace Headless.Api;

/// <summary>
/// Resolves locale-aware display metadata for enum values using <see cref="ICurrentLocale"/>.
/// </summary>
public interface IEnumLocaleAccessor
{
    /// <summary>
    /// Returns locale-aware display metadata for every member of the enum type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The enum type whose members to localize.</typeparam>
    /// <returns>An array of localized enum metadata entries.</returns>
    [SystemPure, JetBrainsPure, MustUseReturnValue]
    EnumLocale<T>[] GetLocale<T>()
        where T : struct, Enum;

    /// <summary>
    /// Returns locale-aware display metadata for a single enum member.
    /// </summary>
    /// <typeparam name="T">The enum type.</typeparam>
    /// <param name="value">The enum value to localize.</param>
    /// <returns>The localized enum metadata.</returns>
    [SystemPure, JetBrainsPure, MustUseReturnValue]
    EnumLocale<T> GetLocale<T>(T value)
        where T : struct, Enum;
}
