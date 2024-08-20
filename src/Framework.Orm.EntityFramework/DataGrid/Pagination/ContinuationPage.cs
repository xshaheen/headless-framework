namespace Framework.Orm.EntityFramework.DataGrid.Pagination;

public sealed class ContinuationPage<T>(IReadOnlyList<T> items, int size, string? continuationToken)
{
    public IReadOnlyList<T> Items { get; } = items;

    public int Size { get; } = size;

    public string? ContinuationToken { get; } = continuationToken;
}

public abstract class ContinuationPageRequest
{
    public string? ContinuationToken { get; init; }

    public int Size { get; init; }
}
