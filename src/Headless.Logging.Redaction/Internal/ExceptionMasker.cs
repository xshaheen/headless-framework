// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Logging.Internal;

/// <summary>Masks the messages of an exception and its inner exceptions for every channel that records exceptions.</summary>
/// <remarks>
/// Only the messages are scanned: rendering <see cref="Exception.ToString"/> would format every stack trace on each
/// recorded exception, which the provider then repeats. A match yields a copy of the original type, so error trackers
/// keep its type and stack frames; <see cref="RedactedException"/> is the fallback when the runtime cannot be copied or
/// the copy's message would still match.
/// </remarks>
internal static class ExceptionMasker
{
    /// <summary>
    /// Returns <paramref name="exception"/> itself, with no allocation, when no message in the chain matched; otherwise a
    /// masked copy, or a <see cref="RedactedException"/>.
    /// </summary>
    public static Exception Mask<TState>(Exception exception, Func<string, TState, string> mask, TState state)
    {
        var message = exception.Message;
        var maskedMessage = mask(message, state);
        var changed = !string.Equals(maskedMessage, message, StringComparison.Ordinal);
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
            ExceptionCopier.TryGetOwnMessage(exception, out var ownMessage)
            && ExceptionCopier.TryCopy(
                exception,
                ownMessage is null ? null : mask(ownMessage, state),
                inners,
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

        return new RedactedException(exception, maskedMessage, inners, text => mask(text, state));
    }
}
