// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Headless.Checks.Internals;

namespace Headless.Checks;

public static partial class Argument
{
    /// <summary>Throws an <see cref="ArgumentOutOfRangeException" /> if <paramref name="stream" /> is not support reading.</summary>
    /// <param name="stream">The argument <see cref="Stream"/> instance to check.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <exception cref="ArgumentException">Thrown if <paramref name="stream"/> doesn't support reading.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void CanRead(Stream stream, [CallerArgumentExpression(nameof(stream))] string? paramName = null)
    {
        if (stream.CanRead)
        {
            return;
        }

        throw new ArgumentException(
            $"Stream {paramName.ToAssertString()} ({stream.GetType().Name}) doesn't support reading.",
            paramName
        );
    }

    /// <summary>Throws an <see cref="ArgumentOutOfRangeException" /> if <paramref name="stream" /> is not support writing.</summary>
    /// <param name="stream">The argument <see cref="Stream"/> instance to check.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <exception cref="ArgumentException">Thrown if <paramref name="stream"/> doesn't support writing.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void CanWrite(Stream stream, [CallerArgumentExpression(nameof(stream))] string? paramName = null)
    {
        if (stream.CanWrite)
        {
            return;
        }

        throw new ArgumentException(
            $"Stream {paramName.ToAssertString()} ({stream.GetType().Name}) doesn't support writing.",
            paramName
        );
    }

    /// <summary>Throws an <see cref="ArgumentOutOfRangeException" /> if <paramref name="stream" /> is not support seeking.</summary>
    /// <param name="stream">The argument <see cref="Stream"/> instance to check.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <exception cref="ArgumentException">Thrown if <paramref name="stream"/> doesn't support seeking.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void CanSeek(Stream stream, [CallerArgumentExpression(nameof(stream))] string? paramName = null)
    {
        if (stream.CanSeek)
        {
            return;
        }

        throw new ArgumentException(
            $"Stream {paramName.ToAssertString()} ({stream.GetType().Name}) doesn't support reading.",
            paramName
        );
    }

    /// <summary>Throws an <see cref="ArgumentOutOfRangeException" /> if <paramref name="stream" /> is not at the starting position.</summary>
    /// <param name="stream">The argument <see cref="Stream"/> instance to check.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <exception cref="ArgumentException">Thrown if <paramref name="stream"/> is not at the starting position.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void IsAtStartPosition(
        Stream stream,
        [CallerArgumentExpression(nameof(stream))] string? paramName = null
    )
    {
        if (stream.Position == 0)
        {
            return;
        }

        FormattableString format =
            $"The stream argument {paramName.ToAssertString()} of type <{stream.GetType().Name} must be at the starting position. (Actual Position {stream.Position})";

        throw new ArgumentException(format.ToString(CultureInfo.InvariantCulture), paramName);
    }

    /// <summary>Throws an <see cref="ArgumentException" /> if the file at <paramref name="path" /> does not exist.</summary>
    /// <param name="path">The file path to check.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="path" /> if the file exists.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="path"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown if <paramref name="path"/> is empty or the file does not exist.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string FileExists(
        [SystemNotNull] string? path,
        string? message = null,
        [CallerArgumentExpression(nameof(path))] string? paramName = null
    )
    {
        IsNotNullOrEmpty(path, message, paramName);

        if (File.Exists(path))
        {
            return path;
        }

        throw new ArgumentException(
            message ?? $"The file {paramName.ToAssertString()} at path \"{path}\" does not exist.",
            paramName
        );
    }

    /// <summary>Throws an <see cref="ArgumentException" /> if the directory at <paramref name="path" /> does not exist.</summary>
    /// <param name="path">The directory path to check.</param>
    /// <param name="message">(Optional) Custom error message.</param>
    /// <param name="paramName">Parameter name (auto generated no need to pass it).</param>
    /// <returns><paramref name="path" /> if the directory exists.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="path"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown if <paramref name="path"/> is empty or the directory does not exist.</exception>
    [DebuggerStepThrough]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string DirectoryExists(
        [SystemNotNull] string? path,
        string? message = null,
        [CallerArgumentExpression(nameof(path))] string? paramName = null
    )
    {
        IsNotNullOrEmpty(path, message, paramName);

        if (Directory.Exists(path))
        {
            return path;
        }

        throw new ArgumentException(
            message ?? $"The directory {paramName.ToAssertString()} at path \"{path}\" does not exist.",
            paramName
        );
    }
}
