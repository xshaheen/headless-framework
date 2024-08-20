using Framework.Orm.EntityFramework.DataGrid.Ordering;
using Framework.Orm.EntityFramework.DataGrid.Pagination;

namespace Framework.Orm.EntityFramework.DataGrid;

public interface IDataGridRequest : IIndexPageRequest, IMultiOrdersListRequest;

public abstract class DataGridRequest : IDataGridRequest
{
    public int Index { get; init; }

    public int Size { get; init; }

    public Orders? Orders { get; init; }
}
