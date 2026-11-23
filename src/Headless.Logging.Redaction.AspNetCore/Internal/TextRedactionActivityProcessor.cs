// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using OpenTelemetry;

namespace Headless.Logging.Internal;

/// <summary>
/// Redacts what a span carries when it ends: each tag whose name the app classified, and the text of every string tag
/// and the status description through the app's text redactors.
/// </summary>
/// <remarks>
/// <para>
/// Instrumentation and app code set tags directly on the span, so a value never passes through the redacting logger:
/// an <c>enduser.email</c> tag, or a failure the jobs scheduler records as <c>exception.message</c>, would otherwise be
/// exported raw. A classified name is redacted whatever its value's type; text patterns apply only to string values,
/// so a numeric tag such as a status code keeps its type.
/// </para>
/// <para>
/// An exception event (<c>Activity.AddException</c>) cannot be rewritten once recorded: its tags are read-only. The
/// framework records its own events with the masked exception instead.
/// </para>
/// <para>Must run before the export processors, so it is added before any exporter.</para>
/// </remarks>
internal sealed class TextRedactionActivityProcessor(LogTextMasker? masker, LogPropertyRedactors names)
    : BaseProcessor<Activity>
{
    public override void OnEnd(Activity data)
    {
        var maskText = masker is { IsEnabled: true };

        if (!maskText && names.Count == 0)
        {
            return;
        }

        if (maskText && data.StatusDescription is { } description)
        {
            var masked = masker!.Mask(description);

            if (!ReferenceEquals(masked, description))
            {
                data.SetStatus(data.Status, masked);
            }
        }

        // Setting a tag while enumerating the span's tags would change the list being read, so changes are collected
        // first; a span with nothing to redact allocates nothing.
        List<KeyValuePair<string, object?>>? changes = null;

        foreach (ref readonly var tag in data.EnumerateTagObjects())
        {
            if (tag.Value is null)
            {
                continue;
            }

            if (names.TryGetRedactor(tag.Key, out var redactor))
            {
                (changes ??= []).Add(new(tag.Key, LogPropertyRedactors.Redact(redactor, tag.Value)));
            }
            else if (maskText && tag.Value is string text)
            {
                var masked = masker!.Mask(text);

                if (!ReferenceEquals(masked, text))
                {
                    (changes ??= []).Add(new(tag.Key, masked));
                }
            }
        }

        if (changes is null)
        {
            return;
        }

        foreach (var (key, value) in changes)
        {
            data.SetTag(key, value);
        }
    }
}
