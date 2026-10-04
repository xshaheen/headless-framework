// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Headless.Checks.Internal;

namespace Headless.Checks;

public static partial class Argument
{
    /// <summary>Throws an <see cref="ArgumentException" /> when <paramref name="stream" /> does not support reading.</summary>
    /// <param name="stream">The argument <see cref="Stream"/> instance to check.</param>
    /// <param name="paramName">The name of the parameter being checked. Captured automatically by the compiler.</param>
    /// <exception cref="ArgumentException"><paramref name="stream"/> doesn't support reading.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void CanRead(Stream stream, [CallerArgumentExpression(nameof(stream))] string? paramName = null)
    {
        if (!stream.CanRead)
        {
            _ThrowForCanRead(stream, paramName);
        }
    }

    /// <summary>Throws an <see cref="ArgumentException" /> when <paramref name="stream" /> does not support writing.</summary>
    /// <param name="stream">The argument <see cref="Stream"/> instance to check.</param>
    /// <param name="paramName">The name of the parameter being checked. Captured automatically by the compiler.</param>
    /// <exception cref="ArgumentException"><paramref name="stream"/> doesn't support writing.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void CanWrite(Stream stream, [CallerArgumentExpression(nameof(stream))] string? paramName = null)
    {
        if (!stream.CanWrite)
        {
            _ThrowForCanWrite(stream, paramName);
        }
    }

    /// <summary>Throws an <see cref="ArgumentException" /> when <paramref name="stream" /> does not support seeking.</summary>
    /// <param name="stream">The argument <see cref="Stream"/> instance to check.</param>
    /// <param name="paramName">The name of the parameter being checked. Captured automatically by the compiler.</param>
    /// <exception cref="ArgumentException"><paramref name="stream"/> doesn't support seeking.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void CanSeek(Stream stream, [CallerArgumentExpression(nameof(stream))] string? paramName = null)
    {
        if (!stream.CanSeek)
        {
            _ThrowForCanSeek(stream, paramName);
        }
    }

    /// <summary>Throws an <see cref="ArgumentException" /> when <paramref name="stream" /> is not at the starting position.</summary>
    /// <param name="stream">The argument <see cref="Stream"/> instance to check.</param>
    /// <param name="paramName">The name of the parameter being checked. Captured automatically by the compiler.</param>
    /// <exception cref="ArgumentException"><paramref name="stream"/> is not at the starting position.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void IsAtStartPosition(
        Stream stream,
        [CallerArgumentExpression(nameof(stream))] string? paramName = null
    )
    {
        if (stream.Position != 0)
        {
            _ThrowForIsAtStartPosition(stream, paramName);
        }
    }

    /// <summary>Throws an <see cref="ArgumentException" /> when the file at <paramref name="path" /> does not exist.</summary>
    /// <param name="path">The file path to check.</param>
    /// <param name="message">A custom error message, or <see langword="null"/> to use the default error message.</param>
    /// <param name="paramName">The name of the parameter being checked. Captured automatically by the compiler.</param>
    /// <returns><paramref name="path" /> when the file exists.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty or the file does not exist.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string FileExists(
        [SystemNotNull] string? path,
        string? message = null,
        [CallerArgumentExpression(nameof(path))] string? paramName = null
    )
    {
        IsNotNullOrEmpty(path, message, paramName);

        if (!File.Exists(path))
        {
            _ThrowForFileExists(path, message, paramName);
        }

        return path;
    }

    /// <summary>Throws an <see cref="ArgumentException" /> when the directory at <paramref name="path" /> does not exist.</summary>
    /// <param name="path">The directory path to check.</param>
    /// <param name="message">A custom error message, or <see langword="null"/> to use the default error message.</param>
    /// <param name="paramName">The name of the parameter being checked. Captured automatically by the compiler.</param>
    /// <returns><paramref name="path" /> when the directory exists.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty or the directory does not exist.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string DirectoryExists(
        [SystemNotNull] string? path,
        string? message = null,
        [CallerArgumentExpression(nameof(path))] string? paramName = null
    )
    {
        IsNotNullOrEmpty(path, message, paramName);

        if (!Directory.Exists(path))
        {
            _ThrowForDirectoryExists(path, message, paramName);
        }

        return path;
    }

    [DoesNotReturn]
    private static void _ThrowForCanRead(Stream stream, string? paramName)
    {
        throw new ArgumentException(
            $"Stream {paramName.ToAssertString()} ({stream.GetType().Name}) doesn't support reading.",
            paramName
        );
    }

    [DoesNotReturn]
    private static void _ThrowForCanWrite(Stream stream, string? paramName)
    {
        throw new ArgumentException(
            $"Stream {paramName.ToAssertString()} ({stream.GetType().Name}) doesn't support writing.",
            paramName
        );
    }

    [DoesNotReturn]
    private static void _ThrowForCanSeek(Stream stream, string? paramName)
    {
        throw new ArgumentException(
            $"Stream {paramName.ToAssertString()} ({stream.GetType().Name}) doesn't support seeking.",
            paramName
        );
    }

    [DoesNotReturn]
    private static void _ThrowForIsAtStartPosition(Stream stream, string? paramName)
    {
        FormattableString format =
            $"The stream argument {paramName.ToAssertString()} of type <{stream.GetType().Name} must be at the starting position. (Actual Position {stream.Position})";

        throw new ArgumentException(format.ToString(CultureInfo.InvariantCulture), paramName);
    }

    [DoesNotReturn]
    private static void _ThrowForFileExists(string? path, string? message, string? paramName)
    {
        throw new ArgumentException(
            message ?? $"The file {paramName.ToAssertString()} at path \"{path}\" does not exist.",
            paramName
        );
    }

    [DoesNotReturn]
    private static void _ThrowForDirectoryExists(string? path, string? message, string? paramName)
    {
        throw new ArgumentException(
            message ?? $"The directory {paramName.ToAssertString()} at path \"{path}\" does not exist.",
            paramName
        );
    }
}
