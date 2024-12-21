// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests.Helpers;

#pragma warning disable MA0096 // A class that implements IComparable<T> should also implement IEquatable<T>
internal sealed record InputsTestArgument : IComparable, IComparable<InputsTestArgument>
{
    public int IntValue { get; set; } = 5;

    public double DoubleValue { get; set; } = 7.2d;

    public float FloatValue { get; set; } = 10.5f;

    public decimal DecimalValue { get; set; } = 25.5m;

    public TimeSpan TimeSpanValue { get; set; } = TimeSpan.Parse("00:00:10", CultureInfo.InvariantCulture);

    public int CompareTo(object? obj)
    {
        if (obj is InputsTestArgument other)
        {
            return CompareTo(other);
        }

        return 0;
    }

    public int CompareTo(InputsTestArgument? other)
    {
        if (other == null)
        {
            return 1;
        }

        var intComparison = IntValue.CompareTo(other.IntValue);

        if (intComparison != 0)
        {
            return intComparison;
        }

        var doubleComparison = DoubleValue.CompareTo(other.DoubleValue);

        if (doubleComparison != 0)
        {
            return doubleComparison;
        }

        var floatComparison = FloatValue.CompareTo(other.FloatValue);

        if (floatComparison != 0)
        {
            return floatComparison;
        }

        var decimalComparison = DecimalValue.CompareTo(other.DecimalValue);

        if (decimalComparison != 0)
        {
            return decimalComparison;
        }

        var timeSpanComparison = TimeSpanValue.CompareTo(other.TimeSpanValue);

        return timeSpanComparison;
    }
}
