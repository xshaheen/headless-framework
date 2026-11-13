// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using OpenTelemetry;

namespace Headless.Logging.Internal;

/// <summary>
/// Masks the text a span carries about a failure, its status description and its exception tags, with the app's text
/// redactors when the span ends.
/// </summary>
/// <remarks>
/// <para>
/// Instrumentation that records a failure as plain tags, as the jobs scheduler does, and code that passes an exception
/// message as the status description, would otherwise export the message unmasked. Only those fields are scanned, so a
/// span without a failure costs three tag lookups.
/// </para>
/// <para>
/// An exception event (<c>Activity.AddException</c>) cannot be rewritten once recorded: its tags are read-only. The
/// framework records its own events with the masked exception instead.
/// </para>
/// <para>Must run before the export processors, so it is added before any exporter.</para>
/// </remarks>
internal sealed class TextRedactionActivityProcessor(LogTextMasker? masker) : BaseProcessor<Activity>
{
    // The OpenTelemetry semantic-convention keys for an exception recorded as span tags.
    private static readonly string[] _ExceptionTags = ["exception.message", "exception.stacktrace"];

    public override void OnEnd(Activity data)
    {
        if (masker is not { IsEnabled: true })
        {
            return;
        }

        if (data.StatusDescription is { } description)
        {
            var masked = masker.Mask(description);

            if (!ReferenceEquals(masked, description))
            {
                data.SetStatus(data.Status, masked);
            }
        }

        foreach (var tag in _ExceptionTags)
        {
            if (data.GetTagItem(tag) is string value)
            {
                var masked = masker.Mask(value);

                if (!ReferenceEquals(masked, value))
                {
                    data.SetTag(tag, masked);
                }
            }
        }
    }
}
