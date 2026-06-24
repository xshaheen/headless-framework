// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Headless.Checks.Internals;

namespace Headless.Checks;

public static partial class Argument
{
    /// <summary>
    /// Throws an <see cref="ArgumentException" /> if <paramref name="value"/> is not bitwise-equal to
    /// <paramref name="target"/> (their raw memory representations differ).
    /// </summary>
    /// <typeparam name="T">An unmanaged type.</typeparam>
    /// <param name="value">The argument to check.</param>
    /// <param name="target">The value to compare against.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="value" /> if it is bitwise-equal to <paramref name="target"/>.</returns>
    /// <remarks>
    /// Compares the underlying bytes rather than calling <see cref="object.Equals(object?)"/>. This distinguishes values
    /// that compare equal but differ in representation (for example <c>+0.0</c> vs <c>-0.0</c>) and treats two
    /// identically-encoded <see cref="double.NaN"/> payloads as equal. Use <see cref="IsEqualTo{T}(T,T,string?,string?)"/>
    /// for value equality instead.
    /// </remarks>
    /// <exception cref="ArgumentException">if <paramref name="value" /> is not bitwise-equal to <paramref name="target"/>.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T IsBitwiseEqualTo<T>(
        T value,
        T target,
        string? message = null,
        [CallerArgumentExpression(nameof(value))] string? paramName = null
    )
        where T : unmanaged
    {
        return _AreBytesEqual(value, target) ? value : _ThrowNotBitwiseEqualTo(value, target, message, paramName);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool _AreBytesEqual<T>(T value, T target)
        where T : unmanaged
    {
        // Unsafe.SizeOf<T>() is a JIT constant per instantiation, so this switch folds to a single branch.
        switch (Unsafe.SizeOf<T>())
        {
            case 1:
                return Unsafe.As<T, byte>(ref value) == Unsafe.As<T, byte>(ref target);
            case 2:
                return Unsafe.As<T, ushort>(ref value) == Unsafe.As<T, ushort>(ref target);
            case 4:
                return Unsafe.As<T, uint>(ref value) == Unsafe.As<T, uint>(ref target);
            case 8:
                return Unsafe.As<T, ulong>(ref value) == Unsafe.As<T, ulong>(ref target);
            case 16:
            {
                // Two 64-bit compares for 16-byte types (Guid, decimal, Int128) — avoids the SequenceEqual call.
                ref var valueULong = ref Unsafe.As<T, ulong>(ref value);
                ref var targetULong = ref Unsafe.As<T, ulong>(ref target);

                return valueULong == targetULong && Unsafe.Add(ref valueULong, 1) == Unsafe.Add(ref targetULong, 1);
            }
            default:
            {
                var size = Unsafe.SizeOf<T>();
                ref var valueByte = ref Unsafe.As<T, byte>(ref value);
                ref var targetByte = ref Unsafe.As<T, byte>(ref target);

                return MemoryMarshal
                    .CreateReadOnlySpan(ref valueByte, size)
                    .SequenceEqual(MemoryMarshal.CreateReadOnlySpan(ref targetByte, size));
            }
        }
    }

    [DoesNotReturn]
    private static T _ThrowNotBitwiseEqualTo<T>(T value, T target, string? message, string? paramName)
        where T : unmanaged
    {
        throw new ArgumentException(
            message ?? $"The argument {paramName.ToAssertString()} must be bitwise-equal to {target.ToAssertString()}.",
            paramName
        );
    }
}
