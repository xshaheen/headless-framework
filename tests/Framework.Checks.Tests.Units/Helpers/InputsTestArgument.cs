// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests.Helpers;

internal sealed class InputsTestArgument : IComparable, IComparable<InputsTestArgument>
{
    public int IntValue { get; set; } = 5;

    public double DoubleValue { get; set; } = 7.2d;

    public float FloatValue { get; set; } = 10.5f;

    public decimal DecimalValue { get; set; } = 25.5m;

    public TimeSpan TimeSpanValue { get; set; } = TimeSpan.Parse("00:00:10");

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
            return 1;

        int intComparison = IntValue.CompareTo(other.IntValue);

        if (intComparison != 0)
            return intComparison;

        int doubleComparison = DoubleValue.CompareTo(other.DoubleValue);

        if (doubleComparison != 0)
            return doubleComparison;

        int floatComparison = FloatValue.CompareTo(other.FloatValue);

        if (floatComparison != 0)
            return floatComparison;

        int decimalComparison = DecimalValue.CompareTo(other.DecimalValue);

        if (decimalComparison != 0)
            return decimalComparison;

        int timeSpanComparison = TimeSpanValue.CompareTo(other.TimeSpanValue);

        return timeSpanComparison;
    }
}
