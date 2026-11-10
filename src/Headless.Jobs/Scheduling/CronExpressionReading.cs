// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;

namespace Headless.Jobs;

/// <summary>
/// Reads a six-field cron expression (seconds first) as plain English, such as <c>At 09:00, Monday through Friday</c>
/// for <c>0 0 9 * * 1-5</c>.
/// </summary>
/// <remarks>
/// The reading describes the expression only; the caller states the zone it fires in. It assumes an expression that
/// already parsed, and falls back to listing each field for shapes it has no shorter wording for.
/// </remarks>
internal static class CronExpressionReading
{
    private static readonly string[] _Days =
    [
        "Sunday",
        "Monday",
        "Tuesday",
        "Wednesday",
        "Thursday",
        "Friday",
        "Saturday",
        "Sunday",
    ];

    private static readonly string[] _Months =
    [
        "",
        "January",
        "February",
        "March",
        "April",
        "May",
        "June",
        "July",
        "August",
        "September",
        "October",
        "November",
        "December",
    ];

    public static string Describe(string expression)
    {
        var fields = expression.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (fields.Length != 6)
        {
            return expression;
        }

        var parts = new List<string> { _Time(fields[0], fields[1], fields[2]) };
        var (dayOfMonth, month, dayOfWeek) = (fields[3], fields[4], fields[5]);

        if (!_IsAny(dayOfMonth))
        {
            parts.Add(_DayOfMonth(dayOfMonth));
        }

        if (!_IsAny(month))
        {
            parts.Add(
                month.Contains('/', StringComparison.Ordinal)
                    ? _Numeric(month, "month", "months")
                    : "in " + _Named(month, _Months)
            );
        }

        if (!_IsAny(dayOfWeek))
        {
            parts.Add(
                dayOfWeek.Contains('/', StringComparison.Ordinal)
                    ? _Numeric(dayOfWeek, "day of the week", "days of the week")
                : _IsRange(dayOfWeek) ? _Named(dayOfWeek, _Days)
                : "on " + _Named(dayOfWeek, _Days)
            );
        }
        else if (
            _IsAny(dayOfMonth)
            && _IsAny(month)
            && _IsNumber(fields[0], out _)
            && _IsNumber(fields[1], out _)
            && _IsNumber(fields[2], out _)
        )
        {
            // Only a fixed time of day needs the day stated; "every 15 minutes" already says it.

            parts.Add("every day");
        }

        return string.Join(", ", parts);
    }

    private static string _Time(string second, string minute, string hour)
    {
        if (_IsNumber(second, out var s) && _IsNumber(minute, out var m) && _IsNumber(hour, out var h))
        {
            var time = string.Create(CultureInfo.InvariantCulture, $"{h:00}:{m:00}");

            return s == 0 ? "At " + time : string.Create(CultureInfo.InvariantCulture, $"At {time}:{s:00}");
        }

        if (_IsAny(hour) && _IsAny(minute))
        {
            return _Capitalize(_Numeric(second, "second", "seconds"));
        }

        if (_IsAny(hour) && string.Equals(second, "0", StringComparison.Ordinal))
        {
            return _IsNumber(minute, out var onlyMinute)
                ? string.Create(CultureInfo.InvariantCulture, $"At minute {onlyMinute} of every hour")
                : _Capitalize(_Numeric(minute, "minute", "minutes"));
        }

        var parts = new List<string>();

        if (!string.Equals(second, "0", StringComparison.Ordinal))
        {
            parts.Add(_Numeric(second, "second", "seconds"));
        }

        parts.Add(_Numeric(minute, "minute", "minutes"));
        parts.Add(_Numeric(hour, "hour", "hours"));

        return _Capitalize(string.Join(", ", parts));
    }

    private static string _DayOfMonth(string field)
    {
        if (field.Contains('/', StringComparison.Ordinal))
        {
            return _Numeric(field, "day of the month", "days of the month");
        }

        return field.Contains(',', StringComparison.Ordinal) || _IsRange(field)
            ? "on days " + _Named(field, names: null) + " of the month"
            : "on day " + field + " of the month";
    }

    // A numeric field: "every minute", "every 15 minutes", "every 15 minutes from 5", "minutes 0, 30",
    // "minutes 9 through 17", or "at minute 5".
    private static string _Numeric(string field, string unit, string units)
    {
        if (_IsAny(field))
        {
            return "every " + unit;
        }

        var slash = field.IndexOf('/', StringComparison.Ordinal);

        if (slash >= 0)
        {
            var start = field[..slash];
            var step = "every " + field[(slash + 1)..] + " " + units;

            return _IsAny(start) ? step : step + " from " + _Named(start, names: null);
        }

        if (field.Contains(',', StringComparison.Ordinal) || _IsRange(field))
        {
            return units + " " + _Named(field, names: null);
        }

        return "at " + unit + " " + field;
    }

    // A list or range of values, named when the field has names: "January, July", "Monday through Friday".
    private static string _Named(string field, string[]? names)
    {
        return string.Join(
            ", ",
            field
                .Split(',')
                .Select(item =>
                {
                    var range = item.Split('-');

                    return range.Length == 2
                        ? _Name(range[0], names) + " through " + _Name(range[1], names)
                        : _Name(item, names);
                })
        );
    }

    private static string _Name(string value, string[]? names)
    {
        return
            names is not null
            && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
            && index < names.Length
            && names[index].Length != 0
            ? names[index]
            : value;
    }

    private static bool _IsRange(string field) =>
        !field.Contains(',', StringComparison.Ordinal) && field.Contains('-', StringComparison.Ordinal);

    private static bool _IsAny(string field) => field is "*" or "?";

    private static bool _IsNumber(string field, out int value)
    {
        return int.TryParse(field, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static string _Capitalize(string text)
    {
        return text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
    }
}
