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
