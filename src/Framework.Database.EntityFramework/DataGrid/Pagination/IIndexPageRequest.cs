namespace Framework.Database.EntityFramework.DataGrid.Pagination;

public interface IIndexPageRequest
{
    public int Index { get; }

    public int Size { get; }
}

public abstract class IndexPageRequest : IIndexPageRequest
{
    public int Index { get; init; }

    public int Size { get; init; } = 25;
}
