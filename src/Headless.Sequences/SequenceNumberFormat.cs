// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;

namespace Headless.Sequences;

/// <summary>A parsed document-number template, and the partition and date rules a policy applies to a number.</summary>
internal sealed class SequenceNumberFormat
{
    private static readonly ConcurrentDictionary<string, SequenceNumberFormat> _Cache = new(StringComparer.Ordinal);

    private readonly IReadOnlyList<Segment> _segments;

    private SequenceNumberFormat(IReadOnlyList<Segment> segments)
    {
        _segments = segments;
    }

    /// <summary>Returns the parsed template, parsing it once per distinct text.</summary>
    /// <exception cref="FormatException">The template has an unknown token or an unbalanced brace.</exception>
    public static SequenceNumberFormat Get(string template)
    {
        return _Cache.GetOrAdd(template, static text => new SequenceNumberFormat(_Parse(text)));
    }

    /// <summary>Reports whether <paramref name="template" /> parses, and why not.</summary>
    public static bool TryValidate(string template, out string error)
    {
        try
        {
            _ = _Parse(template);
            error = string.Empty;

            return true;
        }
        catch (FormatException e)
        {
            error = e.Message;

            return false;
        }
    }

    /// <summary>
    /// Reports whether <paramref name="template" /> parses and names every number it issues under
    /// <paramref name="reset" /> once, and why not.
    /// </summary>
    /// <remarks>
    /// A counter restarts in each partition, so the text must carry what tells partitions apart: exactly one
    /// <c>{seq}</c>, and the date tokens of the reset period. Without them, January's <c>INV-2026-0001</c> and
    /// February's <c>INV-2026-0001</c> are the same number. A calendar-year reset needs <c>{yyyy}</c> or <c>{yy}</c>,
    /// because <c>{fy}</c> repeats across a calendar-year boundary inside one fiscal year.
    /// </remarks>
    public static bool TryValidate(string template, SequenceReset reset, out string error)
    {
        List<Segment> segments;

        try
        {
            segments = _Parse(template);
        }
        catch (FormatException e)
        {
            error = e.Message;

            return false;
        }

        if (segments.Where(static segment => segment.Kind == SegmentKind.Sequence).Take(2).Count() != 1)
        {
            error = $"The sequence number template '{template}' must contain exactly one {{seq}} token.";

            return false;
        }

        var hasYear = segments.Exists(static s => s is { Kind: SegmentKind.Date, Text: "yyyy" or "yy" });
        var hasMonth = segments.Exists(static s => s is { Kind: SegmentKind.Date, Text: "MM" });
        var hasDay = segments.Exists(static s => s is { Kind: SegmentKind.Date, Text: "dd" });
        var hasFiscalYear = segments.Exists(static s => s.Kind == SegmentKind.FiscalYear);

        var (identified, required) = reset switch
        {
            SequenceReset.Year => (hasYear, "{yyyy} or {yy}"),
            SequenceReset.Month => (hasYear && hasMonth, "{yyyy} or {yy}, and {MM}"),
            SequenceReset.Day => (hasYear && hasMonth && hasDay, "{yyyy} or {yy}, {MM}, and {dd}"),
            SequenceReset.FiscalYear => (hasFiscalYear, "{fy}"),
            _ => (true, ""),
        };

        if (!identified)
        {
            error =
                $"The sequence number template '{template}' resets every {reset} but does not contain {required}, "
                + "so two partitions would issue the same number.";

            return false;
        }

        error = string.Empty;

        return true;
    }

    /// <summary>The counter partition the policy's reset chooses for <paramref name="issuedOn" />.</summary>
    public static string? Partition(SequencePolicy policy, DateOnly issuedOn)
    {
        return policy.Reset switch
        {
            SequenceReset.Never => null,
            SequenceReset.Year => issuedOn.ToString("yyyy", CultureInfo.InvariantCulture),
            SequenceReset.Month => issuedOn.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            SequenceReset.Day => issuedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            SequenceReset.FiscalYear => "FY" + FiscalYear(policy, issuedOn).ToString(CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException($"Unknown sequence reset '{policy.Reset}'."),
        };
    }

    /// <summary>The year the fiscal year containing <paramref name="date" /> starts in.</summary>
    public static int FiscalYear(SequencePolicy policy, DateOnly date)
    {
        return date.Month >= policy.FiscalYearStartMonth ? date.Year : date.Year - 1;
    }

    /// <summary>The issue date: <paramref name="now" /> in the policy's time zone.</summary>
    public static DateOnly IssuedOn(SequencePolicy policy, DateTimeOffset now)
    {
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, policy.TimeZone).DateTime);
    }

    /// <summary>The first instant after <paramref name="now" /> in the policy's next reset period, in UTC.</summary>
    public static DateTimeOffset? NextResetAt(SequencePolicy policy, DateTimeOffset now)
    {
        var issuedOn = IssuedOn(policy, now);

        DateOnly? next = policy.Reset switch
        {
            SequenceReset.Never => null,
            SequenceReset.Day => issuedOn.AddDays(1),
            SequenceReset.Month => new DateOnly(issuedOn.Year, issuedOn.Month, 1).AddMonths(1),
            SequenceReset.Year => new DateOnly(issuedOn.Year + 1, 1, 1),
            SequenceReset.FiscalYear => new DateOnly(FiscalYear(policy, issuedOn) + 1, policy.FiscalYearStartMonth, 1),
            _ => throw new InvalidOperationException($"Unknown sequence reset '{policy.Reset}'."),
        };

        return next is { } date ? _StartOf(date, policy.TimeZone) : null;
    }

    /// <summary>The first instant whose date in <paramref name="zone" /> is <paramref name="date" /> or later, in UTC.</summary>
    private static DateTimeOffset _StartOf(DateOnly date, TimeZoneInfo zone)
    {
        // Unspecified kind: the zone APIs then read the value as local time in the zone, never the machine's zone.
        var midnight = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

        if (zone.IsAmbiguousTime(midnight))
        {
            // The larger offset is the first pass through the repeated hour.
            return new DateTimeOffset(midnight, zone.GetAmbiguousTimeOffsets(midnight).Max()).ToUniversalTime();
        }

        if (!zone.IsInvalidTime(midnight))
        {
            return new DateTimeOffset(midnight, zone.GetUtcOffset(midnight)).ToUniversalTime();
        }

        // A daylight-saving jump skips midnight: find the transition instant, the first one whose local date is the
        // new date. Every offset lies within ±14 hours, so local midnight's instant lies inside this window.
        var low = new DateTimeOffset(midnight.AddHours(-15).Ticks, TimeSpan.Zero);
        var high = new DateTimeOffset(midnight.AddHours(15).Ticks, TimeSpan.Zero);

        while (high.Ticks - low.Ticks > 1)
        {
            var middle = new DateTimeOffset(low.Ticks + ((high.Ticks - low.Ticks) / 2), TimeSpan.Zero);

            if (DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(middle, zone).DateTime) >= date)
            {
                high = middle;
            }
            else
            {
                low = middle;
            }
        }

        return high;
    }

    public string Format(SequencePolicy policy, long value, DateOnly issuedOn)
    {
        var builder = new StringBuilder();

        foreach (var segment in _segments)
        {
            switch (segment.Kind)
            {
                case SegmentKind.Literal:
                    builder.Append(segment.Text);
                    break;
                case SegmentKind.Sequence:
                    builder.Append(
                        segment.Width is { } width
                            ? value.ToString(
                                "D" + width.ToString(CultureInfo.InvariantCulture),
                                CultureInfo.InvariantCulture
                            )
                            : value.ToString(CultureInfo.InvariantCulture)
                    );
                    break;
                case SegmentKind.FiscalYear:
                    builder.Append(FiscalYear(policy, issuedOn).ToString("D4", CultureInfo.InvariantCulture));
                    break;
                default:
                    builder.Append(issuedOn.ToString(segment.Text, CultureInfo.InvariantCulture));
                    break;
            }
        }

        return builder.ToString();
    }

    private static List<Segment> _Parse(string template)
    {
        var segments = new List<Segment>();
        var literal = new StringBuilder();
        var i = 0;

        while (i < template.Length)
        {
            var c = template[i];

            if (c == '{' && i + 1 < template.Length && template[i + 1] == '{')
            {
                literal.Append('{');
                i += 2;
                continue;
            }

            if (c == '}' && i + 1 < template.Length && template[i + 1] == '}')
            {
                literal.Append('}');
                i += 2;
                continue;
            }

            if (c == '}')
            {
                throw new FormatException(
                    $"The sequence number template '{template}' has an unmatched '}}' at position {i.ToString(CultureInfo.InvariantCulture)}; write '}}}}' for a literal brace."
                );
            }

            if (c != '{')
            {
                literal.Append(c);
                i++;
                continue;
            }

            var end = template.IndexOf('}', i + 1);

            if (end < 0)
            {
                throw new FormatException(
                    $"The sequence number template '{template}' has an unclosed '{{' at position {i.ToString(CultureInfo.InvariantCulture)}."
                );
            }

            if (literal.Length > 0)
            {
                segments.Add(new Segment(SegmentKind.Literal, literal.ToString(), Width: null));
                literal.Clear();
            }

            segments.Add(_Token(template, template[(i + 1)..end]));
            i = end + 1;
        }

        if (literal.Length > 0)
        {
            segments.Add(new Segment(SegmentKind.Literal, literal.ToString(), Width: null));
        }

        return segments;
    }

    private static Segment _Token(string template, string token)
    {
        switch (token)
        {
            case "seq":
                return new Segment(SegmentKind.Sequence, token, Width: null);
            case "yyyy" or "yy" or "MM" or "dd":
                return new Segment(SegmentKind.Date, token, Width: null);
            case "fy":
                return new Segment(SegmentKind.FiscalYear, token, Width: null);
        }

        if (
            token.StartsWith("seq:D", StringComparison.Ordinal)
            && int.TryParse(token.AsSpan(5), NumberStyles.None, CultureInfo.InvariantCulture, out var width)
            && width is >= 1 and <= 19
        )
        {
            return new Segment(SegmentKind.Sequence, token, width);
        }

        throw new FormatException(
            $"The sequence number template '{template}' has an unknown token '{{{token}}}'. Use {{seq}}, {{seq:D<1-19>}}, "
                + "{yyyy}, {yy}, {MM}, {dd}, or {fy}."
        );
    }

    private enum SegmentKind
    {
        Literal,
        Sequence,
        Date,
        FiscalYear,
    }

    private sealed record Segment(SegmentKind Kind, string Text, int? Width);
}
