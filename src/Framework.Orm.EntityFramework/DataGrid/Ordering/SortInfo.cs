namespace Framework.Orm.EntityFramework.DataGrid.Ordering;

/// <summary>
/// Specifies the direction in which to sort a list of items.
/// </summary>
public enum SortDirection
{
    /// <summary>
    /// Sort from smallest to largest. For example, from A to Z.
    /// </summary>
    Ascending = 0,

    /// <summary>
    /// Sort from largest to smallest. For example, from Z to A.
    /// </summary>
    Descending = 1,
}

public sealed class SortInfo : IEquatable<SortInfo>
{
    private static readonly char[] _Separator = [';'];
    private static readonly char[] _PartsSeparator = [':', '-'];

    public required string SortColumn { get; init; }

    public SortDirection SortDirection { get; init; }

    public static IEnumerable<SortInfo> Parse(string sortExpr)
    {
        var retVal = new List<SortInfo>();

        if (string.IsNullOrEmpty(sortExpr))
        {
            return retVal;
        }

        var sortInfoStrings = sortExpr.Split(_Separator, StringSplitOptions.RemoveEmptyEntries);

        foreach (var sortInfoString in sortInfoStrings)
        {
            var parts = sortInfoString.Split(_PartsSeparator, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 0)
            {
                continue;
            }

            var sortInfo = new SortInfo
            {
                SortColumn = parts[0],
                SortDirection =
                    parts.Length == 1 ? SortDirection.Ascending
                    : parts[1].StartsWith("desc", StringComparison.InvariantCultureIgnoreCase)
                        ? SortDirection.Descending
                    : SortDirection.Ascending,
            };

            retVal.Add(sortInfo);
        }

        return retVal;
    }

    public override string ToString()
    {
        return SortColumn + ":" + (SortDirection == SortDirection.Descending ? "desc" : "asc");
    }

    public static string ToString(IEnumerable<SortInfo> sortInfos)
    {
        return string.Join(';', sortInfos);
    }

    public bool Equals(SortInfo? other)
    {
        return other is not null
            && string.Equals(SortColumn, other.SortColumn, StringComparison.OrdinalIgnoreCase)
            && SortDirection == other.SortDirection;
    }

    public override bool Equals(object? obj)
    {
        return obj is SortInfo other ? Equals(other) : ReferenceEquals(this, obj);
    }

    public override int GetHashCode()
    {
        return StringComparer.Ordinal.GetHashCode(SortColumn);
    }
}
