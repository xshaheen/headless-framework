// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Headless.Checks.Internal;

namespace Headless.Checks;

public static partial class Argument
{
    /// <summary>Throws an <see cref="InvalidEnumArgumentException" /> when <paramref name="argument"/> is not a valid enum value.</summary>
    /// <typeparam name="T">The enum type to validate against.</typeparam>
    /// <param name="argument">The argument to check.</param>
    /// <param name="message">A custom error message, or <see langword="null"/> to use the default error message.</param>
    /// <param name="paramName">The name of the parameter being checked. Captured automatically by the compiler.</param>
    /// <returns><paramref name="argument" /> when the value is not out of range.</returns>
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
        if (!Enum.IsDefined(argument) && !(EnumInfo<T>.IsFlags && _IsValidFlagsCombination<T>(_EnumToUInt64(argument))))
        {
            _ThrowForIsInEnum(argument, message, paramName);
        }

        return argument;
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
        // An int-backed enum is checked through the generic overload so the int is not boxed on every call; any
        // other underlying type keeps the non-generic overload and its ArgumentException for a mismatched type.
        var isDefined = EnumInfo<T>.IsInt32Backed
            ? Enum.IsDefined(Unsafe.As<int, T>(ref argument))
            : Enum.IsDefined(typeof(T), argument);

        if (!isDefined && !_IsValidFlagsCombination<T>(unchecked((ulong)argument)))
        {
            _ThrowForIsInEnumInt<T>(argument, message, paramName);
        }

        return argument;
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="bits"/> is a non-zero combination of bits that are all
    /// covered by the defined members of a <see cref="FlagsAttribute"/> enum. Non-flags enums always return false
    /// (their values must be exact members, validated separately by <see cref="Enum.IsDefined{T}(T)"/>).
    /// </summary>
    private static bool _IsValidFlagsCombination<T>(ulong bits)
        where T : struct, Enum
    {
        return bits != 0 && EnumInfo<T>.IsFlags && (bits & ~EnumInfo<T>.DefinedMask) == 0;
    }

    // Computed once per enum type: the flags attribute and the defined-bits mask never change at run time, and
    // reflecting over them on each guard call boxes every member.
    private static class EnumInfo<T>
        where T : struct, Enum
    {
        public static readonly bool IsFlags = typeof(T).IsDefined(typeof(FlagsAttribute), inherit: false);

        public static readonly bool IsInt32Backed = Enum.GetUnderlyingType(typeof(T)) == typeof(int);

        public static readonly ulong DefinedMask = _ComputeDefinedMask();

        private static ulong _ComputeDefinedMask()
        {
            ulong definedMask = 0;

            foreach (var value in Enum.GetValuesAsUnderlyingType<T>())
            {
                definedMask |= _EnumToUInt64(value);
            }

            return definedMask;
        }
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

    [DoesNotReturn]
    private static void _ThrowForIsInEnum<T>(T argument, string? message, string? paramName)
        where T : struct, Enum
    {
        message ??=
            $"The argument {paramName.ToAssertString()} = {argument} is not a valid value for Enum type <{typeof(T).Name}>. (Parameter: '{paramName}')";

#pragma warning disable MA0015 // InvalidEnumArgumentException has no message-and-paramName overload; the message names the parameter.
        throw new InvalidEnumArgumentException(message);
#pragma warning restore MA0015
    }

    [DoesNotReturn]
    private static void _ThrowForIsInEnumInt<T>(int argument, string? message, string? paramName)
        where T : struct, Enum
    {
        message ??=
            $"The argument {paramName.ToAssertString()} = {argument.ToString(CultureInfo.InvariantCulture)} is not a valid value for Enum type <{typeof(T).Name}>. (Parameter: '{paramName}')";

#pragma warning disable MA0015 // InvalidEnumArgumentException has no message-and-paramName overload; the message names the parameter.
        throw new InvalidEnumArgumentException(message);
#pragma warning restore MA0015
    }
}
