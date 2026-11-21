// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Reflection;

namespace Headless.Logging.Internal;

/// <summary>
/// Masks the messages and <see cref="Exception.Data"/> values of an exception and its inner exceptions for every channel
/// that records exceptions.
/// </summary>
/// <remarks>
/// The messages and data values are scanned, not <see cref="Exception.ToString"/>: rendering it would format every stack
/// trace on each recorded exception, which the provider then repeats. A match yields a copy of the original type, so
/// error trackers keep its type and stack frames; <see cref="RedactedException"/> is the fallback when the runtime
/// cannot be copied or the copy's message would still match.
/// </remarks>
internal static class ExceptionMasker
{
    private static readonly ConcurrentDictionary<Type, bool> _OverridesDataByType = new();

    /// <summary>
    /// Returns <paramref name="exception"/> itself when no message or data value in the chain
    /// matched; otherwise a masked copy, or a <see cref="RedactedException"/>.
    /// </summary>
    public static Exception Mask<TState>(Exception exception, Func<string, TState, string> mask, TState state)
    {
        var message = exception.Message;
        var maskedMessage = mask(message, state);
        var maskedData = _MaskData(exception, mask, state);
        var changed = maskedData is not null || !string.Equals(maskedMessage, message, StringComparison.Ordinal);
        Exception[]? inners = null;

        if (exception is AggregateException aggregate)
        {
            var members = aggregate.InnerExceptions;

            for (var i = 0; i < members.Count; i++)
            {
                var masked = Mask(members[i], mask, state);

                if (!ReferenceEquals(masked, members[i]))
                {
                    // Copied only once a member changed, so an aggregate with no match costs no array.
                    inners ??= [.. members];
                    inners[i] = masked;
                }
            }
        }
        else if (exception.InnerException is { } innerException)
        {
            var masked = Mask(innerException, mask, state);

            if (!ReferenceEquals(masked, innerException))
            {
                inners = [masked];
            }
        }

        if (!changed && inners is null)
        {
            return exception;
        }

        inners ??= exception switch
        {
            AggregateException unchanged => [.. unchanged.InnerExceptions],
            { InnerException: { } unchanged } => [unchanged],
            _ => [],
        };

        if (
            // A copy of a type that overrides Data would still return the original's entries, so it takes the fallback.
            (maskedData is null || !_OverridesDataByType.GetOrAdd(exception.GetType(), _OverridesData))
            && ExceptionCopier.TryGetOwnMessage(exception, out var ownMessage)
            && ExceptionCopier.TryCopy(
                exception,
                ownMessage is null ? null : mask(ownMessage, state),
                inners,
                maskedData,
                out var copy
            )
        )
        {
            // A subclass can build Message from fields of its own, which the copy does not rewrite, so the copy is
            // used only when its message no longer matches.
            var copiedMessage = copy.Message;

            if (string.Equals(mask(copiedMessage, state), copiedMessage, StringComparison.Ordinal))
            {
                return copy;
            }
        }

        return new RedactedException(exception, maskedMessage, inners, maskedData, text => mask(text, state));
    }

    /// <summary>
    /// Returns a new dictionary holding every entry of the exception's <see cref="Exception.Data"/> with its value masked the way log property
    /// values are, or <see langword="null"/> when no value matched. Keys are kept as they are.
    /// </summary>
    private static ListDictionary? _MaskData<TState>(
        Exception exception,
        Func<string, TState, string> mask,
        TState state
    )
    {
        // Reading Exception.Data creates the container the first time, and most exceptions never get one, so the field
        // is checked first. A type that overrides Data may keep its entries elsewhere, so it is always read.
        if (
            !_OverridesDataByType.GetOrAdd(exception.GetType(), _OverridesData)
            && ExceptionCopier.TryGetOwnData(exception, out var own)
            && own is null
        )
        {
            return null;
        }

        var data = exception.Data;

        if (data.Count == 0)
        {
            return null;
        }

        var entries = new DictionaryEntry[data.Count];
        data.CopyTo(entries, 0);
        ListDictionary? masked = null;

        for (var i = 0; i < entries.Length; i++)
        {
            var value = ValueMasker.Mask(entries[i].Value, mask, state);

            if (masked is null)
            {
                if (ReferenceEquals(value, entries[i].Value))
                {
                    continue;
                }

                // Built only once a value changed, keeping the original's order for the entries before it.
                masked = [];

                for (var j = 0; j < i; j++)
                {
                    masked.Add(entries[j].Key, entries[j].Value);
                }
            }

            masked.Add(entries[i].Key, value);
        }

        return masked;
    }

    private static bool _OverridesData(Type type) =>
        type.GetProperty(
            nameof(Exception.Data),
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            typeof(IDictionary),
            Type.EmptyTypes,
            modifiers: null
        )?.GetMethod?.DeclaringType != typeof(Exception);
}
