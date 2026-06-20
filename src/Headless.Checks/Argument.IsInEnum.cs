// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Headless.Checks.Internals;

namespace Headless.Checks;

public static partial class Argument
{
    /// <summary>Throws an <see cref="InvalidEnumArgumentException" /> if <paramref name="argument"/> is not a valid enum value.</summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="argument">The argument to check.</param>
    /// <param name="message">(Optional) Custom error message</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="argument" /> if the value is not out of range.</returns>
    /// <remarks>For <see cref="FlagsAttribute"/> enums, any combination of defined flag bits is accepted even when the combined value is not itself a named member.</remarks>
    /// <exception cref="InvalidEnumArgumentException"><paramref name="argument" /> if the value is out of range.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T IsInEnum<T>(
        T argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
        where T : struct, Enum
    {
        if (Enum.IsDefined(argument) || _IsValidFlagsCombination(typeof(T), _EnumToUInt64(argument)))
        {
            return argument;
        }

        message ??=
            $"The argument {paramName.ToAssertString()} = {argument} is not a valid value for Enum type <{typeof(T).Name}>. (Parameter: '{paramName}')";

#pragma warning disable MA0015
        throw new InvalidEnumArgumentException(message);
#pragma warning restore MA0015
    }

    /// <inheritdoc cref="IsInEnum{T}(T,string?,string?)"/>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int IsInEnum<T>(
        int argument,
        string? message = null,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null
    )
        where T : struct, Enum
    {
        if (
            Enum.IsDefined(typeof(T), argument) || _IsValidFlagsCombination(typeof(T), unchecked((ulong)(long)argument))
        )
        {
            return argument;
        }

        message ??=
            $"The argument {paramName.ToAssertString()} = {argument.ToString(CultureInfo.InvariantCulture)} is not a valid value for Enum type <{typeof(T).Name}>. (Parameter: '{paramName}')";

#pragma warning disable MA0015
        throw new InvalidEnumArgumentException(message);
#pragma warning restore MA0015
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="bits"/> is a non-zero combination of bits that are all
    /// covered by the defined members of a <see cref="FlagsAttribute"/> enum. Non-flags enums always return false
    /// (their values must be exact members, validated separately by <see cref="Enum.IsDefined{T}(T)"/>).
    /// </summary>
    private static bool _IsValidFlagsCombination(Type enumType, ulong bits)
    {
        if (bits == 0 || !enumType.IsDefined(typeof(FlagsAttribute), inherit: false))
        {
            return false;
        }

        ulong definedMask = 0;

        foreach (var value in Enum.GetValuesAsUnderlyingType(enumType))
        {
            definedMask |= _EnumToUInt64(value);
        }

        return (bits & ~definedMask) == 0;
    }

    private static ulong _EnumToUInt64(object value)
    {
        return Convert.GetTypeCode(value) switch
        {
            TypeCode.SByte or TypeCode.Int16 or TypeCode.Int32 or TypeCode.Int64 => unchecked(
                (ulong)Convert.ToInt64(value, CultureInfo.InvariantCulture)
            ),
            _ => Convert.ToUInt64(value, CultureInfo.InvariantCulture),
        };
    }
}
