// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;

namespace Headless.Logging.Internal;

/// <summary>
/// Copies an exception and rewrites the copy's own message and inner exceptions, so providers receive the original type,
/// captured stack frames, target site, and data with the masked text.
/// </summary>
/// <remarks>
/// It reaches the runtime's private fields, which have kept their names across .NET releases but are not a contract. A
/// runtime that renames one makes the first copy throw; copying is then switched off for the process, and callers fall
/// back to <see cref="RedactedException"/>.
/// </remarks>
internal static class ExceptionCopier
{
    private static volatile bool _unavailable;

    /// <summary>The message the exception was constructed with, before a subclass decorates it; <see langword="null"/> when it uses its type's default.</summary>
    public static bool TryGetOwnMessage(Exception exception, out string? message)
    {
        message = null;

        if (_unavailable)
        {
            return false;
        }

        try
        {
            message = _Message(exception);

            return true;
        }
        catch (MissingFieldException)
        {
            _unavailable = true;

            return false;
        }
    }

    /// <summary>
    /// Copies <paramref name="exception"/> with <paramref name="message"/> as its own message and
    /// <paramref name="inners"/> as its inner exceptions (every member of an <see cref="AggregateException"/>, otherwise
    /// one or none). Returns <see langword="false"/>, and no copy, when the runtime's fields are not where expected.
    /// </summary>
    public static bool TryCopy(Exception exception, string? message, Exception[] inners, out Exception copy)
    {
        copy = exception;

        if (_unavailable)
        {
            return false;
        }

        try
        {
            var clone = (Exception)_MemberwiseClone(exception);

            _Message(clone) = message;
            _InnerException(clone) = inners.Length > 0 ? inners[0] : null;

            if (clone is AggregateException aggregate)
            {
                _AggregateInners(aggregate) = inners;
                // The read-only view caches the original's array; dropping it makes InnerExceptions read the new one.
                _AggregateView(aggregate) = null;
            }

            copy = clone;

            return true;
        }
        catch (Exception e) when (e is MissingFieldException or MissingMethodException)
        {
            _unavailable = true;

            return false;
        }
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "MemberwiseClone")]
    private static extern object _MemberwiseClone(object instance);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_message")]
    private static extern ref string? _Message(Exception exception);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_innerException")]
    private static extern ref Exception? _InnerException(Exception exception);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_innerExceptions")]
    private static extern ref Exception[] _AggregateInners(AggregateException exception);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_rocView")]
    private static extern ref System.Collections.ObjectModel.ReadOnlyCollection<Exception>? _AggregateView(
        AggregateException exception
    );
}
