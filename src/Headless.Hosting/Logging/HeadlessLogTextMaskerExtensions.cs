// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Checks;

namespace Headless.Hosting;

/// <summary>Records span exception events through an <see cref="ILogTextMasker"/>.</summary>
[PublicAPI]
public static class HeadlessLogTextMaskerExtensions
{
    extension(ILogTextMasker masker)
    {
        /// <summary>
        /// Records <paramref name="exception"/> on <paramref name="activity"/> as an exception event whose
        /// <c>exception.message</c> and <c>exception.stacktrace</c> are the exception's message and rendered text,
        /// masked.
        /// </summary>
        /// <remarks>
        /// Use it in place of <see cref="Activity.AddException"/>: an exception event's tags cannot be rewritten once it
        /// is recorded, so a span processor cannot mask it afterwards. The rendered text is masked, not only the
        /// messages, so a value that appears only in a custom <see cref="Exception.ToString"/> or a stack frame does not
        /// reach the span. <c>exception.type</c> names the original type. An
        /// <see cref="ActivityListener.ExceptionRecorder"/> receives the masked exception.
        /// </remarks>
        /// <param name="activity">The span to record the event on.</param>
        /// <param name="exception">The exception to record.</param>
        /// <returns><paramref name="activity"/>, for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="activity"/> or <paramref name="exception"/> is <see langword="null"/>.
        /// </exception>
        public Activity RecordException(Activity activity, Exception exception)
        {
            Argument.IsNotNull(activity);
            Argument.IsNotNull(exception);

            // AddException keeps a tag the caller already set and fills only the missing ones from the exception it is
            // given, so these replace the unmasked message, ToString() text, and type it would otherwise write.
            var tags = new TagList
            {
                { "exception.message", masker.Mask(exception.Message) },
                { "exception.stacktrace", masker.Mask(exception.ToString()) },
                { "exception.type", exception.GetType().ToString() },
            };

            return activity.AddException(masker.Mask(exception), tags);
        }
    }
}
