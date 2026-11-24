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
        // first, each with its position in the list; a span with nothing to redact allocates nothing.
        List<(int Position, string Key, object? Value)>? changes = null;
        var position = 0;

        foreach (ref readonly var tag in data.EnumerateTagObjects())
        {
            var current = position++;

            if (tag.Value is null)
            {
                continue;
            }

            if (names.TryGetRedactor(tag.Key, out var redactor))
            {
                (changes ??= []).Add((current, tag.Key, LogPropertyRedactors.Redact(redactor, tag.Value)));
            }
            else if (maskText && tag.Value is string text)
            {
                var masked = masker!.Mask(text);

                if (!ReferenceEquals(masked, text))
                {
                    (changes ??= []).Add((current, tag.Key, masked));
                }
            }
        }

        if (changes is not null)
        {
            _Rewrite(data, changes);
        }
    }

    /// <summary>Replaces every occurrence of each changed tag's key, keeping their relative order.</summary>
    /// <remarks>
    /// <c>Activity.AddTag</c> allows a key more than once, while <c>SetTag</c> updates, and <c>SetTag(key, null)</c>
    /// removes, only the first occurrence. Setting each change in place would leave a later raw duplicate in the
    /// export, so every occurrence of an affected key is removed and re-added with its sanitized value.
    /// </remarks>
    private static void _Rewrite(Activity data, List<(int Position, string Key, object? Value)> changes)
    {
        // Activity matches keys ordinally, so the affected set must too, even though classified names ignore case.
        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (_, key, _) in changes)
        {
            keys.Add(key);
        }

        var rewritten = new List<KeyValuePair<string, object?>>();
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var next = 0;
        var position = 0;

        foreach (ref readonly var tag in data.EnumerateTagObjects())
        {
            var current = position++;

            if (!keys.Contains(tag.Key))
            {
                continue;
            }

            var value = tag.Value;

            if (next < changes.Count && changes[next].Position == current)
            {
                value = changes[next++].Value;
            }

            rewritten.Add(new(tag.Key, value));
            occurrences[tag.Key] = occurrences.GetValueOrDefault(tag.Key) + 1;
        }

        foreach (var (key, count) in occurrences)
        {
            // Removes by count, not until GetTagItem reads null: an occurrence that holds a null value reads null too.
            for (var i = 0; i < count; i++)
            {
                data.SetTag(key, value: null);
            }
        }

        foreach (var (key, value) in rewritten)
        {
            data.AddTag(key, value);
        }
    }
}
