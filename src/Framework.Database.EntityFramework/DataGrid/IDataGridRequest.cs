using Framework.Database.EntityFramework.DataGrid.Ordering;
using Framework.Database.EntityFramework.DataGrid.Pagination;

namespace Framework.Database.EntityFramework.DataGrid;

public interface IDataGridRequest : IIndexPageRequest, IMultiOrdersListRequest;

public abstract class DataGridRequest : IDataGridRequest
{
    public int Index { get; init; }

    public int Size { get; init; }

    public Orders? Orders { get; init; }
}
